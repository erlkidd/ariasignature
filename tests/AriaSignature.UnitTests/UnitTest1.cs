using System.Reflection;
using System.Text.Json;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace AriaSignature.UnitTests;

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