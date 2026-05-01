using AriaSignature.Domain.Enums;

namespace AriaSignature.Domain.Entities;

public sealed class BackupLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public BackupExecutionStatus Status { get; set; } = BackupExecutionStatus.Pending;
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndTimeUtc { get; set; }
    public long? FileSizeBytes { get; set; }
    public string Message { get; set; } = string.Empty;
}
