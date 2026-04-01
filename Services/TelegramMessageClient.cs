using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WebApplication1.Contracts;
using WebApplication1.Options;

namespace WebApplication1.Services;

public sealed class TelegramMessageClient : ITelegramMessageClient
{
    private static readonly ReplyKeyboardMarkup MainMenuKeyboard = new()
    {
        Keyboard =
        [
            [new KeyboardButton { Text = "Add Pair" }],
            [new KeyboardButton { Text = "My Pairs" }],
            [new KeyboardButton { Text = "Delete Pair" }]
        ],
        ResizeKeyboard = true,
        IsPersistent = true
    };

    private readonly HttpClient _httpClient;
    private readonly TelegramBotOptions _options;

    public TelegramMessageClient(HttpClient httpClient, IOptions<TelegramBotOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendTextMessageAsync(
        long chatId,
        string text,
        bool showMainMenu = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            throw new InvalidOperationException("Telegram bot token is not configured.");
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"https://api.telegram.org/bot{_options.BotToken}/sendMessage",
            new SendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                ReplyMarkup = showMainMenu ? MainMenuKeyboard : null
            },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Telegram sendMessage failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {responseBody}",
            null,
            response.StatusCode);
    }

    private sealed class SendMessageRequest
    {
        [JsonPropertyName("chat_id")]
        public long ChatId { get; init; }

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("reply_markup")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReplyKeyboardMarkup? ReplyMarkup { get; init; }
    }

    private sealed class ReplyKeyboardMarkup
    {
        [JsonPropertyName("keyboard")]
        public required KeyboardButton[][] Keyboard { get; init; }

        [JsonPropertyName("resize_keyboard")]
        public bool ResizeKeyboard { get; init; }

        [JsonPropertyName("is_persistent")]
        public bool IsPersistent { get; init; }
    }

    private sealed class KeyboardButton
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;
    }
}
