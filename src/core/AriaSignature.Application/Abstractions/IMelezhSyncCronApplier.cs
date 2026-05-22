namespace AriaSignature.Application.Abstractions;

public interface IMelezhSyncCronApplier
{
    Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken);
}
