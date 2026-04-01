using System.Text.Json.Serialization;

namespace WebApplication1.Telegram.Models;

public sealed class TelegramUserDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }
}
