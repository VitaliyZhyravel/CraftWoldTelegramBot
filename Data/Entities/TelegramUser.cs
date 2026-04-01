namespace WebApplication1.Data.Entities;

public sealed class TelegramUser
{
    public int Id { get; set; }

    public long TelegramUserId { get; set; }

    public long ChatId { get; set; }

    public string? Username { get; set; }

    public string? FirstName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PriceAlertSubscription> Subscriptions { get; set; } = [];

    public TelegramChatSession? ChatSession { get; set; }
}
