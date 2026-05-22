namespace AriaSignature.Api.Contracts;

public sealed class UpdateAppSettingsRequest
{
    public int? ApiPort { get; set; }
    public string? ApiBind { get; set; }
    public string? ApiSharedSecret { get; set; }
    public string? SmartMonitoringCron { get; set; }

    public bool? OutboundSyncEnabled { get; set; }
    public string? OutboundSyncUrl { get; set; }
    public string? OutboundSyncCron { get; set; }

    public bool? MelezhSyncEnabled { get; set; }
    public string? MelezhSyncHandler { get; set; }
    public string? MelezhSyncCron { get; set; }
}
