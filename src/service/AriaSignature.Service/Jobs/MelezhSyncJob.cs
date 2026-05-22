using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service.Jobs;

public sealed class MelezhSyncJob : IJob
{
    private readonly IMelezhSyncDispatcher _dispatcher;
    private readonly ILogger<MelezhSyncJob> _logger;

    public MelezhSyncJob(IMelezhSyncDispatcher dispatcher, ILogger<MelezhSyncJob> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var result = await _dispatcher.PushSnapshotAsync(context.CancellationToken);
        if (!result.Ok)
        {
            _logger.LogDebug("Melezh sync skipped or failed: {Error}", result.Error ?? "unknown");
        }
    }
}
