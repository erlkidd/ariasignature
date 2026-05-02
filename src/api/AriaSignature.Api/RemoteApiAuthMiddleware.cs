using System.Net;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace AriaSignature.Api;

public sealed class RemoteApiAuthMiddleware
{
    private readonly RequestDelegate _next;

    public RemoteApiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var apiToken = (await settings.GetAsync(AppSettingsApiKeys.SharedSecret, context.RequestAborted).ConfigureAwait(false))?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(apiToken))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (IsLocalRequest(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (TryValidateApiToken(apiToken, context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append("WWW-Authenticate", "Bearer");
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/401",
            title = "Требуется авторизация",
            status = 401,
            detail = "Укажите заголовок Authorization: Bearer <token> или X-Aria-Api-Key с тем же значением, что задано как токен удалённого API в настройках службы.",
        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remote))
        {
            return true;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            return IPAddress.IsLoopback(remote.MapToIPv4());
        }

        return false;
    }

    private static bool TryValidateApiToken(string expected, HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Aria-Api-Key", out var headerKey))
        {
            var key = headerKey.ToString().Trim();
            if (!string.IsNullOrEmpty(key) && string.Equals(key, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var auth = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = auth[prefix.Length..].Trim();
            if (!string.IsNullOrEmpty(token) && string.Equals(token, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
