using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Quartz;

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

    public async Task<IReadOnlyCollection<BackupLog>> RunDueJobsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var jobs = await _repository.GetJobsAsync(cancellationToken);
        var dueLogs = new List<BackupLog>();

        foreach (var job in jobs.Where(j => j.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(job.ScheduleCron) || !CronExpression.IsValidExpression(job.ScheduleCron))
            {
                continue;
            }

            var cron = new CronExpression(job.ScheduleCron)
            {
                TimeZone = TimeZoneInfo.Utc
            };

            if (!cron.IsSatisfiedBy(nowUtc.UtcDateTime))
            {
                continue;
            }

            var jobLogs = await _repository.GetLogsByJobAsync(job.Id, cancellationToken);
            var minuteWindowStart = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, nowUtc.Minute, 0, TimeSpan.Zero);
            if (jobLogs.Any(log => log.StartTimeUtc >= minuteWindowStart))
            {
                continue;
            }

            var logResult = await RunJobAsync(job.Id, cancellationToken);
            dueLogs.Add(logResult);
        }

        return dueLogs;
    }

    public Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(BackupExecutionStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        return _repository.GetLogsAsync(status, fromUtc, toUtc, cancellationToken);
    }
}
