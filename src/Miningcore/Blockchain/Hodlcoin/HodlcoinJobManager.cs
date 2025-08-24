using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using NLog;

namespace Miningcore.Blockchain.Hodlcoin;

/// <summary>
/// HODLCoin job manager. Creates HodlcoinJob (not BitcoinJob) so we can build 88-byte headers.
/// </summary>
public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    // --- minimal override to expose job params in mining.notify ---
    protected override object GetJobParamsForStratum(bool isNew)
        => currentJob?.GetJobParams(isNew);

    // --- create/refresh job (based on BitcoinJobManager.UpdateJob, trimmed to essentials for PoW) ---
    protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate)
    {
        // getblocktemplate
        var (capabilities, rules) = GetCapabilities();

        var request = new
        {
            mode = "template",
            capabilities,
            rules
        };

        var response = await GetBlockTemplateAsync(ct, request);
        if (response.Error != null)
        {
            logger.Warn(() => $"getblocktemplate returned error: {response.Error.Message} (code: {response.Error.Code})");
            return (false, false);
        }

        var blockTemplate = response.Response;
        if (blockTemplate == null)
            return (false, false);

        // ignore PoS templates – HODL is PoW
        var isPoS = false;

        // Build a new Hodl job
        var job = new HodlcoinJob();

        // share multiplier & hashers are set up in base.Configure()
        var shareMultiplier = coin.ShareMultiplier;

        // network & pool destination are prepared in base.Configure()
        job.Init(
            blockTemplate,
            NextJobId(),
            PoolConfig,
            ExtraPoolConfig,
            ClusterConfig,
            clock,
            poolAddressDestination,
            network,
            isPoS,
            shareMultiplier,
            coinbaseHasher,
            headerHasher,
            blockHasher);

        // seed birthdays from template extras (used if miner does not supply them)
        job.SetBirthdaysFromTemplate(blockTemplate);

        // atomically rotate
        lock (jobLock)
        {
            // keep a few valid jobs for in-flight shares
            validJobs.Insert(0, job);
            while (validJobs.Count > maxActiveJobs)
                validJobs.RemoveAt(validJobs.Count - 1);

            currentJob = job;
        }

        // announce new job to stratum clients
        var isNew = true;
        return (isNew, forceUpdate);
    }

    /// <summary>
    /// Accepts standard 5 submit params, plus optional birthdayA/birthdayB hex params.
    /// Layout:
    /// [ workerName, jobId, extraNonce2, nTime, nonce, (optional versionBits), (optional birthdayA), (optional birthdayB) ]
    /// </summary>
    public override async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(submission);

        var context = worker.ContextAs<BitcoinWorkerContext>();

        // parse params
        var submit = (object[]) submission;

        if(submit.Length < 5)
            throw new StratumException(StratumError.MinusOne, "malformed submit");

        var workerName = (string) submit[0];
        var jobId      = (string) submit[1];
        var extraNonce2= (string) submit[2];
        var nTime      = (string) submit[3];
        var nonce      = (string) submit[4];

        string? versionBits = null;
        uint? birthdayA = null, birthdayB = null;

        // optional fields
        if(submit.Length >= 6)
        {
            // first optional can be versionBits or directly birthdayA (if miner has no version-rolling)
            var sixth = submit[5]?.ToString();

            // heuristic: version-bits are variable-length hex but birthday values are always 8 hex chars
            if(!string.IsNullOrEmpty(sixth) && sixth!.Length != 8)
            {
                versionBits = sixth;
                if(submit.Length >= 7 && submit[6] != null)
                    birthdayA = Convert.ToUInt32(submit[6].ToString(), 16);
                if(submit.Length >= 8 && submit[7] != null)
                    birthdayB = Convert.ToUInt32(submit[7].ToString(), 16);
            }
            else
            {
                if(!string.IsNullOrEmpty(sixth))
                    birthdayA = Convert.ToUInt32(sixth, 16);

                if(submit.Length >= 7 && submit[6] != null)
                    birthdayB = Convert.ToUInt32(submit[6].ToString(), 16);
            }
        }

        // find job (current or one of recent valid)
        HodlcoinJob? job;
        lock (jobLock)
        {
            job = currentJob?.JobId == jobId
                ? currentJob
                : validJobs.FirstOrDefault(x => x.JobId == jobId);
        }

        if(job == null)
            throw new StratumException(StratumError.MinusOne, "job not found");

        // map miner -> worker name
        context.Worker = workerName;

        // if miner supplied birthdays, set them for this submission
        if(birthdayA.HasValue && birthdayB.HasValue)
            job.SetBirthdays(birthdayA.Value, birthdayB.Value);

        // process
        var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, versionBits);

        // enrich share
        share.PoolId = PoolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint?.Address?.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;

        // submit found block
        if(share.IsBlockCandidate && !string.IsNullOrEmpty(blockHex))
        {
            await SubmitBlockAsync(ct, blockHex);
            OnBlockFound();
        }

        return share;
    }
}
