using AriaSignature.Application.Abstractions;

namespace AriaSignature.Service;

public sealed class BackupSchedulerHostedService : BackgroundService
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupSchedulerHostedService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public BackupSchedulerHostedService(IBackupService backupService, ILogger<BackupSchedulerHostedService> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dueRuns = await _backupService.RunDueJobsAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (dueRuns.Count > 0)
                {
                    _logger.LogInformation("Executed {Count} scheduled backup job(s)", dueRuns.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup scheduler poll failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
