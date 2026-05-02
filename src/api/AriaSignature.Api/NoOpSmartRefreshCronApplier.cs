using AriaSignature.Application.Abstractions;

namespace AriaSignature.Api;

/// <summary>В автономном API-сборке Quartz недоступен — сохранение cron только в базе.</summary>
internal sealed class NoOpSmartRefreshCronApplier : ISmartRefreshCronApplier
{
    public Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken) => Task.CompletedTask;
}
