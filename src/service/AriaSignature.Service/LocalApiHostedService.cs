using AriaSignature.Api;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;

namespace AriaSignature.Service;

public sealed class LocalApiHostedService : IHostedService
{
    private readonly ILogger<LocalApiHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private WebApplication? _webApp;

    public LocalApiHostedService(
        ILogger<LocalApiHostedService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var startupSw = Stopwatch.StartNew();
        var port = await ResolveApiPortAsync(cancellationToken);
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var webRootExists = Directory.Exists(webRoot);

        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootExists ? webRoot : null
        };

        // CreateSlimBuilder не регистрирует regex route constraints; Swashbuckle (UseSwagger) падает при старте.
        var webBuilder = WebApplication.CreateBuilder(options);
        webBuilder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        webBuilder.Services.AddApplication();
        webBuilder.Services.AddInfrastructure();
        webBuilder.Services.AddAriaApi();
        var cronApplier = _serviceProvider.GetRequiredService<ISmartRefreshCronApplier>();
        webBuilder.Services.AddSingleton<ISmartRefreshCronApplier>(cronApplier);
        var outboundCronApplier = _serviceProvider.GetRequiredService<IOutboundSyncCronApplier>();
        webBuilder.Services.AddSingleton<IOutboundSyncCronApplier>(outboundCronApplier);

        _webApp = webBuilder.Build();
        _webApp.UseAriaApi();

        _logger.LogInformation("Starting local API on http://127.0.0.1:{Port} (wwwroot: {WebRoot})", port, webRootExists ? webRoot : "(none)");
        await _webApp.StartAsync(cancellationToken);
        _logger.LogInformation("API bind/start completed in {ElapsedMs} ms", startupSw.ElapsedMilliseconds);
        await LogFirstReadyAsync(port, cancellationToken);
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

    private async Task<int> ResolveApiPortAsync(CancellationToken cancellationToken)
    {
        var fallback = _configuration.GetValue<int?>("Api:Port") ?? 5160;
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await using var scope = _serviceProvider.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
            var fromDb = await settings.GetAsync("Api:Port", linkedCts.Token);
            if (int.TryParse(fromDb, out var p) && p is > 0 and < 65536)
            {
                return p;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read API port from DB; fallback port {FallbackPort} will be used", fallback);
        }

        return fallback;
    }

    private async Task LogFirstReadyAsync(int port, CancellationToken cancellationToken)
    {
        var readySw = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var baseUrl = $"http://127.0.0.1:{port}";
        try
        {
            using var status = await http.GetAsync($"{baseUrl}/api/v1/status", cancellationToken);
            using var root = await http.GetAsync($"{baseUrl}/", cancellationToken);
            _logger.LogInformation(
                "API first-ready probe in {ElapsedMs} ms: status={StatusCode}, root={RootCode}",
                readySw.ElapsedMilliseconds,
                (int)status.StatusCode,
                (int)root.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API first-ready probe failed after {ElapsedMs} ms", readySw.ElapsedMilliseconds);
        }
    }
}
