using Autofac;
using Miningcore.Blockchain.Hodlcoin;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.IO;
using AutoMapper;

namespace Miningcore.Blockchain.Hodlcoin
{
    [CoinFamily(CoinFamily.Bitcoin)] // Hodlcoin is Bitcoin-family
    public class HodlcoinPool : PoolBase
    {
        public HodlcoinPool(
            IComponentContext ctx,
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

        protected object currentJobParams;
        protected HodlcoinJobManager manager;
        private HodlcoinTemplate coin;

        public override void Configure(PoolConfig pc, ClusterConfig cc)
        {
            coin = pc.Template.As<HodlcoinTemplate>();  // <-- Hodlcoin template
            base.Configure(pc, cc);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<HodlcoinJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if(poolConfig.EnableInternalStratum == true)
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
            return new BitcoinWorkerContext(); // Hodlcoin uses Bitcoin-like worker context
        }

        // Needed because PoolBase / StratumServer requires this
        protected override async Task OnRequestAsync(StratumConnection connection,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            await base.OnRequestAsync(connection, tsRequest, ct);
        }
    }
}
