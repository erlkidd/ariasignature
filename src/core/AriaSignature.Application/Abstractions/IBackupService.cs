using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IBackupService
{
    Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken);
    Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken);
    Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken);
    Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken);
    Task<BackupLog> RunJobAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> RunDueJobsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(CancellationToken cancellationToken);
}
