namespace AriaSignature.Application.SystemInfo;

public sealed class SystemInfoSnapshot
{
    public DateTimeOffset CollectedAtUtc { get; init; }
    public string AgentVersion { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string? DnsHostName { get; init; }
    public IReadOnlyList<NetworkAddressInfoDto> NetworkAddresses { get; init; } = Array.Empty<NetworkAddressInfoDto>();
    public string? OsCaption { get; init; }
    public string? OsVersion { get; init; }
    public string? ProcessorName { get; init; }
    public int? LogicalProcessors { get; init; }
    public long? TotalRamBytes { get; init; }
    public long? AvailableRamBytes { get; init; }
    public IReadOnlyList<string> VideoControllers { get; init; } = Array.Empty<string>();
}

public sealed class NetworkAddressInfoDto
{
    public string? InterfaceDescription { get; init; }
    public string Address { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
}
