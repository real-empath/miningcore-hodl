using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Time;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
    {
        public HodlcoinJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider)
            : base(ctx, clock, messageBus, extraNonceProvider)
        { }

        // This is the only override you must have so the base constructs HodlcoinJob
        protected override HodlcoinJob CreateJob() => new();

        // Pick up birthdays from template (no Stratum shape changes needed)
        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            base.SetupJobParamsForStratum(job);
            if (job.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate);
        }
    }
}
