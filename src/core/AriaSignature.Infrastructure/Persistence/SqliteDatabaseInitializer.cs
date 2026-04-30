using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer : ISqliteDatabaseInitializer
{
    private readonly string _connectionString;

    public SqliteDatabaseInitializer(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AriaSignature") ?? "Data Source=ariasignature.db";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = """
            CREATE TABLE IF NOT EXISTS Disks (
                Id TEXT PRIMARY KEY,
                Model TEXT NOT NULL,
                Serial TEXT NOT NULL,
                Interface TEXT NOT NULL,
                SizeTotalBytes INTEGER NOT NULL,
                SizeFreeBytes INTEGER NOT NULL,
                TemperatureCelsius INTEGER NOT NULL,
                HealthPercent INTEGER NOT NULL,
                PowerOnHours INTEGER NOT NULL,
                PowerCycleCount INTEGER NOT NULL,
                ReallocatedSectors INTEGER NOT NULL,
                PendingSectors INTEGER NOT NULL,
                UncorrectableErrors INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SmartMetrics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DiskId TEXT NOT NULL,
                TemperatureCelsius INTEGER NOT NULL,
                HealthPercent INTEGER NOT NULL,
                ReallocatedSectors INTEGER NOT NULL,
                PendingSectors INTEGER NOT NULL,
                UncorrectableErrors INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                TimestampUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BackupJobs (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL,
                Source TEXT NOT NULL,
                Destination TEXT NOT NULL,
                ScheduleCron TEXT NOT NULL,
                RetentionCount INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BackupLogs (
                Id TEXT PRIMARY KEY,
                JobId TEXT NOT NULL,
                Status INTEGER NOT NULL,
                StartTimeUtc TEXT NOT NULL,
                EndTimeUtc TEXT NULL,
                FileSizeBytes INTEGER NULL,
                Message TEXT NOT NULL
            );
            """;

        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
