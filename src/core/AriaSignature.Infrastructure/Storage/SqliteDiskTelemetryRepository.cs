using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using AriaSignature.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace AriaSignature.Infrastructure.Storage;

public sealed class SqliteDiskTelemetryRepository : IDiskTelemetryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteDiskTelemetryRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertDisksAsync(IReadOnlyCollection<Disk> disks, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var disksTemperatureNotNull = await IsColumnNotNullAsync(connection, "Disks", "TemperatureCelsius", cancellationToken);
        var metricsTemperatureNotNull = await IsColumnNotNullAsync(connection, "SmartMetrics", "TemperatureCelsius", cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var disk in disks)
        {
            var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO Disks (Id, Model, Serial, Interface, MediaType, SizeTotalBytes, SizeFreeBytes, SsdLifeRemaining, TemperatureCelsius, HealthPercent, PowerOnHours, PowerCycleCount, ReallocatedSectors, PendingSectors, UncorrectableErrors, SmartCtlUsed, WmiUsed, StorageReliabilityUsed, TelemetryConfidence, TelemetryDegradationReason, Status, UpdatedAtUtc)
                VALUES ($Id, $Model, $Serial, $Interface, $MediaType, $SizeTotalBytes, $SizeFreeBytes, $SsdLifeRemaining, $TemperatureCelsius, $HealthPercent, $PowerOnHours, $PowerCycleCount, $ReallocatedSectors, $PendingSectors, $UncorrectableErrors, $SmartCtlUsed, $WmiUsed, $StorageReliabilityUsed, $TelemetryConfidence, $TelemetryDegradationReason, $Status, $UpdatedAtUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    Model = excluded.Model,
                    Serial = excluded.Serial,
                    Interface = excluded.Interface,
                    MediaType = excluded.MediaType,
                    SizeTotalBytes = excluded.SizeTotalBytes,
                    SizeFreeBytes = excluded.SizeFreeBytes,
                    SsdLifeRemaining = excluded.SsdLifeRemaining,
                    TemperatureCelsius = excluded.TemperatureCelsius,
                    HealthPercent = excluded.HealthPercent,
                    PowerOnHours = excluded.PowerOnHours,
                    PowerCycleCount = excluded.PowerCycleCount,
                    ReallocatedSectors = excluded.ReallocatedSectors,
                    PendingSectors = excluded.PendingSectors,
                    UncorrectableErrors = excluded.UncorrectableErrors,
                    SmartCtlUsed = excluded.SmartCtlUsed,
                    WmiUsed = excluded.WmiUsed,
                    StorageReliabilityUsed = excluded.StorageReliabilityUsed,
                    TelemetryConfidence = excluded.TelemetryConfidence,
                    TelemetryDegradationReason = excluded.TelemetryDegradationReason,
                    Status = excluded.Status,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            BindDisk(upsert, disk, disksTemperatureNotNull);
            await upsert.ExecuteNonQueryAsync(cancellationToken);

            var metric = connection.CreateCommand();
            metric.Transaction = transaction;
            metric.CommandText = """
                INSERT INTO SmartMetrics (DiskId, TemperatureCelsius, HealthPercent, ReallocatedSectors, PendingSectors, UncorrectableErrors, Status, TimestampUtc)
                VALUES ($DiskId, $TemperatureCelsius, $HealthPercent, $ReallocatedSectors, $PendingSectors, $UncorrectableErrors, $Status, $TimestampUtc);
                """;
            metric.Parameters.AddWithValue("$DiskId", disk.Id.ToString());
            metric.Parameters.AddWithValue(
                "$TemperatureCelsius",
                disk.TemperatureCelsius.HasValue
                    ? disk.TemperatureCelsius.Value
                    : metricsTemperatureNotNull ? 0 : (object)DBNull.Value);
            metric.Parameters.AddWithValue("$HealthPercent", disk.HealthPercent.HasValue ? disk.HealthPercent.Value : (object)DBNull.Value);
            metric.Parameters.AddWithValue("$ReallocatedSectors", disk.ReallocatedSectors);
            metric.Parameters.AddWithValue("$PendingSectors", disk.PendingSectors);
            metric.Parameters.AddWithValue("$UncorrectableErrors", disk.UncorrectableErrors);
            metric.Parameters.AddWithValue("$Status", (int)disk.Status);
            metric.Parameters.AddWithValue("$TimestampUtc", disk.UpdatedAtUtc.UtcDateTime.ToString("O"));
            await metric.ExecuteNonQueryAsync(cancellationToken);
        }

        var trimMetrics = connection.CreateCommand();
        trimMetrics.Transaction = transaction;
        trimMetrics.CommandText = """
            DELETE FROM SmartMetrics
            WHERE Id NOT IN (
                SELECT Id FROM SmartMetrics
                ORDER BY TimestampUtc DESC
                LIMIT 5000
            );
            """;
        await trimMetrics.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Model, Serial, Interface, MediaType, SizeTotalBytes, SizeFreeBytes, SsdLifeRemaining, TemperatureCelsius, HealthPercent, PowerOnHours, PowerCycleCount, ReallocatedSectors, PendingSectors, UncorrectableErrors, SmartCtlUsed, WmiUsed, StorageReliabilityUsed, TelemetryConfidence, TelemetryDegradationReason, Status, UpdatedAtUtc
            FROM Disks ORDER BY Model;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var disks = new List<Disk>();
        while (await reader.ReadAsync(cancellationToken))
        {
            disks.Add(ReadDisk(reader));
        }

        return disks;
    }

    public async Task<Disk?> GetDiskAsync(Guid diskId, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Model, Serial, Interface, MediaType, SizeTotalBytes, SizeFreeBytes, SsdLifeRemaining, TemperatureCelsius, HealthPercent, PowerOnHours, PowerCycleCount, ReallocatedSectors, PendingSectors, UncorrectableErrors, SmartCtlUsed, WmiUsed, StorageReliabilityUsed, TelemetryConfidence, TelemetryDegradationReason, Status, UpdatedAtUtc
            FROM Disks WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$Id", diskId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadDisk(reader) : null;
    }

    public async Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid diskId, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DiskId, TemperatureCelsius, HealthPercent, ReallocatedSectors, PendingSectors, UncorrectableErrors, Status, TimestampUtc
            FROM SmartMetrics
            WHERE DiskId = $DiskId
            ORDER BY TimestampUtc DESC
            LIMIT 500;
            """;
        command.Parameters.AddWithValue("$DiskId", diskId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var metrics = new List<SmartMetric>();
        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add(new SmartMetric
            {
                DiskId = Guid.Parse(reader.GetString(0)),
                TemperatureCelsius = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                HealthPercent = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ReallocatedSectors = reader.GetInt32(3),
                PendingSectors = reader.GetInt32(4),
                UncorrectableErrors = reader.GetInt32(5),
                Status = (DiskHealthStatus)reader.GetInt32(6),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(7))
            });
        }

        return metrics;
    }

    private static void BindDisk(SqliteCommand command, Disk disk, bool temperatureNotNull)
    {
        command.Parameters.AddWithValue("$Id", disk.Id.ToString());
        command.Parameters.AddWithValue("$Model", disk.Model);
        command.Parameters.AddWithValue("$Serial", disk.Serial);
        command.Parameters.AddWithValue("$Interface", disk.Interface);
        command.Parameters.AddWithValue("$MediaType", disk.MediaType);
        command.Parameters.AddWithValue("$SizeTotalBytes", disk.SizeTotalBytes);
        command.Parameters.AddWithValue("$SizeFreeBytes", disk.SizeFreeBytes);
        command.Parameters.AddWithValue("$SsdLifeRemaining", disk.SsdLifeRemainingPercent.HasValue ? disk.SsdLifeRemainingPercent.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$TemperatureCelsius",
            disk.TemperatureCelsius.HasValue
                ? disk.TemperatureCelsius.Value
                : temperatureNotNull ? 0 : (object)DBNull.Value);
        command.Parameters.AddWithValue("$HealthPercent", disk.HealthPercent.HasValue ? disk.HealthPercent.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$PowerOnHours", disk.PowerOnHours);
        command.Parameters.AddWithValue("$PowerCycleCount", disk.PowerCycleCount);
        command.Parameters.AddWithValue("$ReallocatedSectors", disk.ReallocatedSectors);
        command.Parameters.AddWithValue("$PendingSectors", disk.PendingSectors);
        command.Parameters.AddWithValue("$UncorrectableErrors", disk.UncorrectableErrors);
        command.Parameters.AddWithValue("$SmartCtlUsed", disk.SmartCtlUsed ? 1 : 0);
        command.Parameters.AddWithValue("$WmiUsed", disk.WmiUsed ? 1 : 0);
        command.Parameters.AddWithValue("$StorageReliabilityUsed", disk.StorageReliabilityUsed ? 1 : 0);
        command.Parameters.AddWithValue("$TelemetryConfidence", disk.TelemetryConfidence);
        command.Parameters.AddWithValue("$TelemetryDegradationReason", disk.TelemetryDegradationReason ?? string.Empty);
        command.Parameters.AddWithValue("$Status", (int)disk.Status);
        command.Parameters.AddWithValue("$UpdatedAtUtc", disk.UpdatedAtUtc.UtcDateTime.ToString("O"));
    }

    private static Disk ReadDisk(SqliteDataReader reader)
    {
        return new Disk
        {
            Id = Guid.Parse(reader.GetString(0)),
            Model = reader.GetString(1),
            Serial = reader.GetString(2),
            Interface = reader.GetString(3),
            MediaType = reader.GetString(4),
            SizeTotalBytes = reader.GetInt64(5),
            SizeFreeBytes = reader.GetInt64(6),
            SsdLifeRemainingPercent = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            TemperatureCelsius = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            HealthPercent = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            PowerOnHours = reader.GetInt64(10),
            PowerCycleCount = reader.GetInt64(11),
            ReallocatedSectors = reader.GetInt32(12),
            PendingSectors = reader.GetInt32(13),
            UncorrectableErrors = reader.GetInt32(14),
            SmartCtlUsed = reader.GetInt32(15) == 1,
            WmiUsed = reader.GetInt32(16) == 1,
            StorageReliabilityUsed = reader.GetInt32(17) == 1,
            TelemetryConfidence = reader.GetInt32(18),
            TelemetryDegradationReason = reader.GetString(19),
            Status = (DiskHealthStatus)reader.GetInt32(20),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(21))
        };
    }

    private static async Task<bool> IsColumnNotNullAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return reader.GetInt32(3) != 0;
        }

        return false;
    }
}
