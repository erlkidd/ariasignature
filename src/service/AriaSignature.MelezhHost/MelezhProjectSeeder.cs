using Microsoft.Data.Sqlite;

namespace AriaSignature.MelezhHost;

/// <summary>Seeds default HTTP handlers (aria_ping, aria_sync) into a new .melezh SQLite project.</summary>
internal static class MelezhProjectSeeder
{
    public static async Task EnsureDefaultHandlersAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ProjectPath))
        {
            return;
        }

        if (await HasHandlerAsync(options.ProjectPath, "aria_ping", cancellationToken) &&
            await HasHandlerAsync(options.ProjectPath, "aria_sync", cancellationToken))
        {
            return;
        }

        await TryAddHandlerAsync(options, runCliAsync, "http", "Get", "get", "aria_ping", cancellationToken);
        await TryAddHandlerAsync(options, runCliAsync, "http", "Post", "json", "aria_sync", cancellationToken);
    }

    private static async Task TryAddHandlerAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        string library,
        string function,
        string method,
        string desiredKey,
        CancellationToken cancellationToken)
    {
        if (await HasHandlerAsync(options.ProjectPath, desiredKey, cancellationToken))
        {
            return;
        }

        var args =
            $"{MelezhCliCommands.AddRequestsHandlerMethod} --proj {MelezhCliCommands.QuoteArg(options.ProjectPath)} --lib {library} --func {function} --method {method}";
        var exit = await runCliAsync(args, cancellationToken);
        if (exit != 0)
        {
            return;
        }

        await RenameLatestHandlerKeyAsync(options.ProjectPath, library, function, method, desiredKey, cancellationToken);
    }

    private static async Task<bool> HasHandlerAsync(string projectPath, string key, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={projectPath}");
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM handlers WHERE key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task RenameLatestHandlerKeyAsync(
        string projectPath,
        string library,
        string function,
        string method,
        string desiredKey,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={projectPath}");
        await connection.OpenAsync(cancellationToken);
        await using var select = connection.CreateCommand();
        select.CommandText =
            "SELECT key FROM handlers WHERE library = $lib AND function = $func AND upper(method) = $method ORDER BY rowid DESC LIMIT 1";
        select.Parameters.AddWithValue("$lib", library);
        select.Parameters.AddWithValue("$func", function);
        select.Parameters.AddWithValue("$method", method.ToUpperInvariant());
        var oldKey = await select.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(oldKey) || string.Equals(oldKey, desiredKey, StringComparison.Ordinal))
        {
            return;
        }

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE handlers SET key = $newKey WHERE key = $oldKey";
        update.Parameters.AddWithValue("$newKey", desiredKey);
        update.Parameters.AddWithValue("$oldKey", oldKey);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }
}
