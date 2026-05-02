using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.Application.Abstractions;

public interface IBackupService
{
    Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken);
    Task<BackupJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);
    Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken);
    Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken);
    Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken);
    Task<BackupLog> RunJobAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> RunDueJobsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(BackupExecutionStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken);
    Task<int> ClearAllLogsAsync(CancellationToken cancellationToken);
}
