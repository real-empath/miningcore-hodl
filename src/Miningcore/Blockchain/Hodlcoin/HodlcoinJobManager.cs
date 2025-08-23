using Autofac;
using Miningcore.Blockchain.Hodlcoin;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Time;
using System.Threading;
using System.Threading.Tasks;

namespace Miningcore.Blockchain.Hodlcoin
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

        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            // Same as BitcoinJobManagerBase unless we add Hodl-specific fields
            base.SetupJobParamsForStratum(job);
        }

        protected override async Task<(HodlcoinJob job, bool force)> UpdateJob(
            bool viaOverride, string via = null, string data = null, CancellationToken ct = default)
        {
            var (job, force) = await base.UpdateJob(viaOverride, via, data, ct);

            if (job?.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate); // Hodl-specific field

            return (job, force);
        }
    }
}
