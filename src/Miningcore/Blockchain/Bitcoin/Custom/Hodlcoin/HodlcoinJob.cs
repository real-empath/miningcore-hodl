using System;
using System.Buffers.Binary;
using System.Globalization;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Stratum;
using NBitcoin;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    /// <summary>
    /// Bitcoin-family job with an 88-byte header for HODLcoin.
    /// The pool still uses SHA256d; the miner does the AES/birthday search.
    /// </summary>
    public class HodlcoinJob : BitcoinJob
    {
        // Optional per-job defaults (can be set later via manager if you want)
        private uint birthdayA;
        private uint birthdayB;

        /// <summary>
        /// If you later want to pull these from GBT extras, call this from your job-manager.
        /// </summary>
        public void SetBirthdaysFromTemplate(BlockTemplate tpl)
        {
            // Example tolerant readers; keep zeros if not provided
            birthdayA = TryGetUintExtra(tpl, "birthdayA") ?? TryGetUintExtra(tpl, "nBirthdayA") ?? 0u;
            birthdayB = TryGetUintExtra(tpl, "birthdayB") ?? TryGetUintExtra(tpl, "nBirthdayB") ?? 0u;
        }

        private static uint? TryGetUintExtra(BlockTemplate tpl, string key)
        {
            if (tpl?.Extra != null && tpl.Extra.TryGetValue(key, out var tok))
            {
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    return (uint) tok.Value<long>();
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.String &&
                    uint.TryParse(tok.Value<string>(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    return v;
            }
            return null;
        }

        /// <summary>
        /// Build HODLcoin's 88-byte header:
        /// [version|prevhash|merkleroot|time|bits|nonce|birthdayA|birthdayB] (all LE)
        /// </summary>
        private byte[] SerializeHeader88(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            // Merkle root from coinbase + tx tree (internal byte order)
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray()); // byte[32]

            // Version (support overt ASIC-boost if mask/bits provided)
            var version = BlockTemplate.Version;
            if (versionMask.HasValue && versionBits.HasValue)
                version = (int)((version & ~versionMask.Value) | (versionBits.Value & versionMask.Value));

            // Prev-hash (internal byte order)
            var prevHash = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();

            // Bits (compact)
            var bitsCompact = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)).ToCompact();

            var header = new byte[88];
            var span = header.AsSpan();

            // 0..3   version
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), version);

            // 4..35  prevhash
            prevHash.CopyTo(span.Slice(4, 32));

            // 36..67 merkle
            merkleRoot.CopyTo(span.Slice(36, 32));

            // 68..71 nTime
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(68, 4), nTime);

            // 72..75 bits
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(72, 4), bitsCompact);

            // 76..79 nonce
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(76, 4), nonce);

            // 80..83 birthdayA
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(80, 4), birthdayA);

            // 84..87 birthdayB
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(84, 4), birthdayB);

            return header;
        }

        /// <summary>
        /// Critical: override the single extension point the base class provides for share processing.
        /// This lets us swap in the 88-byte header while keeping the rest of the Bitcoin pipeline intact.
        /// </summary>
        protected override (Share Share, string BlockHex) ProcessShareInternal(
            StratumConnection worker, string extraNonce2, uint nTime, uint nonce, uint? versionBits)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;

            // 1) coinbase
            var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
            Span<byte> coinbaseHash = stackalloc byte[32];
            coinbaseHasher.Digest(coinbase, coinbaseHash);

            // 2) header (HODLcoin 88 bytes)
            var headerBytes = SerializeHeader88(coinbaseHash, nTime, nonce, context.VersionRollingMask, versionBits);

            // 3) header hash (still SHA256d or coin-specific hasher as configured)
            Span<byte> headerHash = stackalloc byte[32];
            headerHasher.Digest(headerBytes, headerHash, (ulong)nTime, BlockTemplate, coin, networkParams);
            var headerValue = new uint256(headerHash);

            // 4) difficulty checks (unchanged from base)
            var shareDiff = (double)new BigRational(BitcoinConstants.Diff1, headerHash.ToBigInteger()) * shareMultiplier;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

            var isBlockCandidate = headerValue <= blockTargetValue;

            if (!isBlockCandidate && ratio < 0.99)
            {
                if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                    stratumDifficulty = context.PreviousDifficulty.Value;
                }
                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty / shareMultiplier,
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;

                Span<byte> blockHash = stackalloc byte[32];
                blockHasher.Digest(headerBytes, blockHash, nTime);
                result.BlockHash = blockHash.ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbase);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }
    }
}
