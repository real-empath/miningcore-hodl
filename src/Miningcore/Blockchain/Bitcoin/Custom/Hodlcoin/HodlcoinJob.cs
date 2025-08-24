using System;
using System.Buffers.Binary;
using System.Globalization;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Stratum;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    /// <summary>
    /// Bitcoin-family job with an 88-byte header (80B standard + 4B birthdayA + 4B birthdayB).
    /// Fields are little-endian; prevhash/merkleroot are internal byte order (like Bitcoin).
    /// </summary>
    public class HodlcoinJob : BitcoinJob
    {
        // Per-job defaults (from GBT extras), can be overridden by submit.
        private uint birthdayA;
        private uint birthdayB;

        /// <summary>
        /// Pull optional birthdayA/birthdayB from getblocktemplate extras, if present.
        /// Supports keys: birthdayA/nBirthdayA/birthday_a and birthdayB/nBirthdayB/birthday_b.
        /// </summary>
        public void SetBirthdaysFromTemplate(BlockTemplate tpl)
        {
            birthdayA = TryGetUintExtra(tpl, "birthdayA")
                     ?? TryGetUintExtra(tpl, "nBirthdayA")
                     ?? TryGetUintExtra(tpl, "birthday_a")
                     ?? 0u;

            birthdayB = TryGetUintExtra(tpl, "birthdayB")
                     ?? TryGetUintExtra(tpl, "nBirthdayB")
                     ?? TryGetUintExtra(tpl, "birthday_b")
                     ?? 0u;
        }

        private static uint? TryGetUintExtra(BlockTemplate tpl, string key)
        {
            if (tpl.Extra is JObject jo && jo.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                if (tok.Type == JTokenType.Integer)
                    return (uint) tok.Value<long>();

                if (tok.Type == JTokenType.String)
                {
                    var s = tok.Value<string>();
                    // accept hex or decimal
                    if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                        return hex;
                    if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                        return dec;
                }
            }
            return null;
        }

        /// <summary>
        /// Allow miners to supply birthdays on submit. If not provided, use template defaults.
        /// </summary>
        public void SetBirthdaysFromSubmit(string? aHex, string? bHex)
        {
            if (!string.IsNullOrWhiteSpace(aHex) && aHex.Length <= 8)
                birthdayA = Convert.ToUInt32(aHex, 16);

            if (!string.IsNullOrWhiteSpace(bHex) && bHex.Length <= 8)
                birthdayB = Convert.ToUInt32(bHex, 16);
        }

        /// <summary>
        /// Serialize an 88-byte header:
        /// [version|prevhash|merkleroot|time|bits|nonce|birthdayA|birthdayB]
        /// </summary>
        protected override byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            // Merkle (internal bytes) – reuse base’s merkle tree 'mt'
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());

            // Version (apply version-rolling if requested)
            var version = BlockTemplate.Version;
            if (versionMask.HasValue && versionBits.HasValue)
                version = (int)((version & ~versionMask.Value) | (versionBits.Value & versionMask.Value));

            // Previous block hash (internal order)
            var prevHash = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();

            // Compact bits (uint)
            var bitsCompact = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)).ToCompact();

            var header = new byte[88];
            var span = header.AsSpan();

            // 0..3 version
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), version);

            // 4..35 prevhash
            prevHash.CopyTo(span.Slice(4, 32));

            // 36..67 merkle root
            merkleRoot.CopyTo(span.Slice(36, 32));

            // 68..71 time
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
        /// Convenience method if you want to parse optional birthdays here and then delegate to base.
        /// </summary>
        public (Share Share, string BlockHex) ProcessShare(StratumConnection worker,
                                                           string extraNonce2, string nTime, string nonce,
                                                           string? birthdayAHex = null, string? birthdayBHex = null)
        {
            SetBirthdaysFromSubmit(birthdayAHex, birthdayBHex);
            return base.ProcessShare(worker, extraNonce2, nTime, nonce);
        }
    }
}
