using WebApplication1.Models;

namespace WebApplication1.Contracts;

public interface IRoninPoolPriceService
{
    Task<PoolPriceResult> GetPoolPriceAsync(string poolAddress, CancellationToken cancellationToken = default);
}
