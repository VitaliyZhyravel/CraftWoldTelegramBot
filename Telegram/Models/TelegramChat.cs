using System.Text.Json.Serialization;

namespace WebApplication1.Telegram.Models;

public sealed class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}
