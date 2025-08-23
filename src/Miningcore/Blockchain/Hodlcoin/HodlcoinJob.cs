using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using System;

namespace Miningcore.Blockchain.Hodlcoin
{
    public class HodlcoinJob : BitcoinJob
    {
        // Hodlcoin-specific birthdays
        public uint BirthdayA { get; private set; }
        public uint BirthdayB { get; private set; }

        public void SetBirthdaysFromTemplate(BlockTemplate blockTemplate)
        {
            // If daemon includes Hodlcoin-specific birthday fields, extract them here
            if (blockTemplate.Extra != null)
            {
                if (blockTemplate.Extra.TryGetValue("birthdayA", out var a))
                    BirthdayA = Convert.ToUInt32(a);

                if (blockTemplate.Extra.TryGetValue("birthdayB", out var b))
                    BirthdayB = Convert.ToUInt32(b);
            }
        }

        // Override if Hodlcoin uses 88-byte headers
        public override int HeaderSize => 88;
    }
}
