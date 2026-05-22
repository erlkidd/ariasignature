using Quartz;

namespace AriaSignature.Application.Services;

/// <summary>
/// Оценка Quartz-cron для задач архивации в локальном времени Windows (UI строит cron в local TZ).
/// </summary>
internal static class BackupScheduleEvaluator
{
    internal sealed record ScheduleContext(
        DateTime LocalMinuteStart,
        DateTimeOffset MinuteWindowStartUtc);

    internal static ScheduleContext CreateContext(DateTimeOffset nowUtc)
    {
        var localTz = TimeZoneInfo.Local;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, localTz);
        var localMinuteStart = new DateTime(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            nowLocal.Hour,
            nowLocal.Minute,
            0,
            DateTimeKind.Unspecified);
        var minuteWindowStartUtc = new DateTimeOffset(localMinuteStart, localTz.GetUtcOffset(localMinuteStart)).ToUniversalTime();
        return new ScheduleContext(localMinuteStart, minuteWindowStartUtc);
    }

    internal static bool IsCronDue(string? scheduleCron, DateTime localMinuteStart)
    {
        if (string.IsNullOrWhiteSpace(scheduleCron) || !CronExpression.IsValidExpression(scheduleCron))
        {
            return false;
        }

        var cron = new CronExpression(scheduleCron)
        {
            TimeZone = TimeZoneInfo.Local
        };

        return cron.IsSatisfiedBy(localMinuteStart);
    }
}
