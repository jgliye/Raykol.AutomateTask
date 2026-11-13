using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Tray;

/// <summary>
/// 用户会话托盘，定时读取简短终态并按用户去重，不访问业务数据库
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly string root;
    private readonly string receiptPath;
    private readonly Icon image;
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10000 };
    private readonly CancellationTokenSource stopping = new();
    private string? lastReceipt;
    private bool fetching;

    /// <summary>
    /// 初始化托盘与用户级回执，十秒轮询不缓存历史通知
    /// </summary>
    public TrayApplicationContext(string root)
    {
        this.root = root;
        receiptPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Raykol.AutomateTask", NotificationProtocol.PipeName(root) + ".txt");
        image = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        icon = new NotifyIcon { Icon = image, Text = "睿科自动化任务：等待服务", ContextMenuStrip = menu, Visible = true };
        menu.Items.Add("打开管理页面", null, OpenManagement);
        menu.Items.Add("退出提醒（后台任务继续）", null, (_, _) => ExitThread());
        icon.DoubleClick += OpenManagement;
        timer.Tick += Poll;

        try
        {
            lastReceipt = File.Exists(receiptPath) ? File.ReadAllText(receiptPath) : null;
        }
        catch (IOException)
        {
            icon.Text = "睿科自动化任务：通知回执不可读";
        }
        catch (UnauthorizedAccessException)
        {
            icon.Text = "睿科自动化任务：通知回执无读取权限";
        }

        timer.Start();
    }

    /// <summary>
    /// 有界读取单条通知，服务离线时静默等待；退出取消所有进行中的读取
    /// </summary>
    private async void Poll(object? sender, EventArgs e)
    {
        if (fetching || stopping.IsCancellationRequested)
            return;

        fetching = true;

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            // 明确申请数据读写权限，避免通用写权限额外要求创建管道实例的权限
            using var pipe = new NamedPipeClientStream(".", NotificationProtocol.PipeName(root),
                PipeAccessRights.ReadWrite, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Anonymous, HandleInheritability.None);
            await pipe.ConnectAsync(deadline.Token);
            var buffer = new byte[NotificationProtocol.MaxMessageBytes];
            var length = 0;

            while (length < buffer.Length)
            {
                var count = await pipe.ReadAsync(buffer.AsMemory(length), deadline.Token);

                if (count == 0)
                    break;

                length += count;

                if (buffer[length - 1] == (byte)'\n')
                    break;
            }

            if (stopping.IsCancellationRequested)
                return;

            if (length == 0 || length == buffer.Length || buffer[length - 1] != (byte)'\n')
                throw new InvalidDataException("通知格式无效");

            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
            var notification = SerializeHelper.Deserialize<TaskNotification>(buffer.AsSpan(0, length));
            icon.Text = "睿科自动化任务：已连接服务";

            if (notification == null)
                return;

            if (notification.Date != DateOnly.FromDateTime(DateTime.Today))
                throw new InvalidDataException("仅接受当天通知");

            var receipt = $"{notification.Date:yyyy-MM-dd}|{notification.Success}";

            if (lastReceipt == receipt)
                return;

            var text = notification.Success ? "工时填报成功，已核对通过" : "工时填报失败或结果不明，请打开管理页面检查";
            icon.ShowBalloonTip(10000, "睿科自动化任务", $"{notification.Date:yyyy-MM-dd} {text}",
                notification.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
            lastReceipt = receipt;
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            var temp = receiptPath + ".tmp";
            File.WriteAllText(temp, receipt, new UTF8Encoding(false));
            File.Move(temp, receiptPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException)
        {
            if (!stopping.IsCancellationRequested)
                icon.Text = "睿科自动化任务：等待服务或通知通道恢复";
        }
        finally
        {
            fetching = false;
        }
    }

    /// <summary>
    /// 由用户点击后打开本机管理页面，无需管理员授权
    /// </summary>
    private void OpenManagement(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(root, "Open-Management.ps1")}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception)
        {
            icon.ShowBalloonTip(10000, "睿科自动化任务", "管理页面未打开，请检查服务目录和打开页面脚本", ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// 退出提醒时停止轮询，不关闭服务和正在执行的任务
    /// </summary>
    protected override void ExitThreadCore()
    {
        timer.Stop();
        stopping.Cancel();
        icon.Visible = false;
        base.ExitThreadCore();
    }

    /// <summary>
    /// 释放托盘、菜单、计时器与取消资源
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stopping.Cancel();
            timer.Dispose();
            icon.Dispose();
            image.Dispose();
            menu.Dispose();
            stopping.Dispose();
        }

        base.Dispose(disposing);
    }
}
