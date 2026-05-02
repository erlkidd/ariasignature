using System.Reflection;
using AriaSignature.Application.Abstractions;
using AriaSignature.Application.SystemInfo;

namespace AriaSignature.Infrastructure.SystemInfo;

public sealed class NoopSystemInfoService : ISystemInfoService
{
    public Task<SystemInfoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var asm = typeof(ISystemInfoService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string ver;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            ver = plus > 0 ? info[..plus] : info;
        }
        else
        {
            ver = asm.GetName().Version?.ToString(3) ?? "0.0";
        }

        return Task.FromResult(new SystemInfoSnapshot
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            AgentVersion = ver,
            HostName = Environment.MachineName,
            OsCaption = Environment.OSVersion.ToString(),
            NetworkAddresses = Array.Empty<NetworkAddressInfoDto>(),
            VideoControllers = Array.Empty<string>(),
        });
    }
}
