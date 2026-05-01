using AriaSignature.Application.Abstractions;
using Quartz;

namespace AriaSignature.Service.Jobs;

public sealed class SmartRefreshJob : IJob
{
    private readonly IDiskTelemetryService _telemetryService;
    private readonly ILogger<SmartRefreshJob> _logger;

    public SmartRefreshJob(IDiskTelemetryService telemetryService, ILogger<SmartRefreshJob> logger)
    {
        _telemetryService = telemetryService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Executing SMART refresh job");
        await _telemetryService.RefreshAsync(context.CancellationToken);
    }
}
