using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 限制本机入口并统一响应头与异常提示
/// </summary>
public sealed class RequestGuardMiddleware(RequestDelegate next)
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
            await next(context);
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
