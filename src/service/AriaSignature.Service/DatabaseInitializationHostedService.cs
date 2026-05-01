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
        try
        {
            // Не срываем старт сервиса из-за отмены startup-токена от SCM.
            await _initializer.InitializeAsync(CancellationToken.None);
            _logger.LogInformation("SQLite database schema initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite initialization failed");
            _logger.LogWarning("Service startup continues without blocking API; initialization will be retried on next run");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
