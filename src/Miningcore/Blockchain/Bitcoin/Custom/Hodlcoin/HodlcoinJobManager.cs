using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Time;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    /// <summary>
    /// Minimal manager that uses HodlcoinJob so headers are 88 bytes.
    /// No dependencies on BitcoinTemplate or any pay/masternode extras.
    /// </summary>
    public class HodlcoinJobManager : BitcoinJobManagerBase<HodlcoinJob>
    {
        public HodlcoinJobManager(IComponentContext ctx,
                                  IMasterClock clock,
                                  IMessageBus messageBus,
                                  IExtraNonceProvider extraNonceProvider)
            : base(ctx, clock, messageBus, extraNonceProvider)
        { }

        /// <summary>
        /// Ensure the base class constructs a HodlcoinJob.
        /// This avoids overriding complex UpdateJob() signatures that change across versions.
        /// </summary>
        protected override HodlcoinJob CreateJob() => new HodlcoinJob();

        /// <summary>
        /// After the template is loaded, capture optional birthdays (if daemon provides them).
        /// </summary>
        protected override void SetupJobParamsForStratum(HodlcoinJob job)
        {
            base.SetupJobParamsForStratum(job);

            if (job.BlockTemplate != null)
                job.SetBirthdaysFromTemplate(job.BlockTemplate);
        }
    }
}
