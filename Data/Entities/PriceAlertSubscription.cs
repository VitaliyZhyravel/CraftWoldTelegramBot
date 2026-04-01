namespace WebApplication1.Data.Entities;

public sealed class PriceAlertSubscription
{
    public int Id { get; set; }

    public int TelegramUserId { get; set; }

    public int TrackedPoolId { get; set; }

    public decimal ThresholdPercent { get; set; }

    public decimal BasePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAlertedAtUtc { get; set; }

    public TelegramUser TelegramUser { get; set; } = null!;

    public TrackedPool TrackedPool { get; set; } = null!;
}
