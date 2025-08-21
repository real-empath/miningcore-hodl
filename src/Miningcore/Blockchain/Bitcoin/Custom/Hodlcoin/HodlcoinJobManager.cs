using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Mining;
using Miningcore.Time;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
    {
        public HodlcoinJobManager(IComponentContext ctx, IMasterClock clock, IMessageBus messageBus, IExtraNonceProvider extraNonceProvider)
            : base(ctx, clock, messageBus, extraNonceProvider)
        { }

        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            // Same as BitcoinJobManagerBase default (notify fields do not need birthdays if they’re fixed per job).
            base.SetupJobParamsForStratum(job);
        }

        protected override async Task<(HodlcoinJob job, bool force)> UpdateJob(bool viaOverride, string via = null, string data = null, CancellationToken ct = default)
        {
            var (job, force) = await base.UpdateJob(viaOverride, via, data, ct);

            // Pull per-job birthdays from GBT (if provided)
            if (job?.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate);

            return (job, force);
        }
    }
}
