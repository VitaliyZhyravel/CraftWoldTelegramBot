namespace WebApplication1.Models;

public sealed class AlertPersistenceUpdate
{
    public required int SubscriptionId { get; init; }

    public required decimal BasePrice { get; init; }

    public required DateTime AlertedAtUtc { get; init; }
}
