using Microsoft.EntityFrameworkCore;
using WebApplication1.Contracts;
using WebApplication1.Data;
using WebApplication1.Data.Entities;
using WebApplication1.Models;
using WebApplication1.Telegram.Models;

namespace WebApplication1.Services;

public sealed class TelegramBotWorkflowService : ITelegramUpdateHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRoninPoolPriceService _priceService;
    private readonly ITelegramMessageClient _telegramMessageClient;
    private readonly ILogger<TelegramBotWorkflowService> _logger;

    public TelegramBotWorkflowService(
        ApplicationDbContext dbContext,
        IRoninPoolPriceService priceService,
        ITelegramMessageClient telegramMessageClient,
        ILogger<TelegramBotWorkflowService> logger)
    {
        _dbContext = dbContext;
        _priceService = priceService;
        _telegramMessageClient = telegramMessageClient;
        _logger = logger;
    }

    public async Task HandleAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        var message = update.Message;
        if (message?.From is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var user = await GetOrCreateUserAsync(message, cancellationToken);
        var session = await GetOrCreateSessionAsync(user.Id, cancellationToken);
        var text = message.Text.Trim();

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Start", StringComparison.OrdinalIgnoreCase))
        {
            await ResetSessionAsync(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Crypto alert bot is ready.",
                true,
                cancellationToken);
            return;
        }

        if (text.Equals("Add Pair", StringComparison.OrdinalIgnoreCase))
        {
            session.State = TelegramChatState.AwaitingPoolAddress;
            session.PendingPoolAddress = null;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Send pool id/address from Ronin.",
                true,
                cancellationToken);
            return;
        }

        if (text.Equals("My Pairs", StringComparison.OrdinalIgnoreCase))
        {
            await SendSubscriptionsAsync(user, cancellationToken);
            return;
        }

        if (text.Equals("Delete Pair", StringComparison.OrdinalIgnoreCase))
        {
            var subscriptions = await GetUserSubscriptionsAsync(user.Id, cancellationToken);
            if (subscriptions.Count == 0)
            {
                await _telegramMessageClient.SendTextMessageAsync(user.ChatId, "No active pairs.", cancellationToken: cancellationToken);
                return;
            }

            session.State = TelegramChatState.AwaitingDeleteSelection;
            session.PendingPoolAddress = null;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var messageText = "Send subscription id to delete:\n" +
                              string.Join('\n', subscriptions.Select(x =>
                                  $"{x.SubscriptionId}. {x.PairLabel} | {x.PoolAddress} | threshold {x.ThresholdPercent:0.##}%"));

            await _telegramMessageClient.SendTextMessageAsync(user.ChatId, messageText, cancellationToken: cancellationToken);
            return;
        }

        switch (session.State)
        {
            case TelegramChatState.AwaitingPoolAddress:
                await HandlePoolInputAsync(user, session, text, cancellationToken);
                break;
            case TelegramChatState.AwaitingThreshold:
                await HandleThresholdInputAsync(user, session, text, cancellationToken);
                break;
            case TelegramChatState.AwaitingDeleteSelection:
                await HandleDeleteInputAsync(user, session, text, cancellationToken);
                break;
            default:
                await _telegramMessageClient.SendTextMessageAsync(
                    user.ChatId,
                    "Unknown command.",
                    true,
                    cancellationToken);
                break;
        }
    }

    private async Task HandlePoolInputAsync(
        TelegramUser user,
        TelegramChatSession session,
        string input,
        CancellationToken cancellationToken)
    {
        try
        {
            var poolPrice = await _priceService.GetPoolPriceAsync(input, cancellationToken);
            session.State = TelegramChatState.AwaitingThreshold;
            session.PendingPoolAddress = poolPrice.PoolAddress;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                $"{poolPrice.Token0Symbol}/{poolPrice.Token1Symbol} = {poolPrice.Price:F8}, inverse = {poolPrice.InversePrice:F8}\nSend threshold percent, for example: 5",
                true,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Invalid pool address received from telegram user {TelegramUserId}", user.TelegramUserId);
            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Cannot read this pool. Send a valid Ronin pool id/address.",
                true,
                cancellationToken);
        }
    }

    private async Task HandleThresholdInputAsync(
        TelegramUser user,
        TelegramChatSession session,
        string input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PendingPoolAddress))
        {
            await ResetSessionAsync(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Pair setup was reset. Start again with Add Pair.",
                true,
                cancellationToken);
            return;
        }

        if (!decimal.TryParse(input, out var thresholdPercent) || thresholdPercent <= 0)
        {
            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Threshold must be a positive number. Example: 5",
                true,
                cancellationToken);
            return;
        }

        var currentPrice = await _priceService.GetPoolPriceAsync(session.PendingPoolAddress, cancellationToken);
        var trackedPool = await UpsertTrackedPoolAsync(currentPrice, cancellationToken);

        var subscription = await _dbContext.PriceAlertSubscriptions
            .SingleOrDefaultAsync(
                x => x.TelegramUserId == user.Id && x.TrackedPoolId == trackedPool.Id,
                cancellationToken);

        if (subscription is not null && subscription.IsActive)
        {
            await ResetSessionAsync(session);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                $"You already added this pool.\n{currentPrice.Token0Symbol}/{currentPrice.Token1Symbol} = {currentPrice.Price:F8}, inverse = {currentPrice.InversePrice:F8}",
                true,
                cancellationToken);
            return;
        }

        if (subscription is null)
        {
            subscription = new PriceAlertSubscription
            {
                TelegramUserId = user.Id,
                TrackedPoolId = trackedPool.Id,
                ThresholdPercent = thresholdPercent,
                BasePrice = currentPrice.Price,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.PriceAlertSubscriptions.Add(subscription);
        }
        else
        {
            subscription.IsActive = true;
            subscription.ThresholdPercent = thresholdPercent;
            subscription.BasePrice = currentPrice.Price;
            subscription.UpdatedAtUtc = DateTime.UtcNow;
        }

        await ResetSessionAsync(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _telegramMessageClient.SendTextMessageAsync(
            user.ChatId,
            $"Subscription saved.\nPair: {currentPrice.Token0Symbol}/{currentPrice.Token1Symbol}\nPool: {currentPrice.PoolAddress}\nThreshold: {thresholdPercent:0.##}%\nBase price: {currentPrice.Price:F8}",
            true,
            cancellationToken);
    }

    private async Task HandleDeleteInputAsync(
        TelegramUser user,
        TelegramChatSession session,
        string input,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(input, out var subscriptionId))
        {
            await _telegramMessageClient.SendTextMessageAsync(
                user.ChatId,
                "Send numeric subscription id from the list.",
                true,
                cancellationToken);
            return;
        }

        var subscription = await _dbContext.PriceAlertSubscriptions
            .Include(x => x.TrackedPool)
            .SingleOrDefaultAsync(x => x.Id == subscriptionId && x.TelegramUserId == user.Id, cancellationToken);

        if (subscription is null)
        {
            await _telegramMessageClient.SendTextMessageAsync(user.ChatId, "Subscription not found.", cancellationToken: cancellationToken);
            return;
        }

        subscription.IsActive = false;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        var hasOtherActiveSubscriptions = await _dbContext.PriceAlertSubscriptions
            .AnyAsync(
                x => x.TrackedPoolId == subscription.TrackedPoolId && x.IsActive && x.Id != subscription.Id,
                cancellationToken);

        if (!hasOtherActiveSubscriptions)
        {
            subscription.TrackedPool.IsActive = false;
            subscription.TrackedPool.UpdatedAtUtc = DateTime.UtcNow;
        }

        await ResetSessionAsync(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _telegramMessageClient.SendTextMessageAsync(user.ChatId, "Subscription deleted.", true, cancellationToken);
    }

    private async Task SendSubscriptionsAsync(TelegramUser user, CancellationToken cancellationToken)
    {
        var subscriptions = await GetUserSubscriptionsAsync(user.Id, cancellationToken);
        if (subscriptions.Count == 0)
        {
            await _telegramMessageClient.SendTextMessageAsync(user.ChatId, "No active pairs.", true, cancellationToken);
            return;
        }

        var message = "My pairs:\n" + string.Join('\n', subscriptions.Select(x =>
            $"{x.SubscriptionId}. {x.PairLabel} | {x.PoolAddress} | threshold {x.ThresholdPercent:0.##}% | base {x.BasePrice:F8}\n" +
            $"price = {(x.CurrentPrice.HasValue ? x.CurrentPrice.Value.ToString("F8") : "n/a")} | inverse = {(x.CurrentInversePrice.HasValue ? x.CurrentInversePrice.Value.ToString("F8") : "n/a")}"));

        await _telegramMessageClient.SendTextMessageAsync(user.ChatId, message, true, cancellationToken);
    }

    private async Task<List<TrackedPoolSubscriptionInfo>> GetUserSubscriptionsAsync(int userId, CancellationToken cancellationToken)
    {
        return await _dbContext.PriceAlertSubscriptions
            .AsNoTracking()
            .Include(x => x.TrackedPool)
            .Where(x => x.TelegramUserId == userId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new TrackedPoolSubscriptionInfo
            {
                SubscriptionId = x.Id,
                PoolAddress = x.TrackedPool.PoolAddress,
                PairLabel = x.TrackedPool.Token0Symbol + "/" + x.TrackedPool.Token1Symbol,
                ThresholdPercent = x.ThresholdPercent,
                BasePrice = x.BasePrice,
                CurrentPrice = x.TrackedPool.LastKnownPrice,
                CurrentInversePrice = x.TrackedPool.LastKnownInversePrice
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<TelegramUser> GetOrCreateUserAsync(TelegramMessage message, CancellationToken cancellationToken)
    {
        var telegramUserId = message.From!.Id;
        var user = await _dbContext.TelegramUsers
            .SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);

        if (user is null)
        {
            user = new TelegramUser
            {
                TelegramUserId = telegramUserId,
                ChatId = message.Chat.Id,
                Username = message.From.Username,
                FirstName = message.From.FirstName,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.TelegramUsers.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }

        user.ChatId = message.Chat.Id;
        user.Username = message.From.Username;
        user.FirstName = message.From.FirstName;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<TelegramChatSession> GetOrCreateSessionAsync(int userId, CancellationToken cancellationToken)
    {
        var session = await _dbContext.TelegramChatSessions
            .SingleOrDefaultAsync(x => x.TelegramUserId == userId, cancellationToken);

        if (session is not null)
        {
            return session;
        }

        session = new TelegramChatSession
        {
            TelegramUserId = userId,
            State = TelegramChatState.Idle,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.TelegramChatSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task<TrackedPool> UpsertTrackedPoolAsync(PoolPriceResult poolPrice, CancellationToken cancellationToken)
    {
        var trackedPool = await _dbContext.TrackedPools
            .SingleOrDefaultAsync(x => x.PoolAddress == poolPrice.PoolAddress, cancellationToken);

        if (trackedPool is null)
        {
            trackedPool = new TrackedPool
            {
                PoolAddress = poolPrice.PoolAddress,
                Token0Address = poolPrice.Token0Address,
                Token1Address = poolPrice.Token1Address,
                Token0Symbol = poolPrice.Token0Symbol,
                Token1Symbol = poolPrice.Token1Symbol,
                LastKnownPrice = poolPrice.Price,
                LastKnownInversePrice = poolPrice.InversePrice,
                LastKnownTick = poolPrice.Tick,
                LastPolledAtUtc = DateTime.UtcNow,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.TrackedPools.Add(trackedPool);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return trackedPool;
        }

        trackedPool.Token0Address = poolPrice.Token0Address;
        trackedPool.Token1Address = poolPrice.Token1Address;
        trackedPool.Token0Symbol = poolPrice.Token0Symbol;
        trackedPool.Token1Symbol = poolPrice.Token1Symbol;
        trackedPool.LastKnownPrice = poolPrice.Price;
        trackedPool.LastKnownInversePrice = poolPrice.InversePrice;
        trackedPool.LastKnownTick = poolPrice.Tick;
        trackedPool.LastPolledAtUtc = DateTime.UtcNow;
        trackedPool.IsActive = true;
        trackedPool.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return trackedPool;
    }

    private static Task ResetSessionAsync(TelegramChatSession session)
    {
        session.State = TelegramChatState.Idle;
        session.PendingPoolAddress = null;
        session.UpdatedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
