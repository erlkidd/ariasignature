namespace AriaSignature.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Core service heartbeat at {TimeUtc}", DateTimeOffset.UtcNow);
            await Task.Delay(_heartbeatInterval, stoppingToken);
        }
    }
}
