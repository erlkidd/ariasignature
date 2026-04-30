using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace AriaSignature.Infrastructure.Backup;

public sealed class BackupExecutor : IBackupExecutor
{
    public async Task<BackupExecutionResult> ExecuteAsync(BackupJob job, CancellationToken cancellationToken)
    {
        return job.Type switch
        {
            BackupType.File => await ExecuteFileBackupAsync(job, cancellationToken),
            BackupType.MsSql => await ExecuteMsSqlBackupAsync(job, cancellationToken),
            _ => new BackupExecutionResult(false, $"Unsupported backup type: {job.Type}", null)
        };
    }

    private static async Task<BackupExecutionResult> ExecuteFileBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(job.Source))
            {
                return new BackupExecutionResult(false, $"Source file not found: {job.Source}", null);
            }

            var destinationDirectory = job.Destination;
            Directory.CreateDirectory(destinationDirectory);

            var extension = Path.GetExtension(job.Source);
            var baseName = Path.GetFileNameWithoutExtension(job.Source);
            var fileName = $"{baseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";
            var destinationPath = Path.Combine(destinationDirectory, fileName);

            await using var source = File.Open(job.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = File.Open(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);

            ApplyRetention(destinationDirectory, $"{baseName}_*", extension, Math.Max(job.RetentionCount, 1));
            var fileSize = new FileInfo(destinationPath).Length;

            return new BackupExecutionResult(true, $"File backup completed: {destinationPath}", fileSize);
        }
        catch (Exception ex)
        {
            return new BackupExecutionResult(false, $"File backup failed: {ex.Message}", null);
        }
    }

    private static async Task<BackupExecutionResult> ExecuteMsSqlBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(job.Destination);

            var builder = new SqlConnectionStringBuilder(job.Source);
            var databaseName = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return new BackupExecutionResult(false, "MSSQL backup requires Initial Catalog in Source connection string", null);
            }

            var backupPath = Path.Combine(job.Destination, $"{databaseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak");
            var sql = $"BACKUP DATABASE [{databaseName}] TO DISK = @path WITH INIT, FORMAT";

            await using var connection = new SqlConnection(job.Source);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@path", backupPath);
            await command.ExecuteNonQueryAsync(cancellationToken);

            ApplyRetention(job.Destination, $"{databaseName}_*", ".bak", Math.Max(job.RetentionCount, 1));
            long? fileSize = File.Exists(backupPath) ? new FileInfo(backupPath).Length : null;

            return new BackupExecutionResult(true, $"MSSQL backup completed: {backupPath}", fileSize);
        }
        catch (Exception ex)
        {
            return new BackupExecutionResult(false, $"MSSQL backup failed: {ex.Message}", null);
        }
    }

    private static void ApplyRetention(string directory, string patternPrefix, string extension, int retentionCount)
    {
        var files = Directory.EnumerateFiles(directory, $"{patternPrefix}{extension}", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToArray();

        foreach (var file in files.Skip(retentionCount))
        {
            file.Delete();
        }
    }
}
