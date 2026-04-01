using WebApplication1.Data;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Services;

public sealed class ApplicationDbInitializer
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ApplicationDbInitializer> _logger;

    public ApplicationDbInitializer(ApplicationDbContext dbContext, ILogger<ApplicationDbInitializer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying SQLite migrations.");
        await _dbContext.Database.MigrateAsync(cancellationToken);
    }
}
