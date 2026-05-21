using Microsoft.Data.Sqlite;

namespace AriaSignature.MelezhHost;

public static class MelezhProjectBootstrap
{
    public static async Task EnsureAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ProjectPath))
        {
            return;
        }

        var api = MelezhAgentApiSettingsReader.Read(options);
        var pullCron = Environment.GetEnvironmentVariable("ARIASIGNATURE_MELEZH_PULL_CRON")
            ?? MelezhAriaApiHandlerCatalog.DefaultPullCron;

        var storedVersion = await GetBootstrapVersionAsync(options.ProjectPath, cancellationToken);
        var forceRepair = storedVersion < MelezhBootstrapSchema.CurrentVersion;

        foreach (var def in MelezhAriaApiHandlerCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureHandlerAsync(options, runCliAsync, def, api, forceRepair, logger, cancellationToken);
        }

        foreach (var def in MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureScheduledTaskAsync(options, runCliAsync, def.Key, pullCron, logger, cancellationToken);
        }

        await SetBootstrapVersionAsync(options.ProjectPath, MelezhBootstrapSchema.CurrentVersion, cancellationToken);
    }

    private static async Task EnsureHandlerAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        MelezhHandlerDefinition def,
        MelezhAgentApiSettings api,
        bool forceRepair,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var existing = await TryGetHandlerRowAsync(options.ProjectPath, def.Key, cancellationToken);
        if (existing is not null && !forceRepair && HandlerRowMatches(existing.Value, def))
        {
            await ApplyOutboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
            await ApplyInboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
            return;
        }

        if (existing is not null)
        {
            await DeleteHandlerAsync(options.ProjectPath, def.Key, cancellationToken);
            logger.LogInformation(
                "Melezh bootstrap: repairing handler {Key} (library={Lib} func={Func} method={Method})",
                def.Key,
                def.OintLibrary,
                def.OintFunction,
                def.OintMethod);
        }

        var addArgs =
            $"{MelezhCliCommands.AddRequestsHandlerMethod} --proj {MelezhCliCommands.QuoteArg(options.ProjectPath)} --lib {def.OintLibrary} --func {def.OintFunction} --method {def.OintMethod}";
        var exit = await runCliAsync(addArgs, cancellationToken);
        if (exit != 0)
        {
            logger.LogWarning(
                "Melezh bootstrap: {Method} failed exit={Exit} for {Key}",
                MelezhCliCommands.AddRequestsHandlerMethod,
                exit,
                def.Key);
            return;
        }

        await RenameLatestHandlerKeyAsync(
            options.ProjectPath,
            def.OintLibrary,
            def.OintFunction,
            def.OintMethod,
            def.Key,
            cancellationToken);

        await ApplyOutboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
        await ApplyInboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
        logger.LogInformation("Melezh bootstrap: handler {Key} ready", def.Key);
    }

    private static async Task ApplyInboundArgsAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        MelezhHandlerDefinition def,
        MelezhAgentApiSettings api,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (def.Direction != MelezhHandlerDirection.Inbound || def.Key != "aria_sync")
        {
            return;
        }

        var ingestUrl = $"{api.BaseUrl.TrimEnd('/')}/melezh/ingest";
        await SetHandlerArgumentAsync(options, runCliAsync, def.Key, "url", ingestUrl, logger, cancellationToken);
    }

    private static async Task ApplyOutboundArgsAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        MelezhHandlerDefinition def,
        MelezhAgentApiSettings api,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (def.Direction != MelezhHandlerDirection.OutboundToAria || def.ApiPathTemplate is null)
        {
            return;
        }

        var url = BuildAriaUrl(api, def);
        await SetHandlerArgumentAsync(options, runCliAsync, def.Key, "url", url, logger, cancellationToken);

        if (!string.IsNullOrWhiteSpace(api.SharedSecret))
        {
            await SetHandlerArgumentAsync(
                options,
                runCliAsync,
                def.Key,
                "headers",
                $"Authorization: Bearer {api.SharedSecret}",
                logger,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(def.DefaultBodyJson))
        {
            await SetHandlerArgumentAsync(
                options,
                runCliAsync,
                def.Key,
                "data",
                def.DefaultBodyJson,
                logger,
                cancellationToken);
        }
    }

    private static bool HandlerRowMatches(
        (string Library, string Function, string Method) row,
        MelezhHandlerDefinition def) =>
        string.Equals(row.Library, def.OintLibrary, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Function, def.OintFunction, StringComparison.Ordinal)
        && string.Equals(row.Method, def.OintMethod, StringComparison.OrdinalIgnoreCase);

    private static string BuildAriaUrl(MelezhAgentApiSettings api, MelezhHandlerDefinition def)
    {
        var path = def.ApiPathTemplate!;
        var placeholderId = "00000000-0000-0000-0000-000000000001";
        path = path.Replace("{diskId}", placeholderId, StringComparison.Ordinal)
            .Replace("{backupId}", placeholderId, StringComparison.Ordinal);
        return $"{api.BaseUrl.TrimEnd('/')}{path}";
    }

    private static async Task SetHandlerArgumentAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        string handlerKey,
        string arg,
        string value,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var args =
            $"{MelezhCliCommands.SetHandlerArgumentMethod} --proj {MelezhCliCommands.QuoteArg(options.ProjectPath)} --handler {handlerKey} --arg {arg} --value {MelezhCliCommands.QuoteArg(value)}";
        var exit = await runCliAsync(args, cancellationToken);
        if (exit != 0)
        {
            logger.LogDebug(
                "Melezh bootstrap: set arg {Arg} on {Key} exit={Exit}",
                arg,
                handlerKey,
                exit);
        }
    }

    private static async Task EnsureScheduledTaskAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        string handlerKey,
        string cron,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await SchedulerTaskExistsAsync(options.ProjectPath, handlerKey, cancellationToken))
        {
            return;
        }

        var args =
            $"{MelezhCliCommands.AddScheduledTaskMethod} --proj {MelezhCliCommands.QuoteArg(options.ProjectPath)} --handler {handlerKey} --cron {MelezhCliCommands.QuoteArg(cron)}";
        var exit = await runCliAsync(args, cancellationToken);
        if (exit != 0)
        {
            logger.LogWarning(
                "Melezh bootstrap: schedule for {Key} failed exit={Exit}",
                handlerKey,
                exit);
            return;
        }

        logger.LogInformation("Melezh bootstrap: scheduled task for {Key} cron={Cron}", handlerKey, cron);
    }

    private static async Task<(string Library, string Function, string Method)?> TryGetHandlerRowAsync(
        string projectPath,
        string key,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT library, function, method FROM handlers WHERE key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task DeleteHandlerAsync(string projectPath, string key, CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM arguments WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM scheduler_tasks WHERE handler = $handler";
            cmd.Parameters.AddWithValue("$handler", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM handlers WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<bool> SchedulerTaskExistsAsync(
        string projectPath,
        string handlerKey,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM scheduler_tasks WHERE handler = $handler LIMIT 1";
        cmd.Parameters.AddWithValue("$handler", handlerKey);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task RenameLatestHandlerKeyAsync(
        string projectPath,
        string library,
        string function,
        string method,
        string desiredKey,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
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

        await using var updateArgs = connection.CreateCommand();
        updateArgs.CommandText = "UPDATE arguments SET key = $newKey WHERE key = $oldKey";
        updateArgs.Parameters.AddWithValue("$newKey", desiredKey);
        updateArgs.Parameters.AddWithValue("$oldKey", oldKey);
        await updateArgs.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetBootstrapVersionAsync(string projectPath, CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE name = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$name", MelezhBootstrapSchema.VersionSettingKey);
        var raw = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return int.TryParse(raw, out var version) ? version : 0;
    }

    private static async Task SetBootstrapVersionAsync(
        string projectPath,
        int version,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO settings(name, value) VALUES ($name, $value) ON CONFLICT(name) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$name", MelezhBootstrapSchema.VersionSettingKey);
        cmd.Parameters.AddWithValue("$value", version.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteConnection OpenProject(string projectPath)
    {
        var connection = new SqliteConnection($"Data Source={projectPath}");
        connection.Open();
        return connection;
    }
}
