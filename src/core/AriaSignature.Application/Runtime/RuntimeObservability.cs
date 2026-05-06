using System.Diagnostics;
using System.Threading;

namespace AriaSignature.Application.Runtime;

public static class RuntimeObservability
{
    private static readonly long StartedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long _apiStartupLatencyMs = -1;
    private static long _apiFirstReadyLatencyMs = -1;
    private static long _backupSucceeded;
    private static long _backupFailed;
    private static long _smartRefreshSucceeded;
    private static long _smartRefreshFailed;
    private static long _outboundSyncSucceeded;
    private static long _outboundSyncFailed;
    private static long _dbBusyRetries;

    public static void RecordApiStartupLatency(TimeSpan latency)
    {
        Interlocked.Exchange(ref _apiStartupLatencyMs, Math.Max(0, (long)latency.TotalMilliseconds));
    }

    public static void RecordApiFirstReadyLatency(TimeSpan latency)
    {
        Interlocked.Exchange(ref _apiFirstReadyLatencyMs, Math.Max(0, (long)latency.TotalMilliseconds));
    }

    public static void RecordBackupExecution(bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _backupSucceeded);
        }
        else
        {
            Interlocked.Increment(ref _backupFailed);
        }
    }

    public static void RecordSmartRefresh(bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _smartRefreshSucceeded);
        }
        else
        {
            Interlocked.Increment(ref _smartRefreshFailed);
        }
    }

    public static void RecordOutboundSync(bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _outboundSyncSucceeded);
        }
        else
        {
            Interlocked.Increment(ref _outboundSyncFailed);
        }
    }

    public static void RecordDbBusyRetry()
    {
        Interlocked.Increment(ref _dbBusyRetries);
    }

    public static RuntimeObservabilitySnapshot GetSnapshot()
    {
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(StartedAtUnixMs);
        return new RuntimeObservabilitySnapshot(
            StartedAtUtc: startedAt,
            Uptime: DateTimeOffset.UtcNow - startedAt,
            ApiStartupLatencyMs: Interlocked.Read(ref _apiStartupLatencyMs),
            ApiFirstReadyLatencyMs: Interlocked.Read(ref _apiFirstReadyLatencyMs),
            BackupSucceeded: Interlocked.Read(ref _backupSucceeded),
            BackupFailed: Interlocked.Read(ref _backupFailed),
            SmartRefreshSucceeded: Interlocked.Read(ref _smartRefreshSucceeded),
            SmartRefreshFailed: Interlocked.Read(ref _smartRefreshFailed),
            OutboundSyncSucceeded: Interlocked.Read(ref _outboundSyncSucceeded),
            OutboundSyncFailed: Interlocked.Read(ref _outboundSyncFailed),
            DbBusyRetries: Interlocked.Read(ref _dbBusyRetries));
    }
}

public sealed record RuntimeObservabilitySnapshot(
    DateTimeOffset StartedAtUtc,
    TimeSpan Uptime,
    long ApiStartupLatencyMs,
    long ApiFirstReadyLatencyMs,
    long BackupSucceeded,
    long BackupFailed,
    long SmartRefreshSucceeded,
    long SmartRefreshFailed,
    long OutboundSyncSucceeded,
    long OutboundSyncFailed,
    long DbBusyRetries)
{
    public long BackupTotal => BackupSucceeded + BackupFailed;
    public double BackupSuccessRatio => BackupTotal == 0 ? 1d : (double)BackupSucceeded / BackupTotal;
    public long SmartRefreshTotal => SmartRefreshSucceeded + SmartRefreshFailed;
    public double SmartRefreshSuccessRatio => SmartRefreshTotal == 0 ? 1d : (double)SmartRefreshSucceeded / SmartRefreshTotal;
    public long OutboundSyncTotal => OutboundSyncSucceeded + OutboundSyncFailed;
    public double OutboundSyncSuccessRatio => OutboundSyncTotal == 0 ? 1d : (double)OutboundSyncSucceeded / OutboundSyncTotal;
}
