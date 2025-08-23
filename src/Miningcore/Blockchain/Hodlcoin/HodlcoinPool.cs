using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;

namespace Miningcore.Blockchain.Hodlcoin
    [CoinFamily(CoinFamily.Hodlcoin)]
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
            coin = pc.Template.As<HodlcoinTemplate>();  // use custom template
            base.Configure(pc, cc);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<HodlcoinJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs
                    .Select(job => Observable.FromAsync(() =>
                        Guard(() => OnNewJobAsync(job),
                            ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                    .Concat()
                    .Subscribe());

                // Kick off with initial blocktemplate
                await manager.Jobs.Take(1).ToTask(ct);
            }
        }

        protected override WorkerContextBase CreateWorkerContext()
        {
            return new BitcoinWorkerContext(); // works since Hodl uses Bitcoin-like protocol
        }

        // OPTIONAL but often needed: expose stats
        public override double HashrateFromShares(double shares, double interval)
        {
            // Hodlcoin uses SHA256 shares (same as Bitcoin)
            return base.HashrateFromShares(shares, interval);
        }

        // OPTIONAL — handle new jobs (if not already handled in PoolBase)
        protected virtual Task OnNewJobAsync(HodlcoinJob job)
        {
            logger.Debug(() => $"Broadcasting new Hodlcoin job {job.JobId}");
            return Task.CompletedTask;
        }
    }
}
