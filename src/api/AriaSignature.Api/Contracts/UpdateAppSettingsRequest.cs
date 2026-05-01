namespace AriaSignature.Api.Contracts;

public sealed class UpdateAppSettingsRequest
{
    public int? ApiPort { get; set; }
    public string? SmartMonitoringCron { get; set; }
}
