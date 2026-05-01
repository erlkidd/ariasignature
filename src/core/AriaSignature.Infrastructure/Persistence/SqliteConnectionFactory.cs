using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("AriaSignature") ?? "Data Source=ariasignature.db";
        _connectionString = NormalizeConnectionString(configured);
    }

    public SqliteConnection Create()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = Create();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout = 15000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
