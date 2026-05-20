using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

public sealed class QuartzMelezhSyncCronApplier : IMelezhSyncCronApplier
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzMelezhSyncCronApplier> _logger;

    public QuartzMelezhSyncCronApplier(ISchedulerFactory schedulerFactory, ILogger<QuartzMelezhSyncCronApplier> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var triggerKey = new TriggerKey("melezh-sync-trigger");
        var jobKey = new JobKey("melezh-sync-job");

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(CronScheduleBuilder.CronSchedule(cronExpression))
            .Build();

        await scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
        _logger.LogInformation("Триггер Melezh sync перепланирован (cron: {Cron})", cronExpression);
    }
}
