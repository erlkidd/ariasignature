using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

/// <summary>Синхронизирует Quartz outbound-триггер с cron из SQLite после старта.</summary>
public sealed class OutboundSyncCronSyncHostedService : BackgroundService
{
    private readonly IAppSettingsService _settings;
    private readonly IOutboundSyncCronApplier _cronApplier;
    private readonly ILogger<OutboundSyncCronSyncHostedService> _logger;

    public OutboundSyncCronSyncHostedService(
        IAppSettingsService settings,
        IOutboundSyncCronApplier cronApplier,
        ILogger<OutboundSyncCronSyncHostedService> logger)
    {
        _settings = settings;
        _cronApplier = cronApplier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
        try
        {
            var dict = await _settings.GetAllAsync(stoppingToken);
            var cron = dict.GetValueOrDefault(AppSettingsOutboundKeys.Cron);
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
            _logger.LogWarning(ex, "Не удалось применить cron исходящей синхронизации из базы при старте");
        }
    }
}
