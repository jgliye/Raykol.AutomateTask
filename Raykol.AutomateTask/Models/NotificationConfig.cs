using System.ComponentModel.DataAnnotations;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 无需 CRM 的桌面通知内容和执行计划
/// </summary>
public sealed record NotificationConfig : TaskSchedule
{
    /// <summary>
    /// 桌面通知标题
    /// </summary>
    public string Title { get; init; } = "定时提醒";

    /// <summary>
    /// 桌面通知正文
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 按 Windows 托盘通知长度校验内容
    /// </summary>
    public void Validate()
    {
        ValidateSchedule();

        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 60 || Title.Contains('\r') || Title.Contains('\n') ||
            string.IsNullOrWhiteSpace(Content) || Content.Length > 200)
            throw new ValidationException("通知标题须为 1–60 字且不换行，通知内容须为 1–200 字");
    }
}
