using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Contracts;
using WebApplication1.Data;
using WebApplication1.Data.Entities;
using WebApplication1.Models;

namespace WebApplication1.Services;

public sealed class AlertRuntimeCache : IAlertRuntimeCache
{
    private readonly ConcurrentDictionary<int, CachedTrackedPool> _pools = new();
    private readonly ConcurrentDictionary<int, int> _subscriptionToPoolMap = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertRuntimeCache> _logger;
    private bool _initialized;

    public AlertRuntimeCache(IServiceScopeFactory scopeFactory, ILogger<AlertRuntimeCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var trackedPools = await dbContext.TrackedPools
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Where(x => x.Subscriptions.Any(s => s.IsActive))
                .Include(x => x.Subscriptions.Where(s => s.IsActive))
                .ThenInclude(x => x.TelegramUser)
                .ToListAsync(cancellationToken);

            _pools.Clear();
            _subscriptionToPoolMap.Clear();

            foreach (var trackedPool in trackedPools)
            {
                var cachedPool = CachedTrackedPool.FromEntity(trackedPool);
                _pools[trackedPool.Id] = cachedPool;

                foreach (var subscription in trackedPool.Subscriptions.Where(x => x.IsActive))
                {
                    cachedPool.UpsertSubscription(subscription, subscription.TelegramUser);
                    _subscriptionToPoolMap[subscription.Id] = trackedPool.Id;
                }
            }

            _initialized = true;
            _logger.LogInformation("Loaded {PoolCount} active pools into runtime cache.", _pools.Count);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public IReadOnlyCollection<TrackedPoolRuntimeSnapshot> GetActivePools()
    {
        return _pools.Values
            .Select(x => x.ToRuntimeSnapshot())
            .Where(x => x.Subscriptions.Count > 0)
            .OrderBy(x => x.TrackedPoolId)
            .ToList();
    }

    public IReadOnlyCollection<TrackedPoolSubscriptionInfo> GetUserSubscriptions(int telegramUserId)
    {
        return _pools.Values
            .SelectMany(x => x.ToUserSubscriptionInfos(telegramUserId))
            .OrderBy(x => x.SubscriptionId)
            .ToList();
    }

    public void UpdateTelegramUser(TelegramUser user)
    {
        foreach (var pool in _pools.Values)
        {
            pool.UpdateTelegramUser(user);
        }
    }

    public void UpsertSubscription(TrackedPool trackedPool, PriceAlertSubscription subscription, TelegramUser telegramUser)
    {
        var cachedPool = _pools.AddOrUpdate(
            trackedPool.Id,
            _ => CachedTrackedPool.FromEntity(trackedPool),
            (_, existing) =>
            {
                existing.UpdatePoolMetadata(trackedPool);
                return existing;
            });

        cachedPool.UpsertSubscription(subscription, telegramUser);
        _subscriptionToPoolMap[subscription.Id] = trackedPool.Id;
    }

    public void DeactivateSubscription(int subscriptionId, int trackedPoolId)
    {
        if (_pools.TryGetValue(trackedPoolId, out var cachedPool))
        {
            cachedPool.RemoveSubscription(subscriptionId);
            _subscriptionToPoolMap.TryRemove(subscriptionId, out _);

            if (!cachedPool.HasSubscriptions)
            {
                _pools.TryRemove(trackedPoolId, out _);
            }
        }
    }

    public void UpdatePoolState(int trackedPoolId, PoolPriceResult latestPrice, DateTime polledAtUtc)
    {
        if (_pools.TryGetValue(trackedPoolId, out var cachedPool))
        {
            cachedPool.UpdateLatestPrice(latestPrice, polledAtUtc);
        }
    }

    public void ApplyAlertUpdate(int subscriptionId, decimal newBasePrice, DateTime alertedAtUtc)
    {
        if (_subscriptionToPoolMap.TryGetValue(subscriptionId, out var trackedPoolId) &&
            _pools.TryGetValue(trackedPoolId, out var cachedPool))
        {
            cachedPool.ApplyAlertUpdate(subscriptionId, newBasePrice, alertedAtUtc);
        }
    }

    private sealed class CachedTrackedPool
    {
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<int, CachedSubscription> _subscriptions = new();

        private CachedTrackedPool()
        {
        }

        public int TrackedPoolId { get; private set; }

        public string PoolAddress { get; private set; } = string.Empty;

        public string Token0Symbol { get; private set; } = string.Empty;

        public string Token1Symbol { get; private set; } = string.Empty;

        public decimal? CurrentPrice { get; private set; }

        public decimal? CurrentInversePrice { get; private set; }

        public int? CurrentTick { get; private set; }

        public DateTime? LastPolledAtUtc { get; private set; }

        public bool HasSubscriptions => !_subscriptions.IsEmpty;

        public static CachedTrackedPool FromEntity(TrackedPool trackedPool)
        {
            return new CachedTrackedPool
            {
                TrackedPoolId = trackedPool.Id,
                PoolAddress = trackedPool.PoolAddress,
                Token0Symbol = trackedPool.Token0Symbol,
                Token1Symbol = trackedPool.Token1Symbol,
                CurrentPrice = trackedPool.LastKnownPrice,
                CurrentInversePrice = trackedPool.LastKnownInversePrice,
                CurrentTick = trackedPool.LastKnownTick,
                LastPolledAtUtc = trackedPool.LastPolledAtUtc
            };
        }

        public void UpdatePoolMetadata(TrackedPool trackedPool)
        {
            lock (_gate)
            {
                PoolAddress = trackedPool.PoolAddress;
                Token0Symbol = trackedPool.Token0Symbol;
                Token1Symbol = trackedPool.Token1Symbol;
                CurrentPrice = trackedPool.LastKnownPrice;
                CurrentInversePrice = trackedPool.LastKnownInversePrice;
                CurrentTick = trackedPool.LastKnownTick;
                LastPolledAtUtc = trackedPool.LastPolledAtUtc;
            }
        }

        public void UpdateLatestPrice(PoolPriceResult latestPrice, DateTime polledAtUtc)
        {
            lock (_gate)
            {
                PoolAddress = latestPrice.PoolAddress;
                Token0Symbol = latestPrice.Token0Symbol;
                Token1Symbol = latestPrice.Token1Symbol;
                CurrentPrice = latestPrice.Price;
                CurrentInversePrice = latestPrice.InversePrice;
                CurrentTick = latestPrice.Tick;
                LastPolledAtUtc = polledAtUtc;
            }
        }

        public void UpsertSubscription(PriceAlertSubscription subscription, TelegramUser telegramUser)
        {
            _subscriptions.AddOrUpdate(
                subscription.Id,
                _ => CachedSubscription.FromEntity(subscription, telegramUser),
                (_, existing) =>
                {
                    existing.UpdateFromEntity(subscription, telegramUser);
                    return existing;
                });
        }

        public void RemoveSubscription(int subscriptionId)
        {
            _subscriptions.TryRemove(subscriptionId, out _);
        }

        public void ApplyAlertUpdate(int subscriptionId, decimal newBasePrice, DateTime alertedAtUtc)
        {
            if (_subscriptions.TryGetValue(subscriptionId, out var subscription))
            {
                subscription.ApplyAlertUpdate(newBasePrice, alertedAtUtc);
            }
        }

        public void UpdateTelegramUser(TelegramUser telegramUser)
        {
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.UpdateTelegramUser(telegramUser);
            }
        }

        public TrackedPoolRuntimeSnapshot ToRuntimeSnapshot()
        {
            lock (_gate)
            {
                return new TrackedPoolRuntimeSnapshot
                {
                    TrackedPoolId = TrackedPoolId,
                    PoolAddress = PoolAddress,
                    Token0Symbol = Token0Symbol,
                    Token1Symbol = Token1Symbol,
                    CurrentPrice = CurrentPrice,
                    CurrentInversePrice = CurrentInversePrice,
                    CurrentTick = CurrentTick,
                    Subscriptions = _subscriptions.Values
                        .Select(x => x.ToRuntimeSnapshot())
                        .OrderBy(x => x.SubscriptionId)
                        .ToList()
                };
            }
        }

        public IReadOnlyCollection<TrackedPoolSubscriptionInfo> ToUserSubscriptionInfos(int telegramUserId)
        {
            lock (_gate)
            {
                return _subscriptions.Values
                    .Where(x => x.TelegramUserId == telegramUserId)
                    .Select(x => new TrackedPoolSubscriptionInfo
                    {
                        SubscriptionId = x.SubscriptionId,
                        PoolAddress = PoolAddress,
                        PairLabel = Token0Symbol + "/" + Token1Symbol,
                        ThresholdPercent = x.ThresholdPercent,
                        BasePrice = x.BasePrice,
                        CurrentPrice = CurrentPrice,
                        CurrentInversePrice = CurrentInversePrice
                    })
                    .ToList();
            }
        }
    }

