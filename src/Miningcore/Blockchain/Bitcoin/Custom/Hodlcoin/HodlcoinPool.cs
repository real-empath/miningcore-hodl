using Autofac;
using Newtonsoft.Json;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using System.IO;
using System.Reactive.Linq;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    [CoinFamily(CoinFamily.Bitcoin)] // family still Bitcoin
    public class HodlcoinPool : PoolBase
    {
        public HodlcoinPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            RecyclableMemoryStreamManager rmsm,
            NicehashService nicehashService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
        {
        }

        private HodlcoinJobManager manager;

        public override void Configure(PoolConfig pc, ClusterConfig cc)
        {
            base.Configure(pc, cc);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<HodlcoinJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if(poolConfig.EnableInternalStratum)
            {
                disposables.Add(manager.Jobs
                    .Select(job => Observable.FromAsync(() =>
                        Guard(() => OnNewJobAsync(job),
                            ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                    .Concat()
                    .Subscribe());

                // start with initial blocktemplate
                await manager.Jobs.Take(1).ToTask(ct);
            }
        }

        protected override WorkerContextBase CreateWorkerContext()
        {
            return new BitcoinWorkerContext();
        }
    }
}
