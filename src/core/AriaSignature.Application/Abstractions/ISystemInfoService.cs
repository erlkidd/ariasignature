using AriaSignature.Application.SystemInfo;

namespace AriaSignature.Application.Abstractions;

public interface ISystemInfoService
{
    Task<SystemInfoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
