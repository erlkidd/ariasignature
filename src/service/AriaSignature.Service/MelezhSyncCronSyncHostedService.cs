using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

public sealed class MelezhSyncCronSyncHostedService : BackgroundService
{
    private readonly IAppSettingsService _settings;
    private readonly IMelezhSyncCronApplier _cronApplier;
    private readonly ILogger<MelezhSyncCronSyncHostedService> _logger;

    public MelezhSyncCronSyncHostedService(
        IAppSettingsService settings,
        IMelezhSyncCronApplier cronApplier,
        ILogger<MelezhSyncCronSyncHostedService> logger)
    {
        _settings = settings;
        _cronApplier = cronApplier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        try
        {
            var dict = await _settings.GetAllAsync(stoppingToken);
            var cron = dict.GetValueOrDefault(AppSettingsMelezhSyncKeys.Cron);
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
            _logger.LogWarning(ex, "Не удалось применить cron Melezh sync из базы при старте");
        }
    }
}
