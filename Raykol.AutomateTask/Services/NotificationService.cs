using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 通过本机管道提供当天终态，托盘无权读取配置或触发业务
/// </summary>
public sealed class NotificationService(WorkHoursWorker worker, ILogger<NotificationService> log)
    : BackgroundService
{
    /// <summary>
    /// 接受托盘连接并发送受限结果，断线或通知故障不影响工时调度
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastError = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 保持同一个管道实例，防止两个连接间被其他进程抢占名称
                using var pipe = CreatePipe();

                while (!stoppingToken.IsCancellationRequested)
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);

                    try
                    {
                        var date = DateOnly.FromDateTime(DateTime.Today);
                        var state = worker.ReadState(date);
                        TaskNotification? notification = null;

                        if (state?.Complete == true && (state.Result.StartsWith("成功", StringComparison.Ordinal)
                            || state.Result.StartsWith("失败", StringComparison.Ordinal)
                            || state.Result.StartsWith("结果不明", StringComparison.Ordinal)))
                            notification = new(date, state.Result.StartsWith("成功", StringComparison.Ordinal));

                        // 管道使用单行帧，显式采用紧凑格式，避免全局缩进插入换行
                        var bytes = Encoding.UTF8.GetBytes(SerializeHelper.Serialize(notification, JsonSerializerOptions.Web));
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(3));
                        await pipe.WriteAsync(bytes, deadline.Token);
                        await pipe.WriteAsync("\n"u8.ToArray(), deadline.Token);
                        // 客户端读完再断开，避免 Disconnect 丢弃仍在管道缓冲区中的数据
                        var receipt = new byte[1];
                        await pipe.ReadExactlyAsync(receipt, deadline.Token);
                        lastError = null;
                    }
                    catch (IOException)
                    {
                        // 托盘退出导致断线，不影响已持久化的任务结果
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // 单个客户端超时后继续接受后续连接
                    }
                    finally
                    {
                        pipe.Disconnect();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                const string error = "托盘通知通道不可用，将稍后重试；工时调度继续运行";

                if (lastError != error)
                {
                    log.LogWarning("{Result}", error);
                    lastError = error;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 当前服务拥有管道，交互用户可读结果和写回执，拒绝网络登录身份
    /// </summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(NotificationProtocol.PipeName(AppContext.BaseDirectory),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, NotificationProtocol.MaxMessageBytes, security);
    }
}
