using AriaSignature.Infrastructure.Persistence;
using System.Diagnostics;

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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("Initializing SQLite database schema in background phase");
            using var initTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await _initializer.InitializeAsync(initTimeoutCts.Token);
                _logger.LogInformation("SQLite database schema initialization completed in {ElapsedMs} ms", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SQLite initialization timed out after 45 seconds ({ElapsedMs} ms); service startup is not blocked", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQLite initialization failed after {ElapsedMs} ms", sw.ElapsedMilliseconds);
                _logger.LogWarning("Service startup continues without blocking API; initialization will be retried on next run");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
