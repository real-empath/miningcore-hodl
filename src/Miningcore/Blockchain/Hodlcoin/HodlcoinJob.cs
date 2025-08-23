using System.Buffers.Binary;
using System.Text;
using Miningcore.Blockchain.Hodlcoin;
using Miningcore.Blockchain.Hodlcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Stratum;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Hodlcoin
{
    /// <summary>
    /// Bitcoin-family job with 88-byte header (HODLcoin).
    /// </summary>
    public class HodlcoinJob : BitcoinJob
    {
        // The two extra 4-byte fields appended to the standard 80-byte header
        // Defaults come from the job template, but can be overridden by miner submit params.
        private uint birthdayA;
        private uint birthdayB;

        public void SetBirthdaysFromTemplate(BlockTemplate tpl)
        {
            // Be liberal about possible naming in getblocktemplate:
            // e.g. "birthdayA"/"birthdayB", "nBirthdayA"/"nBirthdayB", or lower-case variants.
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
            if (tpl.Extra is JObject jo && jo.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                if (tok.Type == JTokenType.Integer)
                    return (uint) tok.Value<long>();
                if (tok.Type == JTokenType.String && uint.TryParse(tok.Value<string>(), out var v))
                    return v;
            }
            return null;
        }

        /// <summary>
        /// Override to serialize an 88-byte header:
        /// [version|prevhash|merkleroot|time|bits|nonce|birthdayA|birthdayB]
        /// All fields are little-endian; prevhash and merkleroot are internal byte order (reversed from hex).
        /// </summary>
        protected override byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            // Build merkle root with the real coinbase first
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());  // byte[32] internal order

            // version (possibly version-rolled)
            var version = BlockTemplate.Version;
            if (versionMask.HasValue && versionBits.HasValue)
                version = (int) ((version & ~versionMask.Value) | (versionBits.Value & versionMask.Value));

            // prevhash (internal bytes)
            var prevHash = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();

            // bits: compact as uint (little-endian write)
            // BlockTemplate.Bits is hex (e.g. "1d00ffff"). Parse to uint.
            var bitsCompact = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)).ToCompact();

            var header = new byte[88];
            var span = header.AsSpan();

            // 0..3 version
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), version);

            // 4..35 prevhash (internal)
            prevHash.CopyTo(span.Slice(4, 32));

            // 36..67 merkle (internal)
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
        /// Accept optional birthdayA/birthdayB as extra submit params: 
        /// submit: [worker, jobId, extraNonce2, nTime, nonce, birthdayA?, birthdayB?]
        /// If not supplied, we use the job's template values.
        /// </summary>
        public (Share Share, string BlockHex) ProcessShare(StratumConnection worker,
                                                           string extraNonce2, string nTime, string nonce,
                                                           string? birthdayAHex = null, string? birthdayBHex = null)
        {
            // Optional miner-supplied birthdays (hex LE)
            if (!string.IsNullOrEmpty(birthdayAHex) && birthdayAHex.Length <= 8)
                birthdayA = Convert.ToUInt32(birthdayAHex, 16);
            if (!string.IsNullOrEmpty(birthdayBHex) && birthdayBHex.Length <= 8)
                birthdayB = Convert.ToUInt32(birthdayBHex, 16);

            // Defer to BitcoinJob for the rest of the share pipeline (difficulty checks, block candidate, etc.)
            return base.ProcessShare(worker, extraNonce2, nTime, nonce);
        }
    }
}
