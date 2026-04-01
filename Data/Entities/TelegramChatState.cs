namespace WebApplication1.Data.Entities;

public enum TelegramChatState
{
    Idle = 0,
    AwaitingPoolAddress = 1,
    AwaitingThreshold = 2,
    AwaitingDeleteSelection = 3
}
