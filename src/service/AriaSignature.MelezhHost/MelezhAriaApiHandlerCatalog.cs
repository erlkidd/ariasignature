namespace AriaSignature.MelezhHost;

/// <summary>Declarative map: one stable Melezh handler key per AriaSignature API route (see docs/API.md).</summary>
public static class MelezhAriaApiHandlerCatalog
{
    public const string DefaultPullCron = "0 */5 * * * * *";

    public static IReadOnlyList<MelezhHandlerDefinition> All { get; } = BuildAll();

    public static int OutboundCount => All.Count(d => d.Direction == MelezhHandlerDirection.OutboundToAria);

    private static IReadOnlyList<MelezhHandlerDefinition> BuildAll()
    {
        var list = new List<MelezhHandlerDefinition>
        {
            // aria_ping proxies Aria health (OInt http module must be in CLI index)
            OutboundGet("aria_ping", "/status", schedule: false),
            Inbound("aria_sync", MelezhOintHttp.Library, MelezhOintHttp.FuncPostWithBody, MelezhOintHttp.MethodJson),
        };

        list.Add(OutboundGet("aria_get_status", "/status", schedule: true));
        list.Add(OutboundGet("aria_get_health_live", "/health/live", schedule: true));
        list.Add(OutboundGet("aria_get_health_ready", "/health/ready", schedule: true));
        list.Add(OutboundGet("aria_get_health_degradation", "/health/degradation", schedule: true));
        list.Add(OutboundGet("aria_get_observability_runtime", "/observability/runtime", schedule: true));
        list.Add(OutboundGet("aria_get_settings", "/settings", schedule: true));
        list.Add(OutboundGet("aria_get_system", "/system", schedule: true));
        list.Add(OutboundGet("aria_get_disks", "/disks", schedule: true));
        list.Add(OutboundGet("aria_get_backups", "/backups", schedule: true));
        list.Add(OutboundGet("aria_get_backups_logs", "/backups/logs", schedule: true));

        list.Add(OutboundGet("aria_get_disk", "/disks/{diskId}", schedule: false));
        list.Add(OutboundGet("aria_get_disk_smart", "/disks/{diskId}/smart", schedule: false));

        list.Add(OutboundWrite("aria_post_backups_test_mssql", "POST", "/backups/test-mssql", bodyJson: "{}"));
        list.Add(OutboundWrite("aria_post_disks_refresh", "POST", "/disks/refresh", bodyJson: "{}"));
        list.Add(OutboundWrite("aria_post_backups", "POST", "/backups", bodyJson: "{}"));
        list.Add(OutboundWrite("aria_post_backup_run", "POST", "/backups/{backupId}/run", bodyJson: "{}"));

        list.Add(OutboundWrite("aria_put_settings", "PUT", "/settings", bodyJson: "{}"));
        list.Add(OutboundWrite("aria_put_backup", "PUT", "/backups/{backupId}", bodyJson: "{}"));
        list.Add(OutboundWrite("aria_delete_disk_smart", "DELETE", "/disks/{diskId}/smart", bodyJson: null));
        list.Add(OutboundWrite("aria_delete_disks_smart", "DELETE", "/disks/smart", bodyJson: null));
        list.Add(OutboundWrite("aria_delete_backup", "DELETE", "/backups/{backupId}", bodyJson: null));
        list.Add(OutboundWrite("aria_delete_backups_logs", "DELETE", "/backups/logs", bodyJson: null));

        return list;
    }

    private static MelezhHandlerDefinition Inbound(string key, string library, string function, string method) =>
        new(key, MelezhHandlerDirection.Inbound, null, library, function, method, null, null, false);

    private static MelezhHandlerDefinition OutboundGet(string key, string apiPath, bool schedule) =>
        new(
            key,
            MelezhHandlerDirection.OutboundToAria,
            "GET",
            MelezhOintHttp.Library,
            MelezhOintHttp.FuncGet,
            MelezhOintHttp.MethodGet,
            apiPath,
            null,
            schedule);

    private static MelezhHandlerDefinition OutboundWrite(
        string key,
        string httpMethod,
        string apiPath,
        string? bodyJson) =>
        new(
            key,
            MelezhHandlerDirection.OutboundToAria,
            httpMethod,
            MelezhOintHttp.Library,
            MelezhOintHttp.FuncForAriaHttpMethod(httpMethod),
            MelezhOintHttp.MethodJson,
            apiPath,
            bodyJson,
            ScheduleByDefault: false);
}

public enum MelezhHandlerDirection
{
    Inbound,
    OutboundToAria,
}

public sealed record MelezhHandlerDefinition(
    string Key,
    MelezhHandlerDirection Direction,
    string? AriaHttpMethod,
    string OintLibrary,
    string OintFunction,
    string OintMethod,
    string? ApiPathTemplate,
    string? DefaultBodyJson,
    bool ScheduleByDefault);
