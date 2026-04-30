using System.IO;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.UI.Services;

public sealed class LocalDiskInfoProvider
{
    public IReadOnlyCollection<Disk> GetLocalDisks()
    {
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .ToArray();

        return drives.Select(drive => new Disk
        {
            Id = CreateStableId(drive.Name),
            Model = drive.VolumeLabel,
            Serial = drive.Name,
            Interface = "Local",
            SizeTotalBytes = drive.TotalSize,
            SizeFreeBytes = drive.TotalFreeSpace,
            TemperatureCelsius = 0,
            HealthPercent = 100,
            Status = DiskHealthStatus.Ok,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }).ToArray();
    }

    private static Guid CreateStableId(string value)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }
}
