using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 通过本机管道提供当天终态，托盘无权读取配置或触发业务
/// </summary>
public sealed class NotificationService(ITaskService taskService, ILogger<NotificationService> log)
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
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(3));
                        var receipts = await NotificationProtocol.Read<HashSet<string>>(pipe, deadline.Token);

                        if (receipts == null || receipts.Count > 512 || receipts.Any(id => string.IsNullOrEmpty(id) || id.Length > 150))
                            throw new InvalidDataException("通知回执无效");

                        var notification = taskService.GetNotification(receipts);
                        await NotificationProtocol.Write(pipe, notification, deadline.Token);
                        // 客户端读完再断开，避免 Disconnect 丢弃仍在管道缓冲区中的数据
                        var receipt = new byte[1];
                        await pipe.ReadExactlyAsync(receipt, deadline.Token);

                        if (receipt[0] == 1 && notification != null)
                            taskService.Acknowledge(notification);

                        lastError = null;
                    }
                    catch (IOException)
                    {
                        // 托盘退出导致断线，不影响已持久化的任务结果
                    }
                    catch (JsonException)
                    {
                        // 无效客户端帧不影响后续托盘连接
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
