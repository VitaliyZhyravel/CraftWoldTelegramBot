using WebApplication1.Telegram.Models;

namespace WebApplication1.Contracts;

public interface ITelegramUpdateHandler
{
    Task HandleAsync(TelegramUpdate update, CancellationToken cancellationToken = default);
}
