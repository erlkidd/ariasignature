namespace AriaSignature.MelezhHost;

/// <summary>Staggered Melezh scheduler crons for pull handlers (7-field: sec min hour dom month dow year).</summary>
public static class MelezhPullCronSchedule
{
    public const int StaggerStepSeconds = 4;

    /// <summary>Build cron with second offset for index (every 5 minutes).</summary>
    public static string BuildStaggered(int index, int stepSeconds = StaggerStepSeconds)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (stepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        }

        var second = (index * stepSeconds) % 60;
        return $"{second} */5 * * * * *";
    }

    public static IReadOnlyDictionary<string, string> BuildForScheduledHandlers()
    {
        var scheduled = MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault).ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < scheduled.Count; i++)
        {
            map[scheduled[i].Key] = BuildStaggered(i);
        }

        return map;
    }
}
