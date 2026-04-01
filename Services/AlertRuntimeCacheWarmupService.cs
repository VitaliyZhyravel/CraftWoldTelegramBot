using WebApplication1.Contracts;

namespace WebApplication1.Services;

public sealed class AlertRuntimeCacheWarmupService : IHostedService
{
    private readonly IAlertRuntimeCache _runtimeCache;

    public AlertRuntimeCacheWarmupService(IAlertRuntimeCache runtimeCache)
    {
        _runtimeCache = runtimeCache;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _runtimeCache.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
