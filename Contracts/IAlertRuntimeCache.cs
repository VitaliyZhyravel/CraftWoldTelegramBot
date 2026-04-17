using WebApplication1.Data.Entities;
using WebApplication1.Models;

namespace WebApplication1.Contracts;

public interface IAlertRuntimeCache
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    IReadOnlyCollection<TrackedPoolRuntimeSnapshot> GetActivePools();

    IReadOnlyCollection<TrackedPoolSubscriptionInfo> GetUserSubscriptions(int telegramUserId);

    void UpdateTelegramUser(TelegramUser user);

    void UpsertSubscription(TrackedPool trackedPool, PriceAlertSubscription subscription, TelegramUser telegramUser);

    void DeactivateSubscription(int subscriptionId, int trackedPoolId);

    void UpdatePoolState(int trackedPoolId, PoolPriceResult latestPrice, DateTime polledAtUtc);

    void ApplyAlertUpdate(int subscriptionId, decimal newBasePrice, DateTime? alertedAtUtc);
}
