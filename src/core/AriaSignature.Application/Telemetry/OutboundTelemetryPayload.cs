using AriaSignature.Application.SystemInfo;
using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Telemetry;

public sealed class OutboundTelemetryPayload
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string AgentVersion { get; init; } = string.Empty;
    public SystemInfoSnapshot System { get; init; } = null!;
    public IReadOnlyList<Disk> Disks { get; init; } = Array.Empty<Disk>();
    public OutboundBackupSnapshot Backups { get; init; } = null!;
}

public sealed class OutboundBackupSnapshot
{
    public IReadOnlyList<BackupJob> Jobs { get; init; } = Array.Empty<BackupJob>();
    public IReadOnlyList<BackupLog> RecentLogs { get; init; } = Array.Empty<BackupLog>();
}
