using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

/// <summary>Синхронизирует Quartz с cron из SQLite после старта (при старте триггер создаётся из appsettings; БД — источник истины для операторских правок).</summary>
public sealed class SmartMonitoringCronSyncHostedService : BackgroundService
{
    private readonly IAppSettingsService _settings;
    private readonly ISmartRefreshCronApplier _cronApplier;
    private readonly ILogger<SmartMonitoringCronSyncHostedService> _logger;

    public SmartMonitoringCronSyncHostedService(
        IAppSettingsService settings,
        ISmartRefreshCronApplier cronApplier,
        ILogger<SmartMonitoringCronSyncHostedService> logger)
    {
        _settings = settings;
        _cronApplier = cronApplier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        try
        {
            var dict = await _settings.GetAllAsync(stoppingToken);
            var cron = dict.GetValueOrDefault("SmartMonitoring:Cron");
            if (string.IsNullOrWhiteSpace(cron) || !CronExpression.IsValidExpression(cron))
            {
                return;
            }

            await _cronApplier.ApplyCronAsync(cron, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось применить cron обновления дисков из базы при старте");
        }
    }
}
