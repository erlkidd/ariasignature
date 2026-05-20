namespace AriaSignature.Application.Abstractions;

public sealed record MelezhStatusSnapshot(
    bool Enabled,
    int Port,
    string UiUrl,
    string ServiceName,
    string? ServiceStatus,
    bool UiReachable,
    string? LastError = null,
    string? LogHint = null);

public interface IMelezhStatusService
{
    Task<MelezhStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
