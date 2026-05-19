using System.Reflection;
using System.Text.Json;
using AriaSignature.Application.Services;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Monitoring;
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

public class DiskMediaInferenceTests
{
    [Theory]
    [InlineData("NVMe", "Samsung PM9A1", "SSD")]
    [InlineData("SATA", "ST1000DM010", "HDD")]
    [InlineData("Unknown", "Generic USB enclosure", "Не определён")]
    public void InferMediaType_UsesStrongerHeuristics(string iface, string model, string expected)
    {
        var method = typeof(WmiDiskTelemetryCollector)
            .GetMethod("InferMediaType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = (string?)method!.Invoke(null, [iface, model]);
        Assert.Equal(expected, actual);
    }
}