    private sealed class CachedSubscription
    {
        private readonly object _gate = new();

        public int SubscriptionId { get; private set; }

        public int TelegramUserId { get; private set; }

        public long ChatId { get; private set; }

        public decimal ThresholdPercent { get; private set; }

        public decimal BasePrice { get; private set; }

        public DateTime? LastAlertedAtUtc { get; private set; }

        public static CachedSubscription FromEntity(PriceAlertSubscription subscription, TelegramUser telegramUser)
        {
            return new CachedSubscription
            {
                SubscriptionId = subscription.Id,
                TelegramUserId = subscription.TelegramUserId,
                ChatId = telegramUser.ChatId,
                ThresholdPercent = subscription.ThresholdPercent,
                BasePrice = subscription.BasePrice,
                LastAlertedAtUtc = subscription.LastAlertedAtUtc
            };
        }

        public void UpdateFromEntity(PriceAlertSubscription subscription, TelegramUser telegramUser)
        {
            lock (_gate)
            {
                TelegramUserId = subscription.TelegramUserId;
                ChatId = telegramUser.ChatId;
                ThresholdPercent = subscription.ThresholdPercent;
                BasePrice = subscription.BasePrice;
                LastAlertedAtUtc = subscription.LastAlertedAtUtc;
            }
        }

        public void UpdateTelegramUser(TelegramUser telegramUser)
        {
            if (telegramUser.Id != TelegramUserId)
            {
                return;
            }

            lock (_gate)
            {
                ChatId = telegramUser.ChatId;
            }
        }

        public void ApplyAlertUpdate(decimal newBasePrice, DateTime alertedAtUtc)
        {
            lock (_gate)
            {
                BasePrice = newBasePrice;
                LastAlertedAtUtc = alertedAtUtc;
            }
        }

        public AlertSubscriptionRuntimeSnapshot ToRuntimeSnapshot()
        {
            lock (_gate)
            {
                return new AlertSubscriptionRuntimeSnapshot
                {
                    SubscriptionId = SubscriptionId,
                    TelegramUserId = TelegramUserId,
                    ChatId = ChatId,
                    ThresholdPercent = ThresholdPercent,
                    BasePrice = BasePrice
                };
            }
        }
    }
}
