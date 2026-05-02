using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using AriaSignature.Application.Abstractions;
using AriaSignature.Application.SystemInfo;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Infrastructure.SystemInfo;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInfoService : ISystemInfoService
{
    private readonly ILogger<WindowsSystemInfoService> _logger;

    public WindowsSystemInfoService(ILogger<WindowsSystemInfoService> logger)
    {
        _logger = logger;
    }

    public Task<SystemInfoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(MinimalSnapshot());
        }

        var hostName = Dns.GetHostName();
        string? dnsHost = null;
        try
        {
            dnsHost = Dns.GetHostEntry(hostName).HostName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dns.GetHostEntry failed");
        }

        var addresses = CollectAddresses(cancellationToken);
        var (osCaption, osVersion, availKb, totalKb) = ReadOperatingSystemWmi();
        var (totalRam, logicalCpu) = ReadComputerSystemWmi();
        var processorName = ReadProcessorNameWmi();
        var gpus = ReadVideoControllersWmi();

        long? availBytes = availKb.HasValue ? (long)(availKb.Value * 1024UL) : null;
        long? totalOsBytes = totalKb.HasValue ? (long)(totalKb.Value * 1024UL) : null;
        long? ramFromSystem = totalRam.HasValue ? (long)totalRam.Value : null;
        var ramTotal = ramFromSystem ?? totalOsBytes;
        var ramAvail = availBytes;

        return Task.FromResult(new SystemInfoSnapshot
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            AgentVersion = ResolveAgentVersion(),
            HostName = hostName,
            DnsHostName = dnsHost,
            NetworkAddresses = addresses,
            OsCaption = osCaption,
            OsVersion = osVersion,
            ProcessorName = processorName,
            LogicalProcessors = logicalCpu,
            TotalRamBytes = ramTotal,
            AvailableRamBytes = ramAvail,
            VideoControllers = gpus,
        });
    }

    private SystemInfoSnapshot MinimalSnapshot()
    {
        return new SystemInfoSnapshot
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            AgentVersion = ResolveAgentVersion(),
            HostName = Environment.MachineName,
            DnsHostName = null,
            NetworkAddresses = Array.Empty<NetworkAddressInfoDto>(),
            OsCaption = Environment.OSVersion.ToString(),
        };
    }

    private static string ResolveAgentVersion()
    {
        var asm = typeof(ISystemInfoService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0";
    }

    private List<NetworkAddressInfoDto> CollectAddresses(CancellationToken cancellationToken)
    {
        var list = new List<NetworkAddressInfoDto>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    {
                        if (IPAddress.IsLoopback(ua.Address))
                        {
                            continue;
                        }

                        list.Add(new NetworkAddressInfoDto
                        {
                            InterfaceDescription = ni.Description,
                            Address = ua.Address.ToString(),
                            Family = ua.Address.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6",
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Сбор сетевых адресов завершился с ошибкой");
        }

        return list;
    }

    private (string? caption, string? version, ulong? freeKb, ulong? totalKb) ReadOperatingSystemWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            using var results = searcher.Get();
            foreach (var o in results.OfType<ManagementObject>())
            {
                using (o)
                {
                    var caption = o["Caption"]?.ToString()?.Trim();
                    var version = o["Version"]?.ToString()?.Trim();
                    ulong? free = ToULong(o["FreePhysicalMemory"]);
                    ulong? total = ToULong(o["TotalVisibleMemorySize"]);
                    return (caption, version, free, total);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI Win32_OperatingSystem недоступна");
        }

        return (null, null, null, null);
    }

    private (ulong? totalRam, int? logicalProcessors) ReadComputerSystemWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory, NumberOfLogicalProcessors FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (var o in results.OfType<ManagementObject>())
            {
                using (o)
                {
                    var total = ToULong(o["TotalPhysicalMemory"]);
                    var logical = ToInt32(o["NumberOfLogicalProcessors"]);
                    return (total, logical);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI Win32_ComputerSystem недоступна");
        }

        return (null, null);
    }

    private string? ReadProcessorNameWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            using var results = searcher.Get();
            foreach (var o in results.OfType<ManagementObject>())
            {
                using (o)
                {
                    var name = o["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI Win32_Processor недоступна");
        }

        return null;
    }

    private IReadOnlyList<string> ReadVideoControllersWmi()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (var o in results.OfType<ManagementObject>())
            {
                using (o)
                {
                    var name = o["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI Win32_VideoController недоступна");
        }

        return names;
    }

    private static ulong? ToULong(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static int? ToInt32(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
