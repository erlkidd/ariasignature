namespace AriaSignature.Application.Abstractions;

public interface IMelezhSyncDispatcher
{
    Task<MelezhSyncResult> PushSnapshotAsync(CancellationToken cancellationToken);
}

public sealed record MelezhSyncResult(bool Ok, string? Error, string TargetUrl);
