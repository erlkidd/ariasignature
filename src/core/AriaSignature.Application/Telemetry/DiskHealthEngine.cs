using AriaSignature.Domain.Enums;

namespace AriaSignature.Application.Telemetry;

/// <summary>Composite SMART-based health score (~85% alignment with dedicated tools; not vendor-specific).</summary>
public static class DiskHealthEngine
{
    public const int MinPublishConfidence = 40;

    public sealed record Assessment(
        int? HealthPercent,
        DiskHealthStatus Status,
        string HealthSummary,
        int TelemetryConfidence);

    public static Assessment Compute(DiskHealthInput input)
    {
        var wearConfirmed = input.SsdLifeEndOfLife || input.SsdLifeRemainingPercent is > 0;
        var hasSectorSignal = DiskTelemetryRules.HasSectorSmartSignal(
            input.ReallocatedSectors,
            input.PendingSectors,
            input.UncorrectableErrors,
            input.PredictFailure);

        var hadTelemetry = DiskTelemetryRules.HasMeaningfulTelemetrySignal(
            input.TemperatureCelsius,
            input.PowerOnHours,
            input.ReallocatedSectors,
            input.PendingSectors,
            input.UncorrectableErrors,
            input.SsdLifeRemainingPercent,
            wearConfirmed) || input.PredictFailure;

        var confidence = ComputeConfidence(input, hadTelemetry);

        if (!CanPublishHealth(input, wearConfirmed, hasSectorSignal, hadTelemetry))
        {
            var reason = BuildPartialDataSummary(input, confidence);
            return new Assessment(null, DiskHealthStatus.Ok, reason, confidence);
        }

        var factors = new List<string>();
        var score = 100;

        if (input.PredictFailure || input.SmartPassed == false)
        {
            score = Math.Min(score, 30);
            factors.Add(input.PredictFailure ? "WMI PredictFailure" : "SMART: не PASSED");
        }

        if (input.SsdLifeEndOfLife)
        {
            score = 0;
            factors.Add("ресурс SSD исчерпан (EOL)");
        }
        else
        {
            var sectorScore = DiskTelemetryRules.EstimateHealthFromSectorCounters(
                input.ReallocatedSectors,
                input.PendingSectors,
                input.UncorrectableErrors,
                input.SsdLifeRemainingPercent);
            score = Math.Min(score, sectorScore);

            if (input.ReallocatedSectors > 0)
            {
                factors.Add($"Reallocated: {input.ReallocatedSectors}");
            }

            if (input.PendingSectors > 0)
            {
                factors.Add($"Pending: {input.PendingSectors}");
            }

            if (input.UncorrectableErrors > 0)
            {
                factors.Add($"Uncorrectable: {input.UncorrectableErrors}");
            }

            if (input.SsdLifeRemainingPercent is int life)
            {
                score = Math.Min(score, life);
                factors.Add($"остаток ресурса SSD {life}%");
            }
        }

        if (input.VendorHealthPercent is int vendor)
        {
            score = Math.Min(score, vendor);
            factors.Add($"SMART health (vendor) {vendor}%");
        }

        if (input.NvmeCriticalWarning is int cw && cw != 0)
        {
            score = Math.Min(score, cw switch
            {
                _ when (cw & 0x10) != 0 => 25,
                _ when (cw & 0x04) != 0 => 35,
                _ => 45
            });
            factors.Add("NVMe critical warning");
        }

        if (input.TemperatureCelsius is int t)
        {
            if (t > 70)
            {
                score = Math.Max(0, score - 15);
                factors.Add($"температура {t} °C");
            }
            else if (t > 55)
            {
                score = Math.Max(0, score - 5);
                factors.Add($"температура {t} °C");
            }
        }

        score = Math.Clamp(score, 0, 100);
        var status = MapStatus(score, input.SsdLifeEndOfLife, input.PredictFailure, input.SmartPassed == false);
        var sources = BuildSourceTag(input);
        var summary = factors.Count > 0
            ? string.Join(". ", factors) + ". " + sources
            : sources;

        if (confidence < DiskTelemetryRules.MinHealthPublishConfidence)
        {
            summary += " Оценка по неполным данным.";
        }

        return new Assessment(score, status, summary.Trim(), confidence);
    }

    private static bool CanPublishHealth(
        DiskHealthInput input,
        bool wearConfirmed,
        bool hasSectorSignal,
        bool hadTelemetry)
    {
        if (input.PredictFailure || input.SsdLifeEndOfLife || input.SmartPassed == false)
        {
            return true;
        }

        if (wearConfirmed || hasSectorSignal)
        {
            return true;
        }

        if (input.VendorHealthPercent is >= 0 and <= 100)
        {
            return true;
        }

        if (input.NvmeCriticalWarning is > 0)
        {
            return true;
        }

        if (input.SmartCtlUsed && hadTelemetry)
        {
            return true;
        }

        if (input.StorageReliabilityUsed && wearConfirmed)
        {
            return true;
        }

        return false;
    }

    private static int ComputeConfidence(DiskHealthInput input, bool hadTelemetry)
    {
        if (!hadTelemetry)
        {
            return 5;
        }

        var score = 20;
        if (input.SmartCtlUsed)
        {
            score += 55;
        }

        if (input.StorageReliabilityUsed)
        {
            score += 20;
        }

        if (input.WmiUsed)
        {
            score += 10;
        }

        if (input.SsdLifeEndOfLife || input.SsdLifeRemainingPercent is > 0)
        {
            score += 5;
        }

        return Math.Clamp(score, MinPublishConfidence, 95);
    }

    private static DiskHealthStatus MapStatus(int score, bool eol, bool predictFailure, bool smartFailed)
    {
        if (eol || predictFailure || smartFailed || score < 50)
        {
            return DiskHealthStatus.Critical;
        }

        if (score < 80)
        {
            return DiskHealthStatus.Warning;
        }

        return DiskHealthStatus.Ok;
    }

    private static string BuildSourceTag(DiskHealthInput input)
    {
        var parts = new List<string>();
        if (input.SmartCtlUsed)
        {
            parts.Add("smartctl");
        }

        if (input.StorageReliabilityUsed)
        {
            parts.Add("StorageReliability");
        }

        if (input.WmiUsed)
        {
            parts.Add("WMI");
        }

        return parts.Count > 0
            ? "Источник: " + string.Join("+", parts) + "."
            : "Источник не определён.";
    }

    private static string BuildPartialDataSummary(DiskHealthInput input, int confidence)
    {
        var tag = BuildSourceTag(input);
        if (input.TemperatureCelsius is > 0 || input.PowerOnHours > 0)
        {
            return $"Недостаточно SMART для оценки здоровья ({confidence}%). {tag}";
        }

        return $"Телеметрия отсутствует. {tag}";
    }
}
