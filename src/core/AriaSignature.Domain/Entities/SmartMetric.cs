using AriaSignature.Domain.Enums;

namespace AriaSignature.Domain.Entities;

public sealed class SmartMetric
{
    public Guid DiskId { get; set; }
    public int TemperatureCelsius { get; set; }

    /// <summary>Снимок оценки здоровья; null если в момент записи оценка не вычислялась.</summary>
    public int? HealthPercent { get; set; }
    public int ReallocatedSectors { get; set; }
    public int PendingSectors { get; set; }
    public int UncorrectableErrors { get; set; }
    public DiskHealthStatus Status { get; set; } = DiskHealthStatus.Ok;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
