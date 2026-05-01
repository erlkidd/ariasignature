using AriaSignature.Domain.Enums;

namespace AriaSignature.Api.Contracts;

public sealed class UpsertBackupJobRequest
{
    public string Name { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.File;

    /// <summary>Для MsSql: при заполнении собирается строка подключения и подставляется в <see cref="Source"/>.</summary>
    public MsSqlConnectionPayload? MsSql { get; set; }

    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string ScheduleCron { get; set; } = "0 0 * * * ?";
    public int RetentionCount { get; set; } = 7;
    public bool IsEnabled { get; set; } = true;
}
