using System;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    public class HodlcoinJob : BitcoinJob
    {
        public HodlcoinJob(BlockTemplate blockTemplate, string jobId, PoolConfig poolConfig, ClusterConfig clusterConfig,
            IMasterClock clock, IDestinationAddressResolver addressResolver, IExtraNonceProvider extraNonceProvider) :
            base(blockTemplate, jobId, poolConfig, clusterConfig, clock, addressResolver, extraNonceProvider)
        {
        }

        // Hodlcoin uses an 88-byte header instead of 80
        public override void SerializeHeader(Span<byte> span, uint nTime, uint nonce, uint nBits, uint? version)
        {
            if(span.Length < 88)
                throw new ArgumentException("Span too small for Hodlcoin header");

            // Call base implementation for Bitcoin header serialization
            base.SerializeHeader(span, nTime, nonce, nBits, version);

            // Extend by 8 bytes for Hodlcoin-specific header fields
            // (TODO: fill these bytes correctly according to Hodlcoin spec)
            span.Slice(80, 8).Clear();
        }
    }
}
