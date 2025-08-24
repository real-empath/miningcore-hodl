using Miningcore.Blockchain.Bitcoin

namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin
{
    public class HodlcoinTemplate : BitcoinTemplate
    {
        public override int HeaderSize => 88;
    }
}
