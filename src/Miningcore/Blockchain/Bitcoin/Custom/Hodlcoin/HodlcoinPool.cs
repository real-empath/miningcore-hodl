using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using NLog;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    /// <summary>
    /// Keep all Bitcoin pool plumbing. Only swap in our HodlcoinJobManager (which emits 88B headers).
    /// No [CoinFamily] attribute here — your coins.json keeps family=bitcoin.
    /// </summary>
    public class HodlcoinPool : BitcoinPool
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
            NicehashService nicehashService)
            : base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
        { }

        /// <summary>
        /// Use HodlcoinJobManager so jobs are HodlcoinJob (88B header).
        /// </summary>
        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<HodlcoinJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider),
                    new BitcoinExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);
            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs
                    .Select(job => Observable.FromAsync(() =>
                        Guard(() => OnNewJobAsync(job),
                            ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                    .Concat()
                    .Subscribe(_ => { }, ex =>
                    {
                        logger.Debug(ex, nameof(OnNewJobAsync));
                    }));

                // initial blocktemplate
                await manager.Jobs.Take(1).ToTask(ct);
            }
            else
            {
                disposables.Add(manager.Jobs.Subscribe());
            }
        }
    }
}
