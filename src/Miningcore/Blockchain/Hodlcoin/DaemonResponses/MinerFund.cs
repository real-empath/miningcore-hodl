using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Hodlcoin.DaemonResponses
{
    public class MinerFundTemplateExtra
    {
        public string[] Addresses { get; set; }
        public ulong MinimumValue { get; set; }
    }
}
