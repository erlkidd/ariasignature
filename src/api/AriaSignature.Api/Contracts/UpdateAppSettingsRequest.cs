namespace AriaSignature.Api.Contracts;

public sealed class UpdateAppSettingsRequest
{
    public int? ApiPort { get; set; }
    public string? SmartMonitoringCron { get; set; }

    public bool? OutboundSyncEnabled { get; set; }
    public string? OutboundSyncUrl { get; set; }
    public string? OutboundSyncCron { get; set; }
    /// <summary>Пустая строка — удалить токен; "***" — не менять (маска из GET).</summary>
    public string? OutboundBearerToken { get; set; }
    public string? OutboundCustomHeaderName { get; set; }
    /// <summary>Пустая строка — удалить значение; "***" — не менять.</summary>
    public string? OutboundCustomHeaderValue { get; set; }
}
