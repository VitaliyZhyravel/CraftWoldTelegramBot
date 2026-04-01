namespace WebApplication1.Options;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; init; } = string.Empty;

    public string? PublicWebhookUrl { get; init; }

    public string? WebhookSecretToken { get; init; }
}
