namespace AriaSignature.Application.Telemetry;

/// <summary>Shared disk telemetry normalization for collector and tests.</summary>
public static class DiskTelemetryRules
{
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

    public static int? NormalizeSsdLifeRemainingPercent(int? ssdLife, bool wearConfirmed)
    {
        if (ssdLife is not int life)
        {
            return null;
        }

        if (life <= 0 && !wearConfirmed)
        {
            return null;
        }

        return Math.Clamp(life, 0, 100);
    }

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
}
