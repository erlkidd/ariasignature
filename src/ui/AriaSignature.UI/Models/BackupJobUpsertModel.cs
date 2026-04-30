using AriaSignature.Domain.Enums;

namespace AriaSignature.UI.Models;

public sealed class BackupJobUpsertModel
{
    public string Name { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.File;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string ScheduleCron { get; set; } = "0 0 2 * * ?";
    public int RetentionCount { get; set; } = 7;
    public bool IsEnabled { get; set; } = true;
}
