namespace AriaSignature.Application.Telemetry;

/// <summary>Shared disk telemetry normalization for collector and tests (HDS-style: no synthetic metrics).</summary>
public static class DiskTelemetryRules
{
    public const int MinHealthPublishConfidence = 50;

    public static string NormalizeMediaType(
        string? sourceMediaType,
        int? physicalIndex,
        string iface,
        string model,
        IReadOnlyDictionary<int, string>? storageMediaByIndex)
    {
        if (physicalIndex is int idx &&
            storageMediaByIndex is not null &&
            storageMediaByIndex.TryGetValue(idx, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return InferMediaType(sourceMediaType, iface, model);
    }

    public static string InferMediaType(string? sourceMediaType, string iface, string model)
    {
        var m = (model ?? string.Empty).ToUpperInvariant();
        var i = (iface ?? string.Empty).ToUpperInvariant();
        var raw = (sourceMediaType ?? string.Empty).ToUpperInvariant();

        if (raw.Contains("SSD", StringComparison.Ordinal) ||
            raw.Contains("SOLID", StringComparison.Ordinal) ||
            raw.Contains("NVME", StringComparison.Ordinal))
        {
            return raw.Contains("NVME", StringComparison.Ordinal) ? "NVMe" : "SSD";
        }

        if (raw.Contains("HDD", StringComparison.Ordinal) ||
            raw.Contains("HARD DISK", StringComparison.Ordinal) ||
            raw.Contains("ROTATIONAL", StringComparison.Ordinal) ||
            raw.Contains("FIXED HARD DISK", StringComparison.Ordinal))
        {
            return "HDD";
        }

        if (m.Contains("NVME", StringComparison.Ordinal) || i.Contains("NVME", StringComparison.Ordinal))
        {
            return "NVMe";
        }

        if (m.Contains("SSD", StringComparison.Ordinal))
        {
            return "SSD";
        }

        if (m.Contains("HDD", StringComparison.Ordinal))
        {
            return "HDD";
        }

        if (i.Contains("SATA", StringComparison.Ordinal) && !i.Contains("NVME", StringComparison.Ordinal))
        {
            return "HDD";
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Не определён";
        }

        return "Не определён";
    }

    /// <summary>Publish remaining SSD life; 0% only when <paramref name="explicitEndOfLife"/>.</summary>
    public static int? NormalizeSsdLifeRemainingPercent(int? ssdLife, bool explicitEndOfLife)
    {
        if (ssdLife is not int life)
        {
            return null;
        }

        if (life <= 0 && !explicitEndOfLife)
        {
            return null;
        }

        return Math.Clamp(life, 0, 100);
    }

    /// <summary>Prefer the highest trusted remaining %; ignore stray zeros unless EOL.</summary>
    public static int? MergeSsdLifeRemainingPercent(int? current, int? incoming, bool incomingEndOfLife)
    {
        if (incomingEndOfLife && incoming is 0)
        {
            return 0;
        }

        if (incoming is int life && life > 0)
        {
            return current is int cur && cur > 0 ? Math.Max(cur, life) : life;
        }

        return current;
    }

    public static bool HasSectorSmartSignal(int reallocated, int pending, int uncorrectable, bool predictFailure) =>
        predictFailure || reallocated > 0 || pending > 0 || uncorrectable > 0;

    public static bool ShouldPublishHealthPercent(
        int telemetryConfidence,
        bool hasSectorSignal,
        bool predictFailure) =>
        predictFailure ||
        (hasSectorSignal && telemetryConfidence >= MinHealthPublishConfidence);

    public static bool HasMeaningfulTelemetrySignal(
        int? temperatureCelsius,
        long powerOnHours,
        int reallocated,
        int pending,
        int uncorrectable,
        int? ssdLife,
        bool wearConfirmed)
    {
        if (wearConfirmed && ssdLife is int life && life > 0)
        {
            return true;
        }

        if (temperatureCelsius is > 0 || powerOnHours > 0)
        {
            return true;
        }

        return reallocated > 0 || pending > 0 || uncorrectable > 0;
    }

    public static int EstimateHealthFromSectorCounters(int reallocated, int pending, int uncorrectable, int? ssdLife)
    {
        var penalty = Math.Min(reallocated, 20) + (pending * 4) + (uncorrectable * 10);
        var baseHealth = Math.Clamp(100 - penalty, 0, 100);
        if (ssdLife is int life)
        {
            baseHealth = Math.Min(baseHealth, life);
        }

        return baseHealth;
    }
}
