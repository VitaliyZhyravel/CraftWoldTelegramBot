namespace WebApplication1.Models;

public sealed class TrackedPoolSubscriptionInfo
{
    public required int SubscriptionId { get; init; }

    public required string PoolAddress { get; init; }

    public required string PairLabel { get; init; }

    public required decimal ThresholdPercent { get; init; }

    public required decimal BasePrice { get; init; }

    public decimal? CurrentPrice { get; init; }

    public decimal? CurrentInversePrice { get; init; }
}
