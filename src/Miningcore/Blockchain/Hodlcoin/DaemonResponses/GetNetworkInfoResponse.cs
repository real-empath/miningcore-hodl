namespace Miningcore.Blockchain.Hodlcoin.DaemonResponses;

public class NetworkInfo
{
    public string Version { get; set; }
    public string SubVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public bool LocalRelay { get; set; }
    public bool NetworkActive { get; set; }
    public int Connections { get; set; }
}
