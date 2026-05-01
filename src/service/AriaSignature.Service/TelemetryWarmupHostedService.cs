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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            using var warmupTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, warmupTimeoutCts.Token);
            try
            {
                await _diskTelemetryService.RefreshAsync(linkedCts.Token);
                _logger.LogInformation("Initial disk telemetry warmup completed");
            }
            catch (OperationCanceledException) when (warmupTimeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("Initial disk telemetry warmup timed out after 30 seconds; API startup is not blocked");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial disk telemetry warmup failed");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
