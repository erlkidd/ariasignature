using AriaSignature.Infrastructure.Persistence;

namespace AriaSignature.Service;

public sealed class DatabaseInitializationHostedService : IHostedService
{
    private readonly ISqliteDatabaseInitializer _initializer;
    private readonly ILogger<DatabaseInitializationHostedService> _logger;

    public DatabaseInitializationHostedService(
        ISqliteDatabaseInitializer initializer,
        ILogger<DatabaseInitializationHostedService> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing SQLite database schema");
        await _initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
