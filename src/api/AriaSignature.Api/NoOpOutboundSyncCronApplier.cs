using AriaSignature.Application.Abstractions;

namespace AriaSignature.Api;

internal sealed class NoOpOutboundSyncCronApplier : IOutboundSyncCronApplier
{
    public Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken) => Task.CompletedTask;
}
