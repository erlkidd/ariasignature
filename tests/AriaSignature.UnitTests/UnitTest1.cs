using System.Reflection;
using System.Text.Json;
using AriaSignature.Application.Services;
using AriaSignature.Application.Telemetry;
using AriaSignature.Domain.Enums;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Melezh;
using AriaSignature.Infrastructure.Monitoring;
using AriaSignature.MelezhHost;
using Microsoft.Extensions.Logging.Abstractions;

namespace AriaSignature.UnitTests;

public class BackupScheduleEvaluatorTests
{
    [Fact]
    public void IsCronDue_MatchesDailyAtTwoAm_WhenLocalMinuteIsTwoEvenWithNonZeroSeconds()
    {
        var localTwoAm = new DateTime(2026, 5, 19, 2, 0, 0, DateTimeKind.Unspecified);
        Assert.True(BackupScheduleEvaluator.IsCronDue("0 0 2 * * ?", localTwoAm));
    }

    [Fact]
    public void IsCronDue_RejectsDailyAtTwoAm_WhenLocalMinuteIsThree()
    {
        var localThreeAm = new DateTime(2026, 5, 19, 3, 0, 0, DateTimeKind.Unspecified);
        Assert.False(BackupScheduleEvaluator.IsCronDue("0 0 2 * * ?", localThreeAm));
    }

    [Fact]
    public void CreateContext_UsesLocalMinuteWindow_ForDedupUtcBoundary()
    {
        var localTz = TimeZoneInfo.Local;
        var localMinute = new DateTime(2026, 5, 19, 2, 0, 0, DateTimeKind.Unspecified);
        var nowUtc = new DateTimeOffset(localMinute, localTz.GetUtcOffset(localMinute)).ToUniversalTime().AddSeconds(17);
        var context = BackupScheduleEvaluator.CreateContext(nowUtc);

        Assert.Equal(localMinute, context.LocalMinuteStart);
        Assert.Equal(
            new DateTimeOffset(localMinute, localTz.GetUtcOffset(localMinute)).ToUniversalTime(),
            context.MinuteWindowStartUtc);
    }
}

