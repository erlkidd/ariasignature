namespace AriaSignature.Application.Abstractions;

public interface IOutboundSyncCronApplier
{
    Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken);
}
