using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Stratum;
using Miningcore.Time;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Miningcore.Blockchain.Hodlcoin
    public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
    {
        public HodlcoinJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, clock, messageBus, extraNonceProvider)
        { }

        #region Setup job params (for Stratum)

        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            // Standard Bitcoin params
            base.SetupJobParamsForStratum(job);

            // Inject Hodlcoin-specific "birthdays" if template provided them
            job.SetBirthdaysFromTemplate(job.BlockTemplate);

            // You could extend here if you want to send "birthdayA"/"birthdayB"
            // down to miners as part of mining.notify params
        }

        #endregion

        #region UpdateJob (called on new blocktemplate)

        protected override async Task<(HodlcoinJob job, bool force)> UpdateJob(
            bool viaOverride,
            string via = null,
            string data = null,
            CancellationToken ct = default)
        {
            var (job, force) = await base.UpdateJob(viaOverride, via, data, ct);

            // After template is fetched, hydrate birthdays
            if (job?.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate);

            return (job, force);
        }

        #endregion

        #region Stratum Submit override

        // Optional: this is where you’d intercept shares and feed birthdays in
        public async Task<object> SubmitShareAsync(StratumConnection worker,
            HodlcoinWorkerContext context,
            string jobId, string extraNonce2, string nTime, string nonce,
            string birthdayA = null, string birthdayB = null)
        {
            if (!currentJobs.TryGetValue(jobId, out var job))
                throw new StratumException(StratumError.Other, "job not found");

            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, birthdayA, birthdayB);

            // record stats / relay share
            await OnSubmitShareAsync(worker, context, share, blockHex);

            return share;
        }

        #endregion
    }
}
