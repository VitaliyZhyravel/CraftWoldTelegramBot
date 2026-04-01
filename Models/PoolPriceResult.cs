namespace WebApplication1.Models;

public sealed class PoolPriceResult
{
    public required string PoolAddress { get; init; }
    public required string Token0Symbol { get; init; }
    public required string Token1Symbol { get; init; }
    public required string Token0Address { get; init; }
    public required string Token1Address { get; init; }
    public required decimal Price { get; init; }
    public required decimal InversePrice { get; init; }
    public required int Tick { get; init; }
}
