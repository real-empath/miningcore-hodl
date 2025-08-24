using System;
using System.Buffers.Binary;
using System.Globalization;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Stratum;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Miningcore.Persistence.Repositories;   // IStatsRepository lives here


namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    /// 80-byte Bitcoin header + 4-byte birthdayA + 4-byte birthdayB
    public class HodlcoinJob : BitcoinJob
    {
        private uint birthdayA;
        private uint birthdayB;

        public void SetBirthdaysFromTemplate(BlockTemplate tpl)
        {
            if (tpl.Extra is JObject jo)
            {
                if (jo.TryGetValue("nBirthdayA", StringComparison.OrdinalIgnoreCase, out var a))
                    TryEat(a, ref birthdayA);

                if (jo.TryGetValue("nBirthdayB", StringComparison.OrdinalIgnoreCase, out var b))
                    TryEat(b, ref birthdayB);
            }

            static void TryEat(JToken tok, ref uint target)
            {
                if (tok.Type == JTokenType.Integer)
                    target = (uint) tok.Value<long>();
                else if (tok.Type == JTokenType.String &&
                         uint.TryParse(tok.Value<string>(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    target = v;
            }
        }

        protected override byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());
            var version = BlockTemplate.Version;

            if (versionMask.HasValue && versionBits.HasValue)
                version = (int)((version & ~versionMask.Value) | (versionBits.Value & versionMask.Value));

            var prevHash = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();
            var bitsCompact = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)).ToCompact();

            var header = new byte[88];
            var span = header.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], version);
            prevHash.CopyTo(span.Slice(4, 32));
            merkleRoot.CopyTo(span.Slice(36, 32));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(68, 4), nTime);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(72, 4), bitsCompact);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(76, 4), nonce);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(80, 4), birthdayA);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(84, 4), birthdayB);

            return header;
        }

        // Optional helper if miner supplies birthdays on submit
        public void SetBirthdaysFromSubmit(string? aHex, string? bHex)
        {
            if (!string.IsNullOrEmpty(aHex) && aHex.Length <= 8)
                birthdayA = Convert.ToUInt32(aHex, 16);
            if (!string.IsNullOrEmpty(bHex) && bHex.Length <= 8)
                birthdayB = Convert.ToUInt32(bHex, 16);
        }
    }
}
