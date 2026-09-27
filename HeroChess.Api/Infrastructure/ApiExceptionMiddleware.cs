// Vai trò file: Bộ xử lý lỗi chung, tương tự Filter kết hợp @ControllerAdvice.
using HeroChess.Contracts;

namespace HeroChess.Api.Infrastructure;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    // InvokeAsync: Chạy middleware kế tiếp; map lỗi nghiệp vụ, thiếu danh tính và lỗi bất ngờ sang 4xx/500; không trả stack trace cho client.
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (ApiException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
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
