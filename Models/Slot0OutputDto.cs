using Nethereum.ABI.FunctionEncoding.Attributes;

namespace WebApplication1.Models;

[FunctionOutput]
public sealed class Slot0OutputDto : IFunctionOutputDTO
{
    [Parameter("uint160", "sqrtPriceX96", 1)]
    public System.Numerics.BigInteger SqrtPriceX96 { get; set; }

    [Parameter("int24", "tick", 2)]
    public int Tick { get; set; }

    [Parameter("uint16", "observationIndex", 3)]
    public ushort ObservationIndex { get; set; }

    [Parameter("uint16", "observationCardinality", 4)]
    public ushort ObservationCardinality { get; set; }

    [Parameter("uint16", "observationCardinalityNext", 5)]
    public ushort ObservationCardinalityNext { get; set; }

    [Parameter("uint8", "feeProtocol", 6)]
    public byte FeeProtocol { get; set; }

    [Parameter("bool", "unlocked", 7)]
    public bool Unlocked { get; set; }
}
