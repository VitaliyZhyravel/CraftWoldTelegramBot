namespace WebApplication1.Data.Entities;

public sealed class TrackedPool
{
    public int Id { get; set; }

    public string PoolAddress { get; set; } = string.Empty;

    public string Token0Address { get; set; } = string.Empty;

    public string Token1Address { get; set; } = string.Empty;

    public string Token0Symbol { get; set; } = string.Empty;

    public string Token1Symbol { get; set; } = string.Empty;

    public decimal? LastKnownPrice { get; set; }

    public decimal? LastKnownInversePrice { get; set; }

    public int? LastKnownTick { get; set; }

    public DateTime? LastPolledAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PriceAlertSubscription> Subscriptions { get; set; } = [];
}
