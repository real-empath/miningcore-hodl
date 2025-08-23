namespace Miningcore.Blockchain.Bitcoin.Custom.Hodlcoin;

public class HodlcoinTemplate : BitcoinTemplate
{
    public override int HeaderSize => 88;   // <-- the important difference
}
