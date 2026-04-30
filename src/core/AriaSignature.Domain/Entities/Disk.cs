using AriaSignature.Domain.Enums;

namespace AriaSignature.Domain.Entities;

public sealed class Disk
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Model { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
    public long SizeTotalBytes { get; set; }
    public long SizeFreeBytes { get; set; }
    public int TemperatureCelsius { get; set; }
    public int HealthPercent { get; set; }
    public long PowerOnHours { get; set; }
    public long PowerCycleCount { get; set; }
    public int ReallocatedSectors { get; set; }
    public int PendingSectors { get; set; }
    public int UncorrectableErrors { get; set; }
    public DiskHealthStatus Status { get; set; } = DiskHealthStatus.Ok;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
