using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace AriaSignature.Infrastructure.Storage;

public sealed class SqliteAppSettingsService : IAppSettingsService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private const int MaxBusyRetries = 5;

    public SqliteAppSettingsService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Key], Value FROM AppSettings ORDER BY [Key];";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        return dict;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE [Key] = $k LIMIT 1;";
        command.Parameters.AddWithValue("$k", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO AppSettings ([Key], Value) VALUES ($k, $v)
                ON CONFLICT([Key]) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$v", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
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
