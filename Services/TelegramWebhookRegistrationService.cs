using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WebApplication1.Options;

namespace WebApplication1.Services;

public sealed class TelegramWebhookRegistrationService : IHostedService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramWebhookRegistrationService> _logger;

    public TelegramWebhookRegistrationService(
        HttpClient httpClient,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramWebhookRegistrationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.PublicWebhookUrl))
        {
            _logger.LogInformation("Telegram webhook registration skipped because configuration is incomplete.");
            return;
        }

        var webhookUrl = _options.PublicWebhookUrl.TrimEnd('/') + "/telegram/webhook";

        using var response = await _httpClient.PostAsJsonAsync(
            $"https://api.telegram.org/bot{_options.BotToken}/setWebhook",
            new SetWebhookRequest
            {
                Url = webhookUrl,
                SecretToken = _options.WebhookSecretToken
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Telegram webhook registered: {WebhookUrl}", webhookUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class SetWebhookRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("secret_token")]
        public string? SecretToken { get; init; }
    }
}
