using Microsoft.EntityFrameworkCore;
using WebApplication1.Contracts;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Options;

namespace WebApplication1.Services;

public sealed class AlertMonitoringBackgroundService : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertMonitoringBackgroundService> _logger;
    private readonly TimeSpan _pollInterval;

    public AlertMonitoringBackgroundService(
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<RoninPoolOptions> options,
        ILogger<AlertMonitoringBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var priceService = scope.ServiceProvider.GetRequiredService<IRoninPoolPriceService>();
        var telegramClient = scope.ServiceProvider.GetRequiredService<ITelegramMessageClient>();

        var trackedPools = await dbContext.TrackedPools
            .Where(x => x.IsActive)
            .Where(x => x.Subscriptions.Any(s => s.IsActive))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var trackedPool in trackedPools)
        {
            PoolPriceResult latestPrice;

            try
            {
                latestPrice = await priceService.GetPoolPriceAsync(trackedPool.PoolAddress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to update pool {PoolAddress}", trackedPool.PoolAddress);
                continue;
            }

            trackedPool.Token0Address = latestPrice.Token0Address;
            trackedPool.Token1Address = latestPrice.Token1Address;
            trackedPool.Token0Symbol = latestPrice.Token0Symbol;
            trackedPool.Token1Symbol = latestPrice.Token1Symbol;
            trackedPool.LastKnownPrice = latestPrice.Price;
            trackedPool.LastKnownInversePrice = latestPrice.InversePrice;
            trackedPool.LastKnownTick = latestPrice.Tick;
            trackedPool.LastPolledAtUtc = DateTime.UtcNow;
            trackedPool.UpdatedAtUtc = DateTime.UtcNow;

            var subscriptions = await dbContext.PriceAlertSubscriptions
                .Include(x => x.TelegramUser)
                .Where(x => x.IsActive && x.TrackedPoolId == trackedPool.Id)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var subscription in subscriptions)
            {
                var evaluation = Evaluate(subscription.BasePrice, latestPrice.Price, subscription.ThresholdPercent);
                if (!evaluation.ShouldTrigger)
                {
                    continue;
                }

                var pairLabel = $"{latestPrice.Token0Symbol}/{latestPrice.Token1Symbol}";
                var message =
                    $"Alert {pairLabel}\n" +
                    $"Pool: {trackedPool.PoolAddress}\n" +
                    $"Price: {latestPrice.Price:F8}\n" +
                    $"Inverse: {latestPrice.InversePrice:F8}\n" +
                    $"Base: {subscription.BasePrice:F8}\n" +
                    $"Change: {evaluation.ChangePercent:+0.##;-0.##}%\n" +
                    $"Threshold: {subscription.ThresholdPercent:0.##}%";

                try
                {
                    await telegramClient.SendTextMessageAsync(subscription.TelegramUser.ChatId, message, cancellationToken: cancellationToken);
                    subscription.BasePrice = latestPrice.Price;
                    subscription.LastAlertedAtUtc = DateTime.UtcNow;
                    subscription.UpdatedAtUtc = DateTime.UtcNow;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send alert for subscription {SubscriptionId} and pool {PoolAddress}",
                        subscription.Id,
                        trackedPool.PoolAddress);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Ronin pool price updated: {Token0}/{Token1} = {Price}, inverse = {InversePrice}, tick = {Tick}",
                latestPrice.Token0Symbol,
                latestPrice.Token1Symbol,
                latestPrice.Price,
                latestPrice.InversePrice,
                latestPrice.Tick);
        }
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
