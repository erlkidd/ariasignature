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
