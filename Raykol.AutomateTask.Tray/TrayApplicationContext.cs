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
    private readonly HashSet<string> receipts = [];
    private DateOnly receiptDate = DateOnly.FromDateTime(DateTime.Today);
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
        icon = new NotifyIcon { Icon = image, Text = "自动化任务：等待服务", ContextMenuStrip = menu, Visible = true };
        menu.Items.Add("打开管理页面", null, OpenManagement);
        menu.Items.Add("退出提醒（后台任务继续）", null, (_, _) => ExitThread());
        icon.DoubleClick += OpenManagement;
        timer.Tick += Poll;

        try
        {
            if (File.Exists(receiptPath) && new FileInfo(receiptPath).Length < NotificationProtocol.MaxMessageBytes)
            {
                var lines = File.ReadAllLines(receiptPath);

                if (lines.FirstOrDefault() == receiptDate.ToString("yyyy-MM-dd"))
                    receipts.UnionWith(lines.Skip(1).Where(id => id.Length is > 0 and <= 150).Take(512));
            }
        }
        catch (IOException)
        {
            icon.Text = "自动化任务：通知回执不可读";
        }
        catch (UnauthorizedAccessException)
        {
            icon.Text = "自动化任务：通知回执无读取权限";
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
            var today = DateOnly.FromDateTime(DateTime.Today);

            if (receiptDate != today)
            {
                receiptDate = today;
                receipts.Clear();
            }

            await NotificationProtocol.Write(pipe, receipts, deadline.Token);
            var notification = await NotificationProtocol.Read<TaskNotification>(pipe, deadline.Token);

            if (stopping.IsCancellationRequested)
                return;

            icon.Text = "自动化任务：已连接服务";

            if (notification == null)
            {
                await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
                return;
            }

            if (notification.Date != today || string.IsNullOrWhiteSpace(notification.Id) || notification.Id.Length > 150 ||
                string.IsNullOrWhiteSpace(notification.Title) || notification.Title.Length > 60 ||
                string.IsNullOrWhiteSpace(notification.Message) || notification.Message.Length > 200)
                throw new InvalidDataException("仅接受当天有效通知");

            if (!receipts.Contains(notification.Id))
            {
                icon.ShowBalloonTip(10000, notification.Title, notification.Message,
                    notification.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
                receipts.Add(notification.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                var temp = receiptPath + ".tmp";
                File.WriteAllLines(temp, [today.ToString("yyyy-MM-dd"), .. receipts], new UTF8Encoding(false));
                File.Move(temp, receiptPath, true);
            }

            // 展示并记录本用户回执后才确认，服务据此记录托盘接收状态
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException)
        {
            if (!stopping.IsCancellationRequested)
                icon.Text = "自动化任务：等待服务或通知通道恢复";
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
            icon.ShowBalloonTip(10000, "自动化任务", "管理页面未打开，请检查服务目录和打开页面脚本", ToolTipIcon.Warning);
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