public class SmartCtlLowLevelReaderTests
{
    [Fact]
    public void ParseSnapshot_UsesAtaPowerAttributes_WhenTopLevelMissing()
    {
        var json = """
                   {
                     "ata_smart_attributes": {
                       "table": [
                         { "id": 9, "name": "Power_On_Hours", "raw": { "value": 2450 } },
                         { "id": 12, "name": "Power_Cycle_Count", "raw": { "value": 315 } }
                       ]
                     }
                   }
                   """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = InvokeParseSnapshot(doc.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(2450, snapshot!.PowerOnHours);
        Assert.Equal(315, snapshot.PowerCycleCount);
    }

    [Fact]
    public void ParseSnapshot_NormalizesWearAttributes_ToRemainingPercent()
    {
        var json = """
                   {
                     "ata_smart_attributes": {
                       "table": [
                         { "id": 233, "name": "Wear_Leveling_Count", "raw": { "value": 20 } }
                       ]
                     }
                   }
                   """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = InvokeParseSnapshot(doc.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(80, snapshot!.SsdLifeRemainingPercent);
    }

    [Fact]
    public void ParseSnapshot_UsesNormalizedValue_WhenRawIsLarge()
    {
        var json = """
                   {
                     "ata_smart_attributes": {
                       "table": [
                         { "id": 231, "name": "SSD_Life_Left", "value": 95, "raw": { "value": 1234567890 } }
                       ]
                     }
                   }
                   """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = InvokeParseSnapshot(doc.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(95, snapshot!.SsdLifeRemainingPercent);
    }

    [Fact]
    public void ParseSnapshot_UsesNvmePercentageUsed_ForRemainingLife()
    {
        var json = """
                   {
                     "nvme_smart_health_information_log": {
                       "percentage_used": 12
                     }
                   }
                   """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = InvokeParseSnapshot(doc.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(88, snapshot!.SsdLifeRemainingPercent);
    }

    private static SmartCtlLowLevelReader.Snapshot? InvokeParseSnapshot(JsonElement element)
    {
        var reader = new SmartCtlLowLevelReader(new NullLogger<SmartCtlLowLevelReader>());
        var method = typeof(SmartCtlLowLevelReader)
            .GetMethod("ParseSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return (SmartCtlLowLevelReader.Snapshot?)method!.Invoke(reader, [element, @"\\.\PhysicalDrive0"]);
    }
}

public class BackupExecutorIoTests
{
    [Fact]
    public void DescribeFileIoForOperator_LeavesDiskSpaceMessageIntact()
    {
        var method = typeof(BackupExecutor)
            .GetMethod("DescribeFileIoForOperator", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var ex = new IOException("There is not enough space on the disk.");
        var message = (string?)method!.Invoke(null, [ex]);

        Assert.NotNull(message);
        Assert.Contains("not enough space", message!, StringComparison.OrdinalIgnoreCase);
    }
}

public class MelezhCliCommandsTests
{
    [Fact]
    public void BuildCreateProjectArgs_UsesRussianMethodAndQuotedPath()
    {
        var args = MelezhCliCommands.BuildCreateProjectArgs(@"C:\ProgramData\AriaSignature\melezh\AriaSignature.melezh");
        Assert.StartsWith(MelezhCliCommands.CreateProjectMethod, args, StringComparison.Ordinal);
        Assert.Contains("--path \"C:\\ProgramData\\AriaSignature\\melezh\\AriaSignature.melezh\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateProject", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRunProjectArgs_UsesRussianMethodPortAndProj()
    {
        var args = MelezhCliCommands.BuildRunProjectArgs(@"C:\data\proj.melezh", 7788);
        Assert.StartsWith(MelezhCliCommands.RunProjectMethod, args, StringComparison.Ordinal);
        Assert.Contains("--port 7788", args, StringComparison.Ordinal);
        Assert.Contains("--proj \"C:\\data\\proj.melezh\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("RunProject", args, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOscriptInvocation_QuotesAppOsPathWithSpaces()
    {
        var oscript = @"C:\Program Files\AriaSignature\melezh\lib\oint\bin\oscript.exe";
        var appOs = @"C:\Program Files\AriaSignature\melezh\share\oint\lib\melezh\core\Classes\app.os";
        var cliArgs = MelezhCliCommands.BuildRunProjectArgs(@"C:\ProgramData\p.melezh", 7788);

        var (fileName, arguments, workingDirectory) = MelezhCliCommands.ResolveOscriptInvocation(oscript, appOs, cliArgs);

        Assert.Equal(oscript, fileName);
        Assert.StartsWith($"\"{appOs}\" {MelezhCliCommands.RunProjectMethod}", arguments, StringComparison.Ordinal);
        Assert.Equal(@"C:\Program Files\AriaSignature\melezh\lib\oint\bin", workingDirectory);
    }
}

public class DiskTelemetryRulesTests
{
    [Theory]
    [InlineData("Fixed hard disk media", "SATA", "ST1000DM010", null, "HDD")]
    [InlineData("", "NVMe", "Samsung PM9A1", null, "NVMe")]
    [InlineData("SSD", "SATA", "ADATA SU650", null, "SSD")]
    public void NormalizeMediaType_PrefersStorageMapAndHeuristics(
        string raw,
        string iface,
        string model,
        int? physicalIndex,
        string expected)
    {
        IReadOnlyDictionary<int, string>? map = physicalIndex is int idx
            ? new Dictionary<int, string> { [idx] = "HDD" }
            : null;
        var actual = DiskTelemetryRules.NormalizeMediaType(raw, physicalIndex, iface, model, map);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeSsdLife_ReturnsNull_WhenZeroWithoutExplicitEndOfLife()
    {
        Assert.Null(DiskTelemetryRules.NormalizeSsdLifeRemainingPercent(0, explicitEndOfLife: false));
        Assert.Equal(42, DiskTelemetryRules.NormalizeSsdLifeRemainingPercent(42, explicitEndOfLife: false));
        Assert.Equal(0, DiskTelemetryRules.NormalizeSsdLifeRemainingPercent(0, explicitEndOfLife: true));
        Assert.Equal(5, DiskTelemetryRules.NormalizeSsdLifeRemainingPercent(5, explicitEndOfLife: true));
    }

    [Fact]
    public void MergeSsdLife_PrefersHighestTrustedRemaining_IgnoresStrayZero()
    {
        Assert.Equal(95, DiskTelemetryRules.MergeSsdLifeRemainingPercent(95, 0, incomingEndOfLife: false));
        Assert.Equal(95, DiskTelemetryRules.MergeSsdLifeRemainingPercent(80, 95, incomingEndOfLife: false));
        Assert.Equal(0, DiskTelemetryRules.MergeSsdLifeRemainingPercent(80, 0, incomingEndOfLife: true));
    }

    [Fact]
    public void ShouldPublishHealthPercent_RequiresSectorSignal()
    {
        Assert.False(DiskTelemetryRules.ShouldPublishHealthPercent(60, hasSectorSignal: false, predictFailure: false));
        Assert.True(DiskTelemetryRules.ShouldPublishHealthPercent(60, hasSectorSignal: true, predictFailure: false));
        Assert.True(DiskTelemetryRules.ShouldPublishHealthPercent(40, hasSectorSignal: false, predictFailure: true));
    }

    [Fact]
    public void NormalizeMediaType_StorageMapOverridesRawFixedDisk()
    {
        var map = new Dictionary<int, string> { [0] = "HDD" };
        var actual = DiskTelemetryRules.NormalizeMediaType("Fixed hard disk media", 0, "SCSI", "Unknown", map);
        Assert.Equal("HDD", actual);
    }
}

public class DiskHealthEngineTests
{
    [Fact]
    public void Compute_WithSsdWear80_ReturnsApproximately80()
    {
        var a = DiskHealthEngine.Compute(new DiskHealthInput(
            SsdLifeRemainingPercent: 80,
            SsdLifeEndOfLife: false,
            TemperatureCelsius: 42,
            PowerOnHours: 1000,
            ReallocatedSectors: 0,
            PendingSectors: 0,
            UncorrectableErrors: 0,
            PredictFailure: false,
            SmartPassed: true,
            NvmeCriticalWarning: null,
            VendorHealthPercent: null,
            SmartCtlUsed: true,
            StorageReliabilityUsed: false,
            WmiUsed: false));

        Assert.Equal(80, a.HealthPercent);
        Assert.Equal(DiskHealthStatus.Ok, a.Status);
        Assert.Contains("80%", a.HealthSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_WithReallocated5_LowersHealth()
    {
        var a = DiskHealthEngine.Compute(new DiskHealthInput(
            SsdLifeRemainingPercent: null,
            SsdLifeEndOfLife: false,
            TemperatureCelsius: null,
            PowerOnHours: 0,
            ReallocatedSectors: 5,
            PendingSectors: 0,
            UncorrectableErrors: 0,
            PredictFailure: false,
            SmartPassed: null,
            NvmeCriticalWarning: null,
            VendorHealthPercent: null,
            SmartCtlUsed: true,
            StorageReliabilityUsed: false,
            WmiUsed: false));

        Assert.NotNull(a.HealthPercent);
        Assert.True(a.HealthPercent < 100);
        Assert.Contains("Reallocated", a.HealthSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_PredictFailure_CapsCritical()
    {
        var a = DiskHealthEngine.Compute(new DiskHealthInput(
            SsdLifeRemainingPercent: 90,
            SsdLifeEndOfLife: false,
            TemperatureCelsius: 40,
            PowerOnHours: 500,
            ReallocatedSectors: 0,
            PendingSectors: 0,
            UncorrectableErrors: 0,
            PredictFailure: true,
            SmartPassed: null,
            NvmeCriticalWarning: null,
            VendorHealthPercent: null,
            SmartCtlUsed: false,
            StorageReliabilityUsed: false,
            WmiUsed: true));

        Assert.True(a.HealthPercent <= 35);
        Assert.Equal(DiskHealthStatus.Critical, a.Status);
    }

    [Fact]
    public void Compute_TempAndHoursOnly_ReturnsNullHealth()
    {
        var a = DiskHealthEngine.Compute(new DiskHealthInput(
            SsdLifeRemainingPercent: null,
            SsdLifeEndOfLife: false,
            TemperatureCelsius: 38,
            PowerOnHours: 2000,
            ReallocatedSectors: 0,
            PendingSectors: 0,
            UncorrectableErrors: 0,
            PredictFailure: false,
            SmartPassed: null,
            NvmeCriticalWarning: null,
            VendorHealthPercent: null,
            SmartCtlUsed: false,
            StorageReliabilityUsed: false,
            WmiUsed: true));

        Assert.Null(a.HealthPercent);
        Assert.Contains("Недостаточно SMART", a.HealthSummary, StringComparison.Ordinal);
    }
}

public class MelezhAriaApiHandlerCatalogTests
{
    [Fact]
    public void All_HandlerKeys_AreUnique()
    {
        var keys = MelezhAriaApiHandlerCatalog.All.Select(d => d.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalog_ContainsInboundAndOutboundHandlers()
    {
        Assert.Contains(MelezhAriaApiHandlerCatalog.All, d => d.Key == "aria_sync");
        Assert.Contains(MelezhAriaApiHandlerCatalog.All, d => d.Key == "aria_get_disks");
        Assert.Contains(MelezhAriaApiHandlerCatalog.All, d => d.Key == "aria_put_settings");
    }

    [Fact]
    public void ScheduledHandlers_AreOnlyStaticGet()
    {
        var scheduled = MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault).ToList();
        Assert.Equal(10, scheduled.Count);
        Assert.All(scheduled, d =>
        {
            Assert.Equal(MelezhHandlerDirection.OutboundToAria, d.Direction);
            Assert.Equal("GET", d.AriaHttpMethod);
            Assert.DoesNotContain("{", d.ApiPathTemplate ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Catalog_DoesNotMapMelezhPushAsOutbound()
    {
        Assert.DoesNotContain(MelezhAriaApiHandlerCatalog.All, d =>
            d.ApiPathTemplate?.Contains("melezh/push", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Catalog_UsesOintHttpTokens()
    {
        var ping = MelezhAriaApiHandlerCatalog.All.Single(d => d.Key == "aria_ping");
        Assert.Equal(MelezhOintHttp.Library, ping.OintLibrary);
        Assert.Equal(MelezhOintHttp.FuncGet, ping.OintFunction);
        Assert.Equal(MelezhOintHttp.MethodGet, ping.OintMethod);

        var sync = MelezhAriaApiHandlerCatalog.All.Single(d => d.Key == "aria_sync");
        Assert.Equal(MelezhOintHttp.Library, sync.OintLibrary);
        Assert.Equal(MelezhOintHttp.FuncPostWithBody, sync.OintFunction);
        Assert.Equal(MelezhOintHttp.MethodJson, sync.OintMethod);

        var deleteSmart = MelezhAriaApiHandlerCatalog.All.Single(d => d.Key == "aria_delete_disk_smart");
        Assert.Equal(MelezhOintHttp.FuncDeleteWithBody, deleteSmart.OintFunction);

        var putSettings = MelezhAriaApiHandlerCatalog.All.Single(d => d.Key == "aria_put_settings");
        Assert.Equal(MelezhOintHttp.FuncPutWithBody, putSettings.OintFunction);

        var postRefresh = MelezhAriaApiHandlerCatalog.All.Single(d => d.Key == "aria_post_disks_refresh");
        Assert.Equal(MelezhOintHttp.FuncPostWithBody, postRefresh.OintFunction);
    }
}

public class MelezhPullCronScheduleTests
{
    [Fact]
    public void BuildStaggered_UsesDistinctSeconds_ForTenHandlers()
    {
        var scheduled = MelezhAriaApiHandlerCatalog.All.Where(d => d.ScheduleByDefault).ToList();
        Assert.Equal(10, scheduled.Count);

        var crons = scheduled.Select((_, i) => MelezhPullCronSchedule.BuildStaggered(i)).ToList();
        var seconds = crons.Select(c => int.Parse(c.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(10, seconds.Distinct().Count());
        Assert.All(crons, c => Assert.Contains("*/5", c, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildForScheduledHandlers_MapsEveryScheduledKey()
    {
        var map = MelezhPullCronSchedule.BuildForScheduledHandlers();
        var scheduled = MelezhAriaApiHandlerCatalog.All
            .Where(d => d.ScheduleByDefault)
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(scheduled, map.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(10, map.Count);
    }
}

public class MelezhBootstrapSchemaTests
{
    [Fact]
    public void CurrentVersion_IsSix()
    {
        Assert.Equal(6, MelezhBootstrapSchema.CurrentVersion);
    }
}

public class MelezhProjectBootstrapTests
{
    private static string CreateTestProject()
    {
        var path = Path.Combine(Path.GetTempPath(), "aria-mz-bootstrap-" + Guid.NewGuid().ToString("N") + ".melezh");
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE handlers (key TEXT PRIMARY KEY, library TEXT, function TEXT, method TEXT);
            CREATE TABLE arguments (key TEXT, arg TEXT, value TEXT);
            CREATE TABLE scheduler_tasks (handler TEXT, cron TEXT);
            CREATE TABLE settings (name TEXT PRIMARY KEY, value TEXT);
            """;
        cmd.ExecuteNonQuery();
        return path;
    }

    private static void InsertHandler(string projectPath, string key, string library, string function, string method)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={projectPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO handlers(key, library, function, method) VALUES ($key, $lib, $func, $method)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$lib", library);
        cmd.Parameters.AddWithValue("$func", function);
        cmd.Parameters.AddWithValue("$method", method);
        cmd.ExecuteNonQuery();
    }

    private static void InsertScheduler(string projectPath, string handler, string cron)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={projectPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO scheduler_tasks(handler, cron) VALUES ($handler, $cron)";
        cmd.Parameters.AddWithValue("$handler", handler);
        cmd.Parameters.AddWithValue("$cron", cron);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task RenameHandlerKeyAsync_UpdatesHandlersArgumentsAndScheduler()
    {
        var path = CreateTestProject();
        try
        {
            InsertHandler(path, "guid-old", "http", "Get", "GET");
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO arguments(key, arg, value) VALUES ('guid-old', 'url', 'http://test')";
                cmd.ExecuteNonQuery();
            }

            InsertScheduler(path, "guid-old", "0 */5 * * * * *");

            await MelezhProjectBootstrap.RenameHandlerKeyAsync(path, "guid-old", "aria_get_status", CancellationToken.None);

            using var verify = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            verify.Open();
            using (var cmd = verify.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM handlers WHERE key = 'aria_get_status'";
                Assert.Equal(1L, cmd.ExecuteScalar());
            }

            using (var cmd = verify.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM arguments WHERE key = 'aria_get_status'";
                Assert.Equal(1L, cmd.ExecuteScalar());
            }

            using (var cmd = verify.CreateCommand())
            {
                cmd.CommandText = "SELECT handler FROM scheduler_tasks LIMIT 1";
                Assert.Equal("aria_get_status", cmd.ExecuteScalar() as string);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                /* temp file may remain locked briefly on Windows */
            }
        }
    }

    [Fact]
    public async Task PruneOrphanHandlersAsync_RemovesNonCatalogKeys()
    {
        var path = CreateTestProject();
        try
        {
            InsertHandler(path, "aria_sync", "http", "PostСТелом", "JSON");
            InsertHandler(path, Guid.NewGuid().ToString(), "http", "Get", "GET");

            var logger = NullLogger.Instance;
            var pruned = await MelezhProjectBootstrap.PruneOrphanHandlersAsync(path, logger, CancellationToken.None);

            Assert.Equal(1, pruned);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key FROM handlers";
            var keys = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                keys.Add(reader.GetString(0));
            }

            Assert.Single(keys);
            Assert.Equal("aria_sync", keys[0]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                /* temp file may remain locked briefly on Windows */
            }
        }
    }
}

public class MelezhSyncDispatcherTests
{
    [Fact]
    public void BuildTargetUrl_UsesLocalhostPortAndHandler()
    {
        var url = $"http://127.0.0.1:{7788}/{"aria_sync".TrimStart('/')}";
        Assert.Equal("http://127.0.0.1:7788/aria_sync", url);
    }

    [Theory]
    [InlineData("Melezh POST 500: InvalidOperationException", true)]
    [InlineData("non-concurrent collections", true)]
    [InlineData("Melezh POST 404: not found", false)]
    public void IsTransientMelezhRace_DetectsOintHttpRace(string error, bool expected)
    {
        Assert.Equal(expected, MelezhSyncDispatcher.IsTransientMelezhRace(error));
    }
}