using System.Text.Json.Serialization;

namespace WebApplication1.Telegram.Models;

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("chat")]
    public TelegramChat Chat { get; init; } = null!;

    [JsonPropertyName("from")]
    public TelegramUserDto? From { get; init; }
}
