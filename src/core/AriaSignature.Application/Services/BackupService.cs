using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.Application.Services;

public sealed class BackupService : IBackupService
{
    private readonly IBackupJobRepository _repository;
    private readonly IBackupExecutor _executor;

    public BackupService(IBackupJobRepository repository, IBackupExecutor executor)
    {
        _repository = repository;
        _executor = executor;
    }

    public Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken)
    {
        return _repository.GetJobsAsync(cancellationToken);
    }

    public Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken)
    {
        return _repository.CreateJobAsync(job, cancellationToken);
    }

    public Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken)
    {
        return _repository.UpdateJobAsync(id, job, cancellationToken);
    }

    public Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken)
    {
        return _repository.DeleteJobAsync(id, cancellationToken);
    }

    public async Task<BackupLog> RunJobAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await _repository.GetJobAsync(id, cancellationToken);
        if (job is null)
        {
            return new BackupLog
            {
                JobId = id,
                Status = BackupExecutionStatus.Failed,
                Message = "Backup job not found",
                StartTimeUtc = DateTimeOffset.UtcNow,
                EndTimeUtc = DateTimeOffset.UtcNow
            };
        }

        var start = DateTimeOffset.UtcNow;
        BackupExecutionResult result = new(false, "No execution performed", null);

        // Retry policy required by specification.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            result = await _executor.ExecuteAsync(job, cancellationToken);
            if (result.IsSuccess)
            {
                break;
            }
        }

        var log = new BackupLog
        {
            JobId = job.Id,
            Status = result.IsSuccess ? BackupExecutionStatus.Succeeded : BackupExecutionStatus.Failed,
            StartTimeUtc = start,
            EndTimeUtc = DateTimeOffset.UtcNow,
            FileSizeBytes = result.FileSizeBytes,
            Message = result.Message
        };

        await _repository.AddLogAsync(log, cancellationToken);
        return log;
    }

    public Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(CancellationToken cancellationToken)
    {
        return _repository.GetLogsAsync(cancellationToken);
    }
}
