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

        foreach (var def in MelezhAriaApiHandlerCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureHandlerAsync(options, runCliAsync, def, api, logger, cancellationToken);
        }

        foreach (var def in MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureScheduledTaskAsync(options, runCliAsync, def.Key, pullCron, logger, cancellationToken);
        }
    }

    private static async Task EnsureHandlerAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        MelezhHandlerDefinition def,
        MelezhAgentApiSettings api,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await HandlerExistsAsync(options.ProjectPath, def.Key, cancellationToken))
        {
            return;
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

        if (def.Direction == MelezhHandlerDirection.OutboundToAria && def.ApiPathTemplate is not null)
        {
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

        logger.LogInformation("Melezh bootstrap: handler {Key} ready", def.Key);
    }

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

    private static async Task<bool> HandlerExistsAsync(string projectPath, string key, CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM handlers WHERE key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
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

    private static SqliteConnection OpenProject(string projectPath)
    {
        var connection = new SqliteConnection($"Data Source={projectPath}");
        connection.Open();
        return connection;
    }
}
