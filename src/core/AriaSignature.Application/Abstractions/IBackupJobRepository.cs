using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.Application.Abstractions;

public interface IBackupJobRepository
{
    Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken);
    Task<BackupJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);
    Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken);
    Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken);
    Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken);
    Task AddLogAsync(BackupLog log, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(BackupExecutionStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> GetLogsByJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<int> ClearAllLogsAsync(CancellationToken cancellationToken);
}
