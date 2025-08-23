using System;
using System.Buffers.Binary;
using System.Globalization;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Miningcore.Blockchain.Hodlcoin
{
    public class HodlcoinJob : BitcoinJob
    {
        private uint birthdayA;
        private uint birthdayB;

        public HodlcoinJob(
            string id,
            BlockTemplate blockTemplate,
            JobParams jobParams,
            Network network,
            IHashAlgorithm coinbaseHasher,
            IDestination poolAddressDestination,
            bool isPoS,
            ClusterConfig clusterConfig,
            string extraNoncePlaceholder,
            IClock clock) :
            base(id, blockTemplate, jobParams, network, coinbaseHasher,
                poolAddressDestination, isPoS, clusterConfig,
                extraNoncePlaceholder, clock)
        {
            // initialize birthdays if present in template
            SetBirthdaysFromTemplate(blockTemplate);
        }

        #region Birthdays from Template

        public void SetBirthdaysFromTemplate(BlockTemplate tpl)
        {
            birthdayA = TryGetUintTemplateExtra(tpl, "birthdayA")
                     ?? TryGetUintTemplateExtra(tpl, "nBirthdayA")
                     ?? TryGetUintTemplateExtra(tpl, "birthday_a")
                     ?? 0u;

            birthdayB = TryGetUintTemplateExtra(tpl, "birthdayB")
                     ?? TryGetUintTemplateExtra(tpl, "nBirthdayB")
                     ?? TryGetUintTemplateExtra(tpl, "birthday_b")
                     ?? 0u;
        }

        private static uint? TryGetUintTemplateExtra(BlockTemplate tpl, string key)
        {
            if (tpl.Extra is JObject jo &&
                jo.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                if (tok.Type == JTokenType.Integer)
                    return (uint)tok.Value<long>();

                if (tok.Type == JTokenType.String &&
                    uint.TryParse(tok.Value<string>(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    return v;
            }

            return null;
        }

        #endregion

        #region Header serialization (88 bytes)

        // Hodlcoin header layout (little-endian fields):
        // [version|prevhash|merkleroot|time|bits|nonce|birthdayA|birthdayB]
        protected override byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce,
            uint? versionMask, uint? versionBits)
        {
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());   // byte[32]

            // version (with rolling mask if active)
            var version = BlockTemplate.Version;
            if (versionMask.HasValue && versionBits.HasValue)
                version = (int)((version & ~versionMask.Value) | (versionBits.Value & versionMask.Value));

            // prev block (internal byte order)
            var prevHash = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();

            // bits (compact)
            var bitsCompact = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)).ToCompact();

            var header = new byte[88];
            var span = header.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), version);     //  0..3
            prevHash.CopyTo(span.Slice(4, 32));                                     //  4..35
            merkleRoot.CopyTo(span.Slice(36, 32));                                  // 36..67
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(68, 4), nTime);     // 68..71
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(72, 4), bitsCompact);// 72..75
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(76, 4), nonce);     // 76..79

            // Hodlcoin-specific extras
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(80, 4), birthdayA); // 80..83
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(84, 4), birthdayB); // 84..87

            return header;
        }

        #endregion

        #region Share processing with optional birthdays

        // Called by HodlcoinJobManager.SubmitShareAsync
        public (Share Share, string BlockHex) ProcessShare(
            StratumConnection worker,
            string extraNonce2, string nTime, string nonce,
            string? birthdayAHex = null, string? birthdayBHex = null)
        {
            // Optional miner-supplied birthdays (hex LE, <= 8 chars)
            if (!string.IsNullOrEmpty(birthdayAHex) && birthdayAHex.Length <= 8)
                birthdayA = Convert.ToUInt32(birthdayAHex, 16);

            if (!string.IsNullOrEmpty(birthdayBHex) && birthdayBHex.Length <= 8)
                birthdayB = Convert.ToUInt32(birthdayBHex, 16);

            // Defer to base (difficulty check, candidate eval, coinbase, etc.)
            return base.ProcessShare(worker, extraNonce2, nTime, nonce);
        }

        #endregion
    }
}
