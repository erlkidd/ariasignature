using AriaSignature.Api;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure;
using AriaSignature.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISqliteDatabaseInitializer>().InitializeAsync(cancellationToken);
        }

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

        _webApp = webBuilder.Build();
        _webApp.UseAriaApi();

        _logger.LogInformation("Starting local API on http://127.0.0.1:{Port} (wwwroot: {WebRoot})", port, webRootExists ? webRoot : "(none)");
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

    private async Task<int> ResolveApiPortAsync(CancellationToken cancellationToken)
    {
        var fallback = _configuration.GetValue<int?>("Api:Port") ?? 5160;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var fromDb = await settings.GetAsync("Api:Port", cancellationToken);
        if (int.TryParse(fromDb, out var p) && p is > 0 and < 65536)
        {
            return p;
        }

        return fallback;
    }
}
