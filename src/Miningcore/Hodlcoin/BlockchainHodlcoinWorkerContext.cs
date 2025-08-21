using Miningcore.Mining;

namespace Miningcore.Blockchain.Hodlcoin;

public class HodlcoinWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    /// <summary>
    /// Unique value assigned per worker
    /// </summary>
    public string ExtraNonce1 { get; set; }
    
    ///<summary>
    /// Unique value assigned for the HOdl 1gb aes pattern search start location.
    ///</summary>
    public uint32 StartLocation { get; set; }
    
    ///<summary>
    /// Unique value assigned for the HOdl 1gb aes pattern search final calculation.
    ///</summary>
    public uint32 FinalCalculation { get; set; } 

    /// <summary>
    /// Mask for version-rolling (Overt ASIC-Boost)
    /// </summary>
    public uint? VersionRollingMask { get; internal set; }
}
