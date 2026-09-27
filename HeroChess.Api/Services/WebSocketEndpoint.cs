// Vai trò file: Endpoint nâng cấp HTTP thành socket, xác thực vé, nhận message và gọi service trong scope riêng từng message.
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.Extensions.Options;
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public static class WebSocketEndpoint
{
    // HandleAsync: Kiểm tra Origin/ticket, giới hạn payload/tần suất; authorize trước subscribe, trả ACK duplicate và dọn connection khi ngắt.
    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (context.Request.Headers.Origin.Count > 0 && Uri.TryCreate(context.Request.Headers.Origin[0], UriKind.Absolute, out var origin) &&
            !string.Equals(origin.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = 403; return; }
        var token = context.Request.Query["ticket"].ToString();
        var tickets = context.RequestServices.GetRequiredService<WsTicketService>();
        if (string.IsNullOrEmpty(token) || !tickets.TryConsume(token, out var playerId)) { context.Response.StatusCode = 401; return; }
        // Kiểm tra lại sau khi tiêu thụ vé; Admin/disabled không được nối socket dù có vé cũ.
        if (!await context.RequestServices.GetRequiredService<AppDbContext>().Players.AsNoTracking()
            .AnyAsync(x => x.Id == playerId && x.Status == "active", context.RequestAborted))
        { context.Response.StatusCode = 403; return; }
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var hub = context.RequestServices.GetRequiredService<MatchConnectionHub>();
        var options = context.RequestServices.GetRequiredService<IOptions<MatchRuntimeOptions>>().Value;
        var scopes = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
        var connectionId = hub.Add(socket);
        var buffer = new byte[Math.Min(8192, options.MaxWebSocketMessageBytes)];
        var window = DateTimeOffset.UtcNow; var messages = 0;
        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                using var stream = new MemoryStream(); WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close) { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); return; }
                    stream.Write(buffer, 0, result.Count);
                    if (stream.Length > options.MaxWebSocketMessageBytes) { await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", CancellationToken.None); return; }
                } while (!result.EndOfMessage);
                if (result.MessageType != WebSocketMessageType.Text) continue;
                if (DateTimeOffset.UtcNow - window > TimeSpan.FromSeconds(10)) { window = DateTimeOffset.UtcNow; messages = 0; }
                if (++messages > 60) { await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "rate limit", CancellationToken.None); return; }
                string? requestId = null; Guid? commandId = null;
                try
                {
                    using var message = JsonDocument.Parse(stream.ToArray());
                    var root = message.RootElement; var type = root.GetProperty("type").GetString();
                    if (root.TryGetProperty("requestId", out var rid)) requestId = rid.GetString();
                    var payload = root.GetProperty("payload");
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<AppDbContext>().Players.AsNoTracking()
                        .AnyAsync(x => x.Id == playerId && x.Status == "active", context.RequestAborted))
                        throw new ApiException(403, "PLAYER_REQUIRED", "An active player account is required.");
                    if (type == "match.subscribe")
                    {
                        var matchId = payload.GetProperty("matchId").GetGuid();
                        var gate = scope.ServiceProvider.GetRequiredService<MatchLockRegistry>().For(matchId);
                        await gate.WaitAsync(context.RequestAborted);
                        try
                        {
                            var state = await scope.ServiceProvider.GetRequiredService<MatchReadService>().StateAsync(playerId, matchId, context.RequestAborted);
                            hub.Subscribe(connectionId, matchId);
                            await hub.SendAsync(connectionId, new { type = "match.state", requestId, payload = state }, context.RequestAborted);
                        }
                        finally { gate.Release(); }
                    }
                    else if (type == "match.command")
                    {
                        var matchId = payload.GetProperty("matchId").GetGuid(); commandId = payload.GetProperty("commandId").GetGuid();
                        var request = new MatchCommandRequest(commandId.Value, payload.GetProperty("expectedVersion").GetInt32(), payload.GetProperty("action").Clone());
                        var receivedAt = DateTimeOffset.UtcNow;
                        await scope.ServiceProvider.GetRequiredService<MatchReadService>().StateAsync(playerId, matchId, context.RequestAborted);
                        hub.Subscribe(connectionId, matchId);
                        var accepted = await scope.ServiceProvider.GetRequiredService<MatchCommandService>().ExecuteAsync(playerId, matchId, request, receivedAt, context.RequestAborted);
                        if (accepted.Duplicate)
                            await hub.SendAsync(connectionId, new { type = "match.command_accepted", requestId, payload = accepted }, context.RequestAborted);
                    }
                    else throw new ApiException(400, "INVALID_MESSAGE_TYPE", "Unknown WebSocket message type.");
                }
                catch (ApiException error)
                {
                    await hub.SendAsync(connectionId, new { type = "match.command_rejected", requestId, payload = new { commandId, code = error.Code, message = error.Message, details = error.Details } }, context.RequestAborted);
                }
                catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    await hub.SendAsync(connectionId, new { type = "match.command_rejected", requestId, payload = new { commandId, code = "MALFORMED_MESSAGE", message = "The message is malformed." } }, context.RequestAborted);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally { hub.Remove(connectionId); socket.Dispose(); }
    }
}
