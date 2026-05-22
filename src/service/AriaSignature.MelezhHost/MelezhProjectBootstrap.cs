using Microsoft.Data.Sqlite;

namespace AriaSignature.MelezhHost;

public static class MelezhProjectBootstrap
{
    public static async Task<MelezhBootstrapEnsureResult> EnsureAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ProjectPath))
        {
            return new MelezhBootstrapEnsureResult(false, 0, 0);
        }

        var api = MelezhAgentApiSettingsReader.Read(options);
        var pullSchedule = new Dictionary<string, string>(
            MelezhPullCronSchedule.BuildForScheduledHandlers(),
            StringComparer.Ordinal);
        var envPullCron = Environment.GetEnvironmentVariable("ARIASIGNATURE_MELEZH_PULL_CRON");
        if (!string.IsNullOrWhiteSpace(envPullCron))
        {
            foreach (var key in pullSchedule.Keys.ToList())
            {
                pullSchedule[key] = envPullCron;
            }
        }

        var storedVersion = await GetBootstrapVersionAsync(options.ProjectPath, cancellationToken);
        var catalogDrift = await ProjectNeedsCatalogRepairAsync(options.ProjectPath, cancellationToken);
        var forceRepair = storedVersion < MelezhBootstrapSchema.CurrentVersion || catalogDrift;

        var prunedStart = await PruneOrphanHandlersAsync(options.ProjectPath, logger, cancellationToken);
        if (prunedStart > 0)
        {
            logger.LogInformation("Melezh bootstrap: pruned {Count} orphan handler(s) at start", prunedStart);
        }

        if (forceRepair)
        {
            await ClearScheduledTasksForCatalogAsync(options.ProjectPath, cancellationToken);
            logger.LogInformation(
                "Melezh bootstrap: repair mode (stored v{Stored}, target v{Target}, catalogDrift={Drift})",
                storedVersion,
                MelezhBootstrapSchema.CurrentVersion,
                catalogDrift);
        }

        foreach (var def in MelezhAriaApiHandlerCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureHandlerAsync(options, runCliAsync, def, api, forceRepair, logger, cancellationToken);
        }

        foreach (var def in MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cron = pullSchedule[def.Key];
            await EnsureScheduledTaskAsync(
                options,
                runCliAsync,
                def.Key,
                cron,
                forceReschedule: forceRepair,
                logger,
                cancellationToken);
        }

        var prunedAfter = await PruneOrphanHandlersAsync(options.ProjectPath, logger, cancellationToken);
        if (prunedAfter > 0)
        {
            logger.LogInformation("Melezh bootstrap: pruned {Count} orphan handler(s) after ensure", prunedAfter);
        }

        var prunedCron = await PruneOrphanSchedulerTasksAsync(options.ProjectPath, logger, cancellationToken);
        if (prunedCron > 0)
        {
            logger.LogInformation("Melezh bootstrap: pruned {Count} orphan scheduler task(s)", prunedCron);
        }

        await VerifyCatalogHandlersAsync(options, runCliAsync, api, logger, cancellationToken);

        await SetBootstrapVersionAsync(options.ProjectPath, MelezhBootstrapSchema.CurrentVersion, cancellationToken);

        var upgraded = storedVersion > 0 && storedVersion < MelezhBootstrapSchema.CurrentVersion;
        if (upgraded)
        {
            logger.LogInformation(
                "Melezh bootstrap: upgraded project schema {From} -> {To}",
                storedVersion,
                MelezhBootstrapSchema.CurrentVersion);
        }

        return new MelezhBootstrapEnsureResult(
            upgraded,
            storedVersion,
            MelezhBootstrapSchema.CurrentVersion);
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

        var maxRowId = await GetMaxHandlerRowIdAsync(options.ProjectPath, cancellationToken);

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
            await PruneHandlersAddedAfterRowIdAsync(options.ProjectPath, maxRowId, logger, cancellationToken);
            return;
        }

        var newKey = await TryGetHandlerKeyAfterRowIdAsync(options.ProjectPath, maxRowId, cancellationToken);
        if (newKey is null)
        {
            logger.LogWarning(
                "Melezh bootstrap: could not locate new handler row for {Key} after add (maxRowId={MaxRowId})",
                def.Key,
                maxRowId);
            await PruneHandlersAddedAfterRowIdAsync(options.ProjectPath, maxRowId, logger, cancellationToken);
            return;
        }

        if (string.Equals(newKey, def.Key, StringComparison.Ordinal))
        {
            await ApplyOutboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
            await ApplyInboundArgsAsync(options, runCliAsync, def, api, logger, cancellationToken);
            logger.LogInformation("Melezh bootstrap: handler {Key} ready", def.Key);
            return;
        }

        await RenameHandlerKeyAsync(options.ProjectPath, newKey, def.Key, cancellationToken);
        var renamed = await TryGetHandlerRowAsync(options.ProjectPath, def.Key, cancellationToken);
        if (renamed is null)
        {
            logger.LogWarning(
                "Melezh bootstrap: rename to {Key} failed (left {OldKey}); deleting stale row",
                def.Key,
                newKey);
            await DeleteHandlerAsync(options.ProjectPath, newKey, cancellationToken);
            await PruneHandlersAddedAfterRowIdAsync(options.ProjectPath, maxRowId, logger, cancellationToken);
            return;
        }

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
        bool forceReschedule,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var existingCron = await TryGetSchedulerCronAsync(options.ProjectPath, handlerKey, cancellationToken);
        if (!forceReschedule
            && existingCron is not null
            && string.Equals(existingCron, cron, StringComparison.Ordinal))
        {
            return;
        }

        if (existingCron is not null)
        {
            await DeleteSchedulerTaskAsync(options.ProjectPath, handlerKey, cancellationToken);
            logger.LogInformation(
                "Melezh bootstrap: rescheduling {Key} (was cron={OldCron}, new cron={Cron})",
                handlerKey,
                existingCron,
                cron);
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

    internal static async Task<bool> ProjectNeedsCatalogRepairAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var catalogKeys = MelezhAriaApiHandlerCatalog.All
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

        var presentCatalog = new HashSet<string>(StringComparer.Ordinal);
        var hasOrphans = false;

        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key FROM handlers";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            if (catalogKeys.Contains(key))
            {
                presentCatalog.Add(key);
                continue;
            }

            hasOrphans = true;
        }

        return hasOrphans || presentCatalog.Count != catalogKeys.Count;
    }

    private static async Task PruneHandlersAddedAfterRowIdAsync(
        string projectPath,
        long maxRowId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key FROM handlers WHERE rowid > $maxRowId";
        cmd.Parameters.AddWithValue("$maxRowId", maxRowId);
        var keys = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(reader.GetString(0));
            }
        }

        foreach (var key in keys)
        {
            await DeleteHandlerAsync(projectPath, key, cancellationToken);
            logger.LogDebug("Melezh bootstrap: removed failed-add handler {Key}", key);
        }
    }

    internal static async Task<int> PruneOrphanHandlersAsync(
        string projectPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var catalogKeys = MelezhAriaApiHandlerCatalog.All
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

        var orphanKeys = new List<string>();
        await using (var connection = OpenProject(projectPath))
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT key FROM handlers";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                if (!catalogKeys.Contains(key))
                {
                    orphanKeys.Add(key);
                }
            }
        }

        foreach (var key in orphanKeys)
        {
            await DeleteHandlerAsync(projectPath, key, cancellationToken);
            logger.LogDebug("Melezh bootstrap: pruned orphan handler {Key}", key);
        }

        return orphanKeys.Count;
    }

    private static async Task ClearScheduledTasksForCatalogAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var scheduledKeys = MelezhAriaApiHandlerCatalog.All
            .Where(d => d.ScheduleByDefault)
            .Select(d => d.Key)
            .ToList();

        await using var connection = OpenProject(projectPath);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var key in scheduledKeys)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM scheduler_tasks WHERE handler = $handler";
            cmd.Parameters.AddWithValue("$handler", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
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

    private static async Task<string?> TryGetSchedulerCronAsync(
        string projectPath,
        string handlerKey,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT cron FROM scheduler_tasks WHERE handler = $handler LIMIT 1";
        cmd.Parameters.AddWithValue("$handler", handlerKey);
        var raw = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static async Task DeleteSchedulerTaskAsync(
        string projectPath,
        string handlerKey,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduler_tasks WHERE handler = $handler";
        cmd.Parameters.AddWithValue("$handler", handlerKey);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<long> GetMaxHandlerRowIdAsync(string projectPath, CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(rowid), 0) FROM handlers";
        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        return raw is long l ? l : Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> TryGetHandlerKeyAfterRowIdAsync(
        string projectPath,
        long maxRowId,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenProject(projectPath);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key FROM handlers WHERE rowid > $maxRowId ORDER BY rowid ASC";
        cmd.Parameters.AddWithValue("$maxRowId", maxRowId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        string? found = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (found is not null)
            {
                return null;
            }

            found = reader.GetString(0);
        }

        return found;
    }

    internal static async Task RenameHandlerKeyAsync(
        string projectPath,
        string oldKey,
        string desiredKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(oldKey) || string.Equals(oldKey, desiredKey, StringComparison.Ordinal))
        {
            return;
        }

        await using var connection = OpenProject(projectPath);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE handlers SET key = $newKey WHERE key = $oldKey";
            update.Parameters.AddWithValue("$newKey", desiredKey);
            update.Parameters.AddWithValue("$oldKey", oldKey);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateArgs = connection.CreateCommand())
        {
            updateArgs.Transaction = tx;
            updateArgs.CommandText = "UPDATE arguments SET key = $newKey WHERE key = $oldKey";
            updateArgs.Parameters.AddWithValue("$newKey", desiredKey);
            updateArgs.Parameters.AddWithValue("$oldKey", oldKey);
            await updateArgs.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateSched = connection.CreateCommand())
        {
            updateSched.Transaction = tx;
            updateSched.CommandText = "UPDATE scheduler_tasks SET handler = $newKey WHERE handler = $oldKey";
            updateSched.Parameters.AddWithValue("$newKey", desiredKey);
            updateSched.Parameters.AddWithValue("$oldKey", oldKey);
            await updateSched.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<int> PruneOrphanSchedulerTasksAsync(
        string projectPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var allowedHandlers = MelezhAriaApiHandlerCatalog.All
            .Where(d => d.ScheduleByDefault)
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

        var orphanHandlers = new List<string>();
        await using (var connection = OpenProject(projectPath))
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT handler FROM scheduler_tasks";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var handler = reader.GetString(0);
                if (!allowedHandlers.Contains(handler))
                {
                    orphanHandlers.Add(handler);
                }
            }
        }

        foreach (var handler in orphanHandlers)
        {
            await DeleteSchedulerTaskAsync(projectPath, handler, cancellationToken);
            logger.LogDebug("Melezh bootstrap: pruned orphan scheduler for {Handler}", handler);
        }

        return orphanHandlers.Count;
    }

    private static async Task VerifyCatalogHandlersAsync(
        MelezhHostOptions options,
        Func<string, CancellationToken, Task<int>> runCliAsync,
        MelezhAgentApiSettings api,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var def in MelezhAriaApiHandlerCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await TryGetHandlerRowAsync(options.ProjectPath, def.Key, cancellationToken);
            if (row is not null)
            {
                continue;
            }

            logger.LogWarning("Melezh bootstrap: catalog handler {Key} missing after ensure; retrying", def.Key);
            await EnsureHandlerAsync(options, runCliAsync, def, api, forceRepair: true, logger, cancellationToken);
        }
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
