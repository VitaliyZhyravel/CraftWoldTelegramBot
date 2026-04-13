using Microsoft.EntityFrameworkCore;
using WebApplication1.Contracts;
using WebApplication1.Data;
using WebApplication1.Data.Entities;
using WebApplication1.Models;
using WebApplication1.Options;

namespace WebApplication1.Services;

public sealed class AlertMonitoringBackgroundService : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertRuntimeCache _runtimeCache;
    private readonly IRoninPoolPriceService _priceService;
    private readonly ITelegramMessageClient _telegramClient;
    private readonly ILogger<AlertMonitoringBackgroundService> _logger;
    private readonly TimeSpan _pollInterval;

    public AlertMonitoringBackgroundService(
        IServiceScopeFactory scopeFactory,
        IAlertRuntimeCache runtimeCache,
        IRoninPoolPriceService priceService,
        ITelegramMessageClient telegramClient,
        Microsoft.Extensions.Options.IOptions<RoninPoolOptions> options,
        ILogger<AlertMonitoringBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _runtimeCache = runtimeCache;
        _priceService = priceService;
        _telegramClient = telegramClient;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _runtimeCache.InitializeAsync(stoppingToken);
        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!await _semaphore.WaitAsync(0, stoppingToken))
            {
                _logger.LogWarning("Skipping monitoring tick because previous tick is still running.");
                continue;
            }

            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during alert monitoring tick.");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        var trackedPools = _runtimeCache.GetActivePools();

        foreach (var trackedPool in trackedPools)
        {
            PoolPriceResult latestPrice;

            try
            {
                latestPrice = await _priceService.GetPoolPriceAsync(trackedPool.PoolAddress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to update pool {PoolAddress}", trackedPool.PoolAddress);
                continue;
            }

            var priceChanged = HasPriceChanged(trackedPool, latestPrice);
            if (!priceChanged)
            {
                continue;
            }

            var utcNow = DateTime.UtcNow;
            _runtimeCache.UpdatePoolState(trackedPool.TrackedPoolId, latestPrice, utcNow);

            var alertUpdates = new List<AlertPersistenceUpdate>();

            foreach (var subscription in trackedPool.Subscriptions.OrderBy(x => x.SubscriptionId))
            {
                var evaluation = Evaluate(subscription.BasePrice, latestPrice.Price, subscription.ThresholdPercent);
                if (!evaluation.ShouldTrigger)
                {
                    continue;
                }

                var pairLabel = $"{latestPrice.Token0Symbol}/{latestPrice.Token1Symbol}";
                var message =
                    $"Alert {pairLabel}\n" +
                    $"Price: {latestPrice.Price:F8}\n" +
                    $"Inverse: {latestPrice.InversePrice:F8}\n" +
                    $"Change: {evaluation.ChangePercent:+0.##;-0.##}%\n" +
                    $"Threshold: {subscription.ThresholdPercent:0.##}%";

                try
                {
                    await _telegramClient.SendTextMessageAsync(subscription.ChatId, message, cancellationToken: cancellationToken);
                    alertUpdates.Add(new AlertPersistenceUpdate
                    {
                        SubscriptionId = subscription.SubscriptionId,
                        BasePrice = latestPrice.Price,
                        AlertedAtUtc = utcNow
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send alert for subscription {SubscriptionId} and pool {PoolAddress}",
                        subscription.SubscriptionId,
                        trackedPool.PoolAddress);
                }
            }

            if (alertUpdates.Count > 0)
            {
                await PersistAlertUpdatesAsync(alertUpdates, cancellationToken);
            }

            _logger.LogInformation(
                "Ronin pool price updated: {Token0}/{Token1} = {Price}, inverse = {InversePrice}, tick = {Tick}",
                latestPrice.Token0Symbol,
                latestPrice.Token1Symbol,
                latestPrice.Price,
                latestPrice.InversePrice,
                latestPrice.Tick);
        }
    }

    private async Task PersistAlertUpdatesAsync(
        IReadOnlyCollection<AlertPersistenceUpdate> alertUpdates,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var alertUpdate in alertUpdates)
        {
            var subscription = new PriceAlertSubscription
            {
                Id = alertUpdate.SubscriptionId
            };

            dbContext.PriceAlertSubscriptions.Attach(subscription);
            subscription.BasePrice = alertUpdate.BasePrice;
            subscription.LastAlertedAtUtc = alertUpdate.AlertedAtUtc;
            subscription.UpdatedAtUtc = alertUpdate.AlertedAtUtc;

            dbContext.Entry(subscription).Property(x => x.BasePrice).IsModified = true;
            dbContext.Entry(subscription).Property(x => x.LastAlertedAtUtc).IsModified = true;
            dbContext.Entry(subscription).Property(x => x.UpdatedAtUtc).IsModified = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var alertUpdate in alertUpdates)
        {
            _runtimeCache.ApplyAlertUpdate(alertUpdate.SubscriptionId, alertUpdate.BasePrice, alertUpdate.AlertedAtUtc);
        }
    }

    private static bool HasPriceChanged(TrackedPoolRuntimeSnapshot trackedPool, PoolPriceResult latestPrice)
    {
        return trackedPool.CurrentPrice != latestPrice.Price ||
               trackedPool.CurrentTick != latestPrice.Tick;
    }

    private static AlertEvaluationResult Evaluate(decimal basePrice, decimal currentPrice, decimal thresholdPercent)
    {
        if (basePrice <= 0 || thresholdPercent <= 0)
        {
            return new AlertEvaluationResult
            {
                ShouldTrigger = false,
                ChangePercent = 0
            };
        }

        var changePercent = ((currentPrice - basePrice) / basePrice) * 100m;

        return new AlertEvaluationResult
        {
            ShouldTrigger = Math.Abs(changePercent) >= thresholdPercent,
            ChangePercent = changePercent
        };
    }

    public override void Dispose()
    {
        _semaphore.Dispose();
        base.Dispose();
    }
}
