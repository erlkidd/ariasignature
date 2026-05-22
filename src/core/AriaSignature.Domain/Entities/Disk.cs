using AriaSignature.Domain.Enums;

namespace AriaSignature.Domain.Entities;

public sealed class Disk
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Model { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
    /// <summary>Носитель: HDD, SSD, SSD NVMe и т.п. (по данным WMI).</summary>
    public string MediaType { get; set; } = string.Empty;
    public long SizeTotalBytes { get; set; }
    public long SizeFreeBytes { get; set; }

    /// <summary>Занятый объём (вычисляется из общего и свободного).</summary>
    public long SizeUsedBytes => Math.Max(0, SizeTotalBytes - SizeFreeBytes);

    /// <summary>Оставшийся ресурс SSD/NVMe, % (null если неизвестно).</summary>
    public int? SsdLifeRemainingPercent { get; set; }

    public int? TemperatureCelsius { get; set; }

    /// <summary>Процент «здоровья» по SMART/износу; null если метрик недостаточно для оценки.</summary>
    public int? HealthPercent { get; set; }

    /// <summary>Краткое пояснение оценки здоровья (факторы SMART, источники).</summary>
    public string HealthSummary { get; set; } = string.Empty;
    public long PowerOnHours { get; set; }
    public long PowerCycleCount { get; set; }
    public int ReallocatedSectors { get; set; }
    public int PendingSectors { get; set; }
    public int UncorrectableErrors { get; set; }
    public bool SmartCtlUsed { get; set; }
    public bool WmiUsed { get; set; }
    public bool StorageReliabilityUsed { get; set; }
    public int TelemetryConfidence { get; set; }
    public string TelemetryDegradationReason { get; set; } = string.Empty;
    public DiskHealthStatus Status { get; set; } = DiskHealthStatus.Ok;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
