using AriaSignature.Api;
using AriaSignature.Application;
using AriaSignature.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Service;

public sealed class LocalApiHostedService : IHostedService
{
    private readonly ILogger<LocalApiHostedService> _logger;
    private readonly IConfiguration _configuration;
    private WebApplication? _webApp;

    public LocalApiHostedService(ILogger<LocalApiHostedService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = _configuration.GetValue<int?>("Api:Port") ?? 5160;

        var webBuilder = WebApplication.CreateSlimBuilder();
        webBuilder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        webBuilder.Services.AddApplication();
        webBuilder.Services.AddInfrastructure();
        webBuilder.Services.AddAriaApi();

        _webApp = webBuilder.Build();
        _webApp.UseAriaApi();

        _logger.LogInformation("Starting local API on http://127.0.0.1:{Port}", port);
        await _webApp.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApp is null)
        {
            return;
        }

        _logger.LogInformation("Stopping local API host");
        await _webApp.StopAsync(cancellationToken);
        await _webApp.DisposeAsync();
    }
}
