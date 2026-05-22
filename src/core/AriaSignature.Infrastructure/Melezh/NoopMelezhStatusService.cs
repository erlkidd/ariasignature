using AriaSignature.Application.Abstractions;

namespace AriaSignature.Infrastructure.Melezh;

internal sealed class NoopMelezhStatusService : IMelezhStatusService
{
    public Task<MelezhStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MelezhStatusSnapshot(
            false,
            7788,
            "http://127.0.0.1:7788/ui",
            "AriaSignatureMelezhService",
            null,
            false));
}
