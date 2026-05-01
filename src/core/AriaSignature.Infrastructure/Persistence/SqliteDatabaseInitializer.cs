using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer : ISqliteDatabaseInitializer
{
    private readonly string _connectionString;

    public SqliteDatabaseInitializer(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("AriaSignature") ?? "Data Source=ariasignature.db";
        _connectionString = NormalizeConnectionString(configured);
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
                HealthPercent INTEGER NULL,
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
                HealthPercent INTEGER NULL,
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

            CREATE TABLE IF NOT EXISTS AppSettings (
                [Key] TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;

        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await ApplyMigrationsAsync(connection, cancellationToken);
        await SeedDefaultSettingsAsync(connection, cancellationToken);
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, "Disks", "MediaType", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "Disks", "SsdLifeRemaining", "INTEGER NULL", cancellationToken);
        await MigrateHealthPercentColumnsToNullableAsync(connection, cancellationToken);
    }

    private static async Task MigrateHealthPercentColumnsToNullableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "Disks", "SmartMetrics" })
        {
            if (!await ColumnExistsAsync(connection, table, "HealthPercent", cancellationToken))
            {
                continue;
            }

            if (!await IsHealthPercentNotNullAsync(connection, table, cancellationToken))
            {
                continue;
            }

            try
            {
                await using var drop = connection.CreateCommand();
                drop.CommandText = $"ALTER TABLE {table} DROP COLUMN HealthPercent;";
                await drop.ExecuteNonQueryAsync(cancellationToken);
                await using var add = connection.CreateCommand();
                add.CommandText = $"ALTER TABLE {table} ADD COLUMN HealthPercent INTEGER NULL;";
                await add.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                /* SQLite без DROP COLUMN: схема остаётся NOT NULL */
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> IsHealthPercentNotNullAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!string.Equals(reader.GetString(1), "HealthPercent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return reader.GetInt32(3) != 0;
        }

        return false;
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string table,
        string column,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (existing.Contains(column))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedDefaultSettingsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await UpsertSettingIfMissingAsync(connection, "Api:Port", "5160", cancellationToken);
        await UpsertSettingIfMissingAsync(connection, "SmartMonitoring:Cron", "0 */1 * * * ?", cancellationToken);
    }

    private static async Task UpsertSettingIfMissingAsync(
        SqliteConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM AppSettings WHERE [Key] = $k LIMIT 1;";
        check.Parameters.AddWithValue("$k", key);
        var exists = await check.ExecuteScalarAsync(cancellationToken) is not null;
        if (exists)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO AppSettings ([Key], Value) VALUES ($k, $v);";
        insert.Parameters.AddWithValue("$k", key);
        insert.Parameters.AddWithValue("$v", value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (!Path.IsPathRooted(dataSource))
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AriaSignature");
            Directory.CreateDirectory(appDataDir);
            builder.DataSource = Path.Combine(appDataDir, dataSource);
        }

        return builder.ConnectionString;
    }
}
