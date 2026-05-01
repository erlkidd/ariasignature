namespace AriaSignature.Api.Contracts;

/// <summary>Параметры подключения к Microsoft SQL Server для UI (без ручной сборки строки).</summary>
public sealed class MsSqlConnectionPayload
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;

    /// <summary>Режим: <c>sql</c> (логин/пароль) или <c>windows</c> (учётная запись службы Windows).</summary>
    public string Auth { get; set; } = "sql";

    public string? User { get; set; }
    public string? Password { get; set; }
    public bool TrustServerCertificate { get; set; } = true;
}
