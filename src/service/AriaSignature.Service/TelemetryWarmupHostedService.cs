using AriaSignature.Application.Abstractions;

namespace AriaSignature.Service;

public sealed class TelemetryWarmupHostedService : IHostedService
{
    private readonly IDiskTelemetryService _diskTelemetryService;
    private readonly ILogger<TelemetryWarmupHostedService> _logger;

    public TelemetryWarmupHostedService(IDiskTelemetryService diskTelemetryService, ILogger<TelemetryWarmupHostedService> logger)
    {
        _diskTelemetryService = diskTelemetryService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diskTelemetryService.RefreshAsync(cancellationToken);
            _logger.LogInformation("Initial disk telemetry warmup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial disk telemetry warmup failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
