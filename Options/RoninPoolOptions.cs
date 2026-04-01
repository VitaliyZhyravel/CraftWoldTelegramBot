namespace WebApplication1.Options;

public sealed class RoninPoolOptions
{
    public const string SectionName = "RoninPool";

    public string RpcEndpoint { get; init; } = string.Empty;

    public int PollIntervalSeconds { get; init; } = 1;
}
