using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

public sealed class QuartzOutboundSyncCronApplier : IOutboundSyncCronApplier
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzOutboundSyncCronApplier> _logger;

    public QuartzOutboundSyncCronApplier(ISchedulerFactory schedulerFactory, ILogger<QuartzOutboundSyncCronApplier> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var triggerKey = new TriggerKey("outbound-sync-trigger");
        var jobKey = new JobKey("outbound-sync-job");

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(CronScheduleBuilder.CronSchedule(cronExpression))
            .Build();

        await scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
        _logger.LogInformation("Триггер исходящей синхронизации перепланирован (cron: {Cron})", cronExpression);
    }
}
