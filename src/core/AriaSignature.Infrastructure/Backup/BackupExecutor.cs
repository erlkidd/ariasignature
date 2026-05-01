using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace AriaSignature.Infrastructure.Backup;

public sealed class BackupExecutor : IBackupExecutor
{
    private static readonly string? RarExecutable = ResolveRarExecutable();

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
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var tmpFileName = $"{baseName}_{stamp}{extension}";
            var tmpPath = Path.Combine(destinationDirectory, tmpFileName);

            await using var source = File.Open(job.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = File.Open(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);

            var archivePath = Path.Combine(destinationDirectory, $"{baseName}_{stamp}.rar");
            var archiveResult = PackToRar(tmpPath, archivePath, cancellationToken);
            File.Delete(tmpPath);
            if (!archiveResult.success)
            {
                return new BackupExecutionResult(false, archiveResult.message, null);
            }

            ApplyRetention(destinationDirectory, $"{baseName}_*", ".rar", Math.Max(job.RetentionCount, 1));
            var fileSize = new FileInfo(archivePath).Length;

            return new BackupExecutionResult(true, $"File backup completed: {archivePath}", fileSize);
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

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var tmpBakPath = Path.Combine(job.Destination, $"{databaseName}_{stamp}.bak");
            var sql = $"BACKUP DATABASE [{databaseName}] TO DISK = @path WITH INIT, FORMAT, CHECKSUM";

            await using var connection = new SqlConnection(job.Source);
            await connection.OpenAsync(cancellationToken);
            await using var existsCommand = new SqlCommand("SELECT DB_ID(@dbName)", connection);
            existsCommand.Parameters.AddWithValue("@dbName", databaseName);
            var exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
            if (exists is null || exists == DBNull.Value)
            {
                return new BackupExecutionResult(false, $"MSSQL database not found: {databaseName}", null);
            }

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@path", tmpBakPath);
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (!File.Exists(tmpBakPath) || new FileInfo(tmpBakPath).Length == 0)
            {
                return new BackupExecutionResult(false, "MSSQL backup produced empty .bak file", null);
            }

            var archivePath = Path.Combine(job.Destination, $"{databaseName}_{stamp}.rar");
            var archiveResult = PackToRar(tmpBakPath, archivePath, cancellationToken);
            File.Delete(tmpBakPath);
            if (!archiveResult.success)
            {
                return new BackupExecutionResult(false, archiveResult.message, null);
            }

            ApplyRetention(job.Destination, $"{databaseName}_*", ".rar", Math.Max(job.RetentionCount, 1));
            long? fileSize = File.Exists(archivePath) ? new FileInfo(archivePath).Length : null;

            return new BackupExecutionResult(true, $"MSSQL backup completed: {archivePath}", fileSize);
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

    private static (bool success, string message) PackToRar(string sourceFilePath, string destinationRarPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RarExecutable))
        {
            return (false, "Rar archiver not found. Install WinRAR and ensure Rar.exe is available in PATH.");
        }

        if (File.Exists(destinationRarPath))
        {
            File.Delete(destinationRarPath);
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = RarExecutable,
                Arguments = $"a -ep1 -inul \"{destinationRarPath}\" \"{sourceFilePath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();
        if (cancellationToken.IsCancellationRequested)
        {
            return (false, "Backup archive packing cancelled");
        }

        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            return (false, $"Rar packing failed with code {process.ExitCode}. {output} {error}".Trim());
        }

        if (!File.Exists(destinationRarPath) || new FileInfo(destinationRarPath).Length == 0)
        {
            return (false, "Rar archive was not created or is empty");
        }

        return (true, "ok");
    }

    private static string? ResolveRarExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles") is { Length: > 0 } pf ? Path.Combine(pf, "WinRAR", "Rar.exe") : string.Empty,
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { Length: > 0 } pfx86 ? Path.Combine(pfx86, "WinRAR", "Rar.exe") : string.Empty
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var file = Path.Combine(part.Trim(), "Rar.exe");
            if (File.Exists(file))
            {
                return file;
            }
        }

        return null;
    }
}
