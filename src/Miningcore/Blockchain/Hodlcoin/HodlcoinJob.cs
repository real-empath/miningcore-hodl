using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Hodlcoin;

/// <summary>
/// Bitcoin-like job with HODL’s 88-byte header (80-byte standard header + 4-byte birthdayA + 4-byte birthdayB).
/// </summary>
public class HodlcoinJob : BitcoinJob
{
    // birthdays default to template extras if miner doesn’t send them on submit
    private uint birthdayA;
    private uint birthdayB;

    public void SetBirthdaysFromTemplate(BlockTemplate tpl)
    {
        // Accept both string and uint JToken shapes if present
        if (tpl.Extras is JObject extras)
        {
            if (extras.TryGetValue("nBirthdayA", out var a))
            {
                if (a.Type == JTokenType.String)
                    birthdayA = Convert.ToUInt32((string) a, 16);
                else if (a.Type == JTokenType.Integer)
                    birthdayA = (uint) a;
            }
            if (extras.TryGetValue("nBirthdayB", out var b))
            {
                if (b.Type == JTokenType.String)
                    birthdayB = Convert.ToUInt32((string) b, 16);
                else if (b.Type == JTokenType.Integer)
                    birthdayB = (uint) b;
            }
        }
    }

    public void SetBirthdays(uint a, uint b)
    {
        birthdayA = a;
        birthdayB = b;
    }

    /// <summary>
    /// Same as BitcoinJob.ProcessShareInternal except the header we hash/serialize is 88 bytes with birthdays appended.
    /// </summary>
    protected override (Share Share, string BlockHex) ProcessShareInternal(
        StratumConnection worker,
        string extraNonce2,
        uint nTime,
        uint nonce,
        uint? versionBits)
    {
        var context = worker.ContextAs<BitcoinWorkerContext>();

        // --- 1) coinbase & merkle ---
        var coinbaseBytes = SerializeCoinbase(context.ExtraNonce1, extraNonce2);
        Span<byte> coinbaseHash = stackalloc byte[32];
        coinbaseHasher.Digest(coinbaseBytes, coinbaseHash);

        var mt = GetMerkleRoot(coinbaseHash.ToNewArray());
        var merkleRoot = mt;

        // --- 2) version (handle version-rolling if present) ---
        var version = BlockTemplate.Version;
        if(versionBits.HasValue && context.VersionRollingMask.HasValue)
            version = (int)((uint)version & ~context.VersionRollingMask.Value | (versionBits.Value & context.VersionRollingMask.Value));

        // --- 3) build 88-byte header (LE fields) ---
        // prevhash (internal little-endian bytes)
        var prevHashBytes = uint256.Parse(BlockTemplate.PreviousBlockhash).ToBytes();

        // compact bits (LE)
        var bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits));
        var bitsCompact = bits.ToCompact();

        // standard 80-byte header
        Span<byte> header = stackalloc byte[88];
        var ofs = 0;

        // version
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(ofs, 4), version); ofs += 4;

        // prevhash (32 bytes, already internal order)
        prevHashBytes.CopyTo(header.Slice(ofs, 32)); ofs += 32;

        // merkle root (LE)
        var merkleLe = merkleRoot.ToBytes(); // internal order
        merkleLe.CopyTo(header.Slice(ofs, 32)); ofs += 32;

        // nTime
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ofs, 4), nTime); ofs += 4;

        // nBits (compact)
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ofs, 4), bitsCompact); ofs += 4;

        // nonce
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ofs, 4), nonce); ofs += 4;

        // birthdays (HODL extension)
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ofs, 4), birthdayA); ofs += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ofs, 4), birthdayB); ofs += 4;

        // --- 4) header hash & share difficulty ---
        Span<byte> headerHash = stackalloc byte[32];
        headerHasher.Digest(header, headerHash, nTime, BlockTemplate, coin, networkParams);

        var shareMultiplier = coin.ShareMultiplier;
        var shareDiff = (double) new BigRational(BitcoinConstants.Diff1, headerHash.ToBigInteger()) * shareMultiplier;

        // current stratum difficulty (adjusted for multiplier when recorded on Share)
        var stratumDifficulty = context.Difficulty;
        var result = new Share
        {
            BlockHeight = BlockTemplate.Height,
            NetworkDifficulty = Difficulty
        };

        // check vs worker target
        var stratumTarget = new BigRational(BitcoinConstants.Diff1, stratumDifficulty * shareMultiplier);
        var headerValue = headerHash.ToBigInteger();
        var meetsShareTarget = headerValue <= stratumTarget;
        if (!meetsShareTarget)
            throw new StratumException(StratumError.LowDifficultyShare, "low difficulty share");

        // --- 5) block-candidate? ---
        var isBlockCandidate = headerValue <= blockTargetValue;
        result.IsBlockCandidate = isBlockCandidate;

        // difficulty to book (same as upstream: record stratumDifficulty / shareMultiplier)
        result.Difficulty = stratumDifficulty / shareMultiplier;

        // --- 6) if block, serialize block using our 88-byte header ---
        string blockHex = null;
        if(isBlockCandidate)
        {
            // Build full block with this exact header (+ coinbase/txs from template)
            var coinbase = coinbaseBytes;
            var blockBytes = SerializeBlock(header, coinbase);
            blockHex = Convert.ToHexString(blockBytes);
        }

        return (result, blockHex);
    }
}
