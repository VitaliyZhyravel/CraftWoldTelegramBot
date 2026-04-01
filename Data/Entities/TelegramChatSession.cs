namespace WebApplication1.Data.Entities;

public sealed class TelegramChatSession
{
    public int Id { get; set; }

    public int TelegramUserId { get; set; }

    public TelegramChatState State { get; set; } = TelegramChatState.Idle;

    public string? PendingPoolAddress { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public TelegramUser TelegramUser { get; set; } = null!;
}
