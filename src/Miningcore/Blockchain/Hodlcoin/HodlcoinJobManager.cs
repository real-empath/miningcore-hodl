using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Stratum;
using Miningcore.Time;
using System.Threading;
using System.Threading.Tasks;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
    {
        public HodlcoinJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, clock, messageBus, extraNonceProvider)
        { }

        // REQUIRED override — construct HodlcoinJob
        protected override HodlcoinJob CreateJob()
        {
            return new HodlcoinJob();
        }

        // REQUIRED override — pass configs to base
        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);
        }

        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            base.SetupJobParamsForStratum(job);

            // Hodlcoin-specific birthdays
            job.SetBirthdaysFromTemplate(job.BlockTemplate);
        }

        protected override async Task<(HodlcoinJob job, bool force)> UpdateJob(
            bool viaOverride,
            string via = null,
            string data = null,
            CancellationToken ct = default)
        {
            var (job, force) = await base.UpdateJob(viaOverride, via, data, ct);

            if (job?.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate);

            return (job, force);
        }

        // Hodlcoin Stratum share submission
        public async Task<object> SubmitShareAsync(StratumConnection worker,
            HodlcoinWorkerContext context,
            string jobId, string extraNonce2, string nTime, string nonce,
            string birthdayA = null, string birthdayB = null)
        {
            if (!currentJobs.TryGetValue(jobId, out var job))
                throw new StratumException(StratumError.Other, "job not found");

            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, birthdayA, birthdayB);

            await OnSubmitShareAsync(worker, context, share, blockHex);

            return share;
        }
    }
}
