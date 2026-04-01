namespace WebApplication1.Contracts;

public interface ITelegramMessageClient
{
    Task SendTextMessageAsync(
        long chatId,
        string text,
        bool showMainMenu = false,
        CancellationToken cancellationToken = default);
}
