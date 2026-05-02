using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service;

public sealed class QuartzSmartRefreshCronApplier : ISmartRefreshCronApplier
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzSmartRefreshCronApplier> _logger;

    public QuartzSmartRefreshCronApplier(ISchedulerFactory schedulerFactory, ILogger<QuartzSmartRefreshCronApplier> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var triggerKey = new TriggerKey("smart-refresh-trigger");
        var jobKey = new JobKey("smart-refresh-job");

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(CronScheduleBuilder.CronSchedule(cronExpression))
            .Build();

        await scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
        _logger.LogInformation("Триггер обновления дисков перепланирован (cron: {Cron})", cronExpression);
    }
}
