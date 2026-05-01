using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using AriaSignature.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace AriaSignature.Infrastructure.Storage;

public sealed class SqliteBackupJobRepository : IBackupJobRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private const int MaxBusyRetries = 5;

    public SqliteBackupJobRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<BackupJob>> GetJobsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Type, Source, Destination, ScheduleCron, RetentionCount, IsEnabled FROM BackupJobs ORDER BY Name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var jobs = new List<BackupJob>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    public async Task<BackupJob?> GetJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Type, Source, Destination, ScheduleCron, RetentionCount, IsEnabled FROM BackupJobs WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<BackupJob> CreateJobAsync(BackupJob job, CancellationToken cancellationToken)
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

        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO BackupJobs (Id, Name, Type, Source, Destination, ScheduleCron, RetentionCount, IsEnabled)
                VALUES ($Id, $Name, $Type, $Source, $Destination, $ScheduleCron, $RetentionCount, $IsEnabled);
                """;
            BindJob(command, entity);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

        return entity;
    }

    public async Task<BackupJob?> UpdateJobAsync(Guid id, BackupJob job, CancellationToken cancellationToken)
    {
        var affected = 0;
        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE BackupJobs
                SET Name = $Name,
                    Type = $Type,
                    Source = $Source,
                    Destination = $Destination,
                    ScheduleCron = $ScheduleCron,
                    RetentionCount = $RetentionCount,
                    IsEnabled = $IsEnabled
                WHERE Id = $Id;
                """;
            command.Parameters.AddWithValue("$Id", id.ToString());
            command.Parameters.AddWithValue("$Name", job.Name);
            command.Parameters.AddWithValue("$Type", (int)job.Type);
            command.Parameters.AddWithValue("$Source", job.Source);
            command.Parameters.AddWithValue("$Destination", job.Destination);
            command.Parameters.AddWithValue("$ScheduleCron", job.ScheduleCron);
            command.Parameters.AddWithValue("$RetentionCount", job.RetentionCount);
            command.Parameters.AddWithValue("$IsEnabled", job.IsEnabled ? 1 : 0);
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
        if (affected == 0)
        {
            return null;
        }

        return await GetJobAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteJobAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = false;
        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM BackupJobs WHERE Id = $Id;";
            command.Parameters.AddWithValue("$Id", id.ToString());
            deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }, cancellationToken);
        return deleted;
    }

    public async Task AddLogAsync(BackupLog log, CancellationToken cancellationToken)
    {
        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO BackupLogs (Id, JobId, Status, StartTimeUtc, EndTimeUtc, FileSizeBytes, Message)
                VALUES ($Id, $JobId, $Status, $StartTimeUtc, $EndTimeUtc, $FileSizeBytes, $Message);
                """;
            command.Parameters.AddWithValue("$Id", log.Id.ToString());
            command.Parameters.AddWithValue("$JobId", log.JobId.ToString());
            command.Parameters.AddWithValue("$Status", (int)log.Status);
            command.Parameters.AddWithValue("$StartTimeUtc", log.StartTimeUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$EndTimeUtc", log.EndTimeUtc?.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$FileSizeBytes", (object?)log.FileSizeBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$Message", log.Message);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<BackupLog>> GetLogsAsync(BackupExecutionStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var sql = """
            SELECT Id, JobId, Status, StartTimeUtc, EndTimeUtc, FileSizeBytes, Message
            FROM BackupLogs
            WHERE 1=1
            """;
        if (status is BackupExecutionStatus st)
        {
            sql += " AND Status = $status";
            command.Parameters.AddWithValue("$status", (int)st);
        }

        if (fromUtc is DateTimeOffset from)
        {
            sql += " AND StartTimeUtc >= $from";
            command.Parameters.AddWithValue("$from", from.UtcDateTime.ToString("O"));
        }

        if (toUtc is DateTimeOffset to)
        {
            sql += " AND StartTimeUtc <= $to";
            command.Parameters.AddWithValue("$to", to.UtcDateTime.ToString("O"));
        }

        sql += " ORDER BY StartTimeUtc DESC LIMIT 1000;";
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadLogs(reader, cancellationToken);
    }

    public async Task<IReadOnlyCollection<BackupLog>> GetLogsByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, JobId, Status, StartTimeUtc, EndTimeUtc, FileSizeBytes, Message FROM BackupLogs WHERE JobId = $JobId ORDER BY StartTimeUtc DESC LIMIT 1000;";
        command.Parameters.AddWithValue("$JobId", jobId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadLogs(reader, cancellationToken);
    }

    private static void BindJob(Microsoft.Data.Sqlite.SqliteCommand command, BackupJob job)
    {
        command.Parameters.AddWithValue("$Id", job.Id.ToString());
        command.Parameters.AddWithValue("$Name", job.Name);
        command.Parameters.AddWithValue("$Type", (int)job.Type);
        command.Parameters.AddWithValue("$Source", job.Source);
        command.Parameters.AddWithValue("$Destination", job.Destination);
        command.Parameters.AddWithValue("$ScheduleCron", job.ScheduleCron);
        command.Parameters.AddWithValue("$RetentionCount", job.RetentionCount);
        command.Parameters.AddWithValue("$IsEnabled", job.IsEnabled ? 1 : 0);
    }

    private static BackupJob ReadJob(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new BackupJob
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            Type = (BackupType)reader.GetInt32(2),
            Source = reader.GetString(3),
            Destination = reader.GetString(4),
            ScheduleCron = reader.GetString(5),
            RetentionCount = reader.GetInt32(6),
            IsEnabled = reader.GetInt32(7) == 1
        };
    }

    private static async Task<IReadOnlyCollection<BackupLog>> ReadLogs(Microsoft.Data.Sqlite.SqliteDataReader reader, CancellationToken cancellationToken)
    {
        var logs = new List<BackupLog>();
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new BackupLog
            {
                Id = Guid.Parse(reader.GetString(0)),
                JobId = Guid.Parse(reader.GetString(1)),
                Status = (BackupExecutionStatus)reader.GetInt32(2),
                StartTimeUtc = DateTimeOffset.Parse(reader.GetString(3)),
                EndTimeUtc = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                FileSizeBytes = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                Message = reader.GetString(6)
            });
        }

        return logs;
    }

    private static async Task ExecuteWithBusyRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxBusyRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await operation();
                return;
            }
            catch (SqliteException ex) when (IsSqliteBusy(ex) && attempt < MaxBusyRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsSqliteBusy(SqliteException ex)
    {
        return ex.SqliteErrorCode is 5 or 6;
    }
}
