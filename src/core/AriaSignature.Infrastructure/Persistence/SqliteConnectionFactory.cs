using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AriaSignature") ?? "Data Source=ariasignature.db";
    }

    public SqliteConnection Create()
    {
        return new SqliteConnection(_connectionString);
    }
}
