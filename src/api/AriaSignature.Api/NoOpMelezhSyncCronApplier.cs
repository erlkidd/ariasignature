using AriaSignature.Application.Abstractions;

namespace AriaSignature.Api;

internal sealed class NoOpMelezhSyncCronApplier : IMelezhSyncCronApplier
{
    public Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken) => Task.CompletedTask;
}
