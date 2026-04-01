namespace WebApplication1.Models;

public sealed class AlertSubscriptionRuntimeSnapshot
{
    public required int SubscriptionId { get; init; }

    public required int TelegramUserId { get; init; }

    public required long ChatId { get; init; }

    public required decimal ThresholdPercent { get; init; }

    public required decimal BasePrice { get; init; }
}
