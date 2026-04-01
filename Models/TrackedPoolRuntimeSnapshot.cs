namespace WebApplication1.Models;

public sealed class TrackedPoolRuntimeSnapshot
{
    public required int TrackedPoolId { get; init; }

    public required string PoolAddress { get; init; }

    public required string Token0Symbol { get; init; }

    public required string Token1Symbol { get; init; }

    public decimal? CurrentPrice { get; init; }

    public decimal? CurrentInversePrice { get; init; }

    public int? CurrentTick { get; init; }

    public IReadOnlyCollection<AlertSubscriptionRuntimeSnapshot> Subscriptions { get; init; } = [];
}
