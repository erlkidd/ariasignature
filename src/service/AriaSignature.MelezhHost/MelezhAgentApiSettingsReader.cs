using Microsoft.Data.Sqlite;

namespace AriaSignature.MelezhHost;

public sealed record MelezhAgentApiSettings(int Port, string? SharedSecret)
{
    public string BaseUrl => $"http://127.0.0.1:{Port}/api/v1";
}

public static class MelezhAgentApiSettingsReader
{
    public static MelezhAgentApiSettings Read(MelezhHostOptions options)
    {
        var port = ResolveApiPort(options);
        var secret = TryReadSharedSecretFromDb();
        return new MelezhAgentApiSettings(port, secret);
    }

    private static int ResolveApiPort(MelezhHostOptions options)
    {
        var env = Environment.GetEnvironmentVariable("ARIASIGNATURE_API_PORT");
        if (int.TryParse(env, out var parsed) && parsed is > 0 and <= 65535)
        {
            return parsed;
        }

        var fromDb = TryReadSetting("Api:Port");
        if (int.TryParse(fromDb, out parsed) && parsed is > 0 and <= 65535)
        {
            return parsed;
        }

        return 5160;
    }

    private static string? TryReadSharedSecretFromDb() =>
        TryReadSetting("Api:SharedSecret");

    private static string? TryReadSetting(string key)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AriaSignature",
            "ariasignature.db");
        if (!File.Exists(dbPath))
        {
            return null;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE name = $name LIMIT 1";
            cmd.Parameters.AddWithValue("$name", key);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }
}
