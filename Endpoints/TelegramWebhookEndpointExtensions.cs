using Microsoft.Extensions.Options;
using WebApplication1.Contracts;
using WebApplication1.Options;
using WebApplication1.Telegram.Models;

namespace WebApplication1.Endpoints;

public static class TelegramWebhookEndpointExtensions
{
    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/telegram/webhook",
            async (
                HttpContext httpContext,
                TelegramUpdate update,
                ITelegramUpdateHandler updateHandler,
                IOptions<TelegramBotOptions> telegramOptions,
                CancellationToken cancellationToken) =>
            {
                var secretToken = telegramOptions.Value.WebhookSecretToken;
                if (!string.IsNullOrWhiteSpace(secretToken))
                {
                    var requestToken = httpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
                    if (!string.Equals(requestToken, secretToken, StringComparison.Ordinal))
                    {
                        return Results.Unauthorized();
                    }
                }

                await updateHandler.HandleAsync(update, cancellationToken);
                return Results.Ok();
            });

        return endpoints;
    }
}
