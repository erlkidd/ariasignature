using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IBackupExecutor
{
    Task<BackupExecutionResult> ExecuteAsync(BackupJob job, CancellationToken cancellationToken);
}

public sealed record BackupExecutionResult(bool IsSuccess, string Message, long? FileSizeBytes);
