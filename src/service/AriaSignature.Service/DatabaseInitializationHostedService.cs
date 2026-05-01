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
        using var initTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await _initializer.InitializeAsync(initTimeoutCts.Token);
            _logger.LogInformation("SQLite database schema initialization completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SQLite initialization timed out after 45 seconds; service startup is not blocked");
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
