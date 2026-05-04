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
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

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
        var bindMode = await ResolveBindModeAsync(cancellationToken);
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var webRootExists = Directory.Exists(webRoot);

        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootExists ? webRoot : null
        };

        // CreateSlimBuilder не регистрирует regex route constraints; Swashbuckle (UseSwagger) падает при старте.
        var webBuilder = WebApplication.CreateBuilder(options);
        var listenUrls = ResolveListenUrls(port, bindMode);
        webBuilder.WebHost.UseUrls(listenUrls);
        webBuilder.Services.AddApplication();
        webBuilder.Services.AddInfrastructure();
        webBuilder.Services.AddAriaApi();
        var cronApplier = _serviceProvider.GetRequiredService<ISmartRefreshCronApplier>();
        webBuilder.Services.AddSingleton<ISmartRefreshCronApplier>(cronApplier);
        var outboundCronApplier = _serviceProvider.GetRequiredService<IOutboundSyncCronApplier>();
        webBuilder.Services.AddSingleton<IOutboundSyncCronApplier>(outboundCronApplier);

        _webApp = webBuilder.Build();
        _webApp.UseAriaApi();

        _logger.LogInformation(
            "Starting API. bind={BindMode}; urls={ListenUrls}; contentRoot={ContentRoot}; webRoot={WebRoot}; localPanel=http://127.0.0.1:{Port}",
            bindMode,
            listenUrls,
            AppContext.BaseDirectory,
            webRootExists ? webRoot : "(none)",
            port);
        try
        {
            await _webApp.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (IsListenAddressFailure(ex))
        {
            _logger.LogError(
                ex,
                "API start failed. bind={BindMode}; urls={ListenUrls}; port={Port}. Check for IPv6 restrictions or port conflicts.",
                bindMode,
                listenUrls,
                port);
            throw;
        }
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
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
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

    private async Task<string> ResolveBindModeAsync(CancellationToken cancellationToken)
    {
        var fallbackBind = _configuration.GetValue<string>("Api:Bind") ?? "all";
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await using var scope = _serviceProvider.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
            var fromDb = await settings.GetAsync(AppSettingsApiKeys.Bind, linkedCts.Token);
            var mode = string.IsNullOrWhiteSpace(fromDb) ? fallbackBind : fromDb.Trim();
            return string.Equals(mode, "loopback", StringComparison.OrdinalIgnoreCase) ? "loopback" : "all";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Api:Bind from DB; using configuration fallback {FallbackBind}", fallbackBind);
        }

        return string.Equals(fallbackBind.Trim(), "loopback", StringComparison.OrdinalIgnoreCase) ? "loopback" : "all";
    }

    private static string ResolveListenUrls(int port, string bindMode)
    {
        if (string.Equals(bindMode, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return $"http://127.0.0.1:{port}";
        }

        // Prefer IPv4 wildcard only: on hardened Win11 hosts, explicit [::] may fail startup.
        return $"http://0.0.0.0:{port}";
    }

    private static bool IsListenAddressFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException)
            {
                return true;
            }

            if (current is IOException ioEx && ioEx.InnerException is SocketException)
            {
                return true;
            }

            if (current is HttpRequestException && current.InnerException is SocketException)
            {
                return true;
            }

            if (current is Win32Exception win32 && win32.NativeErrorCode is 10013 or 10048)
            {
                return true;
            }
        }

        return false;
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
