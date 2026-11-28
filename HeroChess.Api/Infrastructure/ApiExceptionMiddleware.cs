// Vai trò file: Bộ xử lý lỗi chung, tương tự Filter kết hợp @ControllerAdvice.
using HeroChess.Contracts;

namespace HeroChess.Api.Infrastructure;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    // InvokeAsync: Chạy middleware kế tiếp; map lỗi nghiệp vụ, thiếu danh tính và lỗi bất ngờ sang 4xx/500; không trả stack trace cho client.
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (BadHttpRequestException exception)
        {
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = exception.StatusCode;
            var tooLarge = exception.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new ApiError(tooLarge ? "REQUEST_TOO_LARGE" : "BAD_REQUEST",
                tooLarge ? "The request body exceeds the allowed size." : "The HTTP request is invalid.", context.TraceIdentifier));
        }
        catch (ApiException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
            if (exception.StatusCode == 429 && exception.RetryAfterSeconds is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new ApiError(exception.Code, exception.Message, context.TraceIdentifier, exception.Details));
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("UNAUTHORIZED", "Authentication is required.", context.TraceIdentifier));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API error for request {TraceIdentifier}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiError("INTERNAL_ERROR", "The server could not complete the request.", context.TraceIdentifier));
        }
    }
}
