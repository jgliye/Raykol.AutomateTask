using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Antiforgery;

namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 限制本机入口，调用框架防伪验证并统一响应头与异常提示
/// </summary>
public sealed class RequestGuardMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    /// <summary>
    /// 对每个请求施加本机管理边界
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var expectedHost = $"127.0.0.1:{context.Connection.LocalPort}";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        // Ant Design Vue 在运行时注入主题样式，脚本仍仅允许本站打包资源
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

        if (context.Connection.RemoteIpAddress is { } ip && !IPAddress.IsLoopback(ip) ||
            !string.Equals(request.Host.Value, expectedHost, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            return;
        }

        try
        {
            if (request.Path.StartsWithSegments("/api") &&
                !HttpMethods.IsGet(request.Method) &&
                !HttpMethods.IsHead(request.Method) &&
                !HttpMethods.IsOptions(request.Method))
                await antiforgery.ValidateRequestAsync(context);

            await next(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "页面验证已过期，请刷新页面后重试" });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex is ValidationException or InvalidDataException ? ex.Message : "操作失败，请检查配置、会话或网络；未输出请求凭据"
            });
        }
    }
}
