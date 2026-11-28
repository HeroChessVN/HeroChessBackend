using System.Security.Claims;
using HeroChess.Api.Services;

namespace HeroChess.Api.Infrastructure;

public sealed class TrafficLimitsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TrafficLimits limits)
    {
        var api = context.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase);
        var socket = context.Request.Path.StartsWithSegments("/ws/v1", StringComparison.OrdinalIgnoreCase);
        var readiness = context.Request.Path.StartsWithSegments("/health/ready", StringComparison.OrdinalIgnoreCase);
        if (!api && !socket && !readiness) { await next(context); return; }

        // Only the authenticated identity can choose an account partition; never a query/header player ID.
        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var account))
        {
            limits.CheckAccount(account);
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/v1/matches", StringComparison.OrdinalIgnoreCase))
                limits.CheckAccount(account, "reads");
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/v1/ws-ticket", StringComparison.OrdinalIgnoreCase))
                limits.CheckAccount(account, "tickets");
        }
        var address = context.Connection.RemoteIpAddress;
        limits.CheckAddress(address is null ? "unknown" : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString(),
            context.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase));
        // An upgraded socket has a separate lifetime quota and must not hold an HTTP slot.
        using var lease = !socket ? limits.EnterHttp() : null;
        await next(context);
    }
}
