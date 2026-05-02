namespace AriaSignature.Api.Contracts;

public sealed class UpdateAppSettingsRequest
{
    public int? ApiPort { get; set; }
    public string? SmartMonitoringCron { get; set; }

    public bool? OutboundSyncEnabled { get; set; }
    public string? OutboundSyncUrl { get; set; }
    public string? OutboundSyncCron { get; set; }
}
