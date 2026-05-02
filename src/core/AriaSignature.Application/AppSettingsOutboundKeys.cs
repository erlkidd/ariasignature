namespace AriaSignature.Application;

public static class AppSettingsOutboundKeys
{
    public const string Enabled = "OutboundSync:Enabled";
    public const string Url = "OutboundSync:Url";
    public const string Cron = "OutboundSync:Cron";
    public const string BearerToken = "OutboundSync:BearerToken";
    public const string CustomHeaderName = "OutboundSync:CustomHeaderName";
    public const string CustomHeaderValue = "OutboundSync:CustomHeaderValue";

    /// <summary>Клиент и API используют это значение для «секрет задан, не менять без ввода нового».</summary>
    public const string SecretMaskedSentinel = "***";
}
