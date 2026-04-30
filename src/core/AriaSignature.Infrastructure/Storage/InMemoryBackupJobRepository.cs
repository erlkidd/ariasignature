using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;

namespace AriaSignature.Infrastructure.Storage;

public sealed class InMemoryBackupJobRepository : IBackupJobRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, BackupJob> _jobs = [];
    private readonly List<BackupLog> _logs = [];

    public Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyCollection<BackupJob>>(_jobs.Values.ToArray());
        }
    }

    public Task<BackupJob?> GetJobAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _jobs.TryGetValue(id, out var job);
            return Task.FromResult(job);
        }
    }

    public Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var entity = new BackupJob
        {
            Id = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id,
            Name = job.Name,
            Type = job.Type,
            Source = job.Source,
            Destination = job.Destination,
            ScheduleCron = job.ScheduleCron,
            RetentionCount = job.RetentionCount,
            IsEnabled = job.IsEnabled
        };

        lock (_sync)
        {
            _jobs[entity.Id] = entity;
        }

        return Task.FromResult(entity);
    }

    public Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_jobs.ContainsKey(id))
            {
                return Task.FromResult<BackupJob?>(null);
            }

            var updated = new BackupJob
            {
                Id = id,
                Name = job.Name,
                Type = job.Type,
                Source = job.Source,
                Destination = job.Destination,
                ScheduleCron = job.ScheduleCron,
                RetentionCount = job.RetentionCount,
                IsEnabled = job.IsEnabled
            };
            _jobs[id] = updated;
            return Task.FromResult<BackupJob?>(updated);
        }
    }

    public Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(_jobs.Remove(id));
        }
    }

    public Task AddLogAsync(BackupLog log, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _logs.Add(log);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyCollection<BackupLog>>(_logs.OrderByDescending(x => x.StartTimeUtc).ToArray());
        }
    }

    public Task<IReadOnlyCollection<BackupLog>> GetLogsByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyCollection<BackupLog>>(_logs
                .Where(x => x.JobId == jobId)
                .OrderByDescending(x => x.StartTimeUtc)
                .ToArray());
        }
    }
}
