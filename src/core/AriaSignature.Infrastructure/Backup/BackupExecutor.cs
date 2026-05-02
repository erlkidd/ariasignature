using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace AriaSignature.Infrastructure.Backup;

public sealed class BackupExecutor : IBackupExecutor
{
    private static readonly string? RarExecutable = ResolveRarExecutable();
    private const string DefaultJobFileName = "rezervnaya-kopiya";

    public async Task<BackupExecutionResult> ExecuteAsync(BackupJob job, CancellationToken cancellationToken)
    {
        return job.Type switch
        {
            BackupType.File => await ExecuteFileBackupAsync(job, cancellationToken),
            BackupType.MsSql => await ExecuteMsSqlBackupAsync(job, cancellationToken),
            _ => new BackupExecutionResult(false, $"Неподдерживаемый тип архивации: {job.Type}", null)
        };
    }

    private static async Task<BackupExecutionResult> ExecuteFileBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(job.Source))
            {
                return new BackupExecutionResult(false, $"Исходный файл не найден: {job.Source}", null);
            }

            var destinationDirectory = job.Destination;
            Directory.CreateDirectory(destinationDirectory);

            var extension = Path.GetExtension(job.Source);
            var baseName = Path.GetFileNameWithoutExtension(job.Source);
            var archivePrefix = SanitizeFileName(job.Name);
            var runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var tmpFileName = $"{baseName}_{runStamp}{extension}";
            var tmpPath = Path.Combine(destinationDirectory, tmpFileName);

            await using var source = File.Open(job.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = File.Open(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);

            var archivePath = BuildArchivePath(destinationDirectory, archivePrefix, runStamp);
            var archiveResult = PackToRar(tmpPath, archivePath, cancellationToken);
            File.Delete(tmpPath);
            if (!archiveResult.success)
            {
                return new BackupExecutionResult(false, archiveResult.message, null);
            }

            ApplyRetention(destinationDirectory, $"{archivePrefix}_*", ".rar", Math.Max(job.RetentionCount, 1));
            var fileSize = new FileInfo(archivePath).Length;

            return new BackupExecutionResult(true, $"Файловая архивация выполнена: {archivePath}", fileSize);
        }
        catch (Exception ex)
        {
            return new BackupExecutionResult(false, $"Ошибка файловой архивации: {DescribeException(ex)}", null);
        }
    }

    private static async Task<BackupExecutionResult> ExecuteMsSqlBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(job.Destination);

            var builder = new SqlConnectionStringBuilder(job.Source);
            var databaseName = builder.InitialCatalog;
            var archivePrefix = SanitizeFileName(job.Name);
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return new BackupExecutionResult(false, "Для MSSQL-архивации в строке подключения требуется параметр Initial Catalog", null);
            }

            var runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var tmpBakPath = Path.Combine(job.Destination, $"{archivePrefix}_{runStamp}.bak");
            var sql = $"BACKUP DATABASE [{databaseName}] TO DISK = @path WITH INIT, FORMAT, CHECKSUM";

            await using var connection = new SqlConnection(job.Source);
            await connection.OpenAsync(cancellationToken);
            await using var existsCommand = new SqlCommand("SELECT DB_ID(@dbName)", connection);
            existsCommand.Parameters.AddWithValue("@dbName", databaseName);
            var exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
            if (exists is null || exists == DBNull.Value)
            {
                return new BackupExecutionResult(false, $"База MSSQL не найдена: {databaseName}", null);
            }

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@path", tmpBakPath);
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (!File.Exists(tmpBakPath) || new FileInfo(tmpBakPath).Length == 0)
            {
                return new BackupExecutionResult(false, "MSSQL-архивация создала пустой файл .bak", null);
            }

            var archivePath = BuildArchivePath(job.Destination, archivePrefix, runStamp);
            var archiveResult = PackToRar(tmpBakPath, archivePath, cancellationToken);
            File.Delete(tmpBakPath);
            if (!archiveResult.success)
            {
                return new BackupExecutionResult(false, archiveResult.message, null);
            }

            ApplyRetention(job.Destination, $"{archivePrefix}_*", ".rar", Math.Max(job.RetentionCount, 1));
            long? fileSize = File.Exists(archivePath) ? new FileInfo(archivePath).Length : null;

            return new BackupExecutionResult(true, $"MSSQL-архивация выполнена: {archivePath}", fileSize);
        }
        catch (Exception ex)
        {
            return new BackupExecutionResult(false, $"Ошибка MSSQL-архивации: {DescribeException(ex)}", null);
        }
    }

    private static string DescribeException(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is IOException io && IsFileSharingViolation(io))
            {
                return "файл занят другим процессом; повторите позже";
            }
        }

        return ex.Message;
    }

    private static bool IsFileSharingViolation(IOException io)
    {
        // ERROR_SHARING_VIOLATION 32, HRESULT 0x80070020
        if ((io.HResult & 0xFFFF) == 0x20 || io.HResult == unchecked((int)0x80070020))
        {
            return true;
        }

        var m = io.Message ?? "";
        return m.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
               || m.Contains("используется другим процессом", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildArchivePath(string directory, string archivePrefix, string stamp)
    {
        return Path.Combine(directory, $"{archivePrefix}_{stamp}.rar");
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultJobFileName;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim('_', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? DefaultJobFileName : sanitized;
    }

    private static (bool success, string message) PackToRar(string sourceFilePath, string destinationRarPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RarExecutable))
        {
            return (false, "Архиватор RAR не найден. Установите WinRAR и убедитесь, что Rar.exe доступен в PATH.");
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
            return (false, "Упаковка архива отменена");
        }

        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            return (false, $"Ошибка упаковки RAR (код {process.ExitCode}). {output} {error}".Trim());
        }

        if (!File.Exists(destinationRarPath) || new FileInfo(destinationRarPath).Length == 0)
        {
            return (false, "RAR-архив не создан или пуст");
        }

        return (true, "Упаковка выполнена");
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
