namespace Raykol.AutomateTask.Models;

/// <summary>
/// 托盘可读取的工时结果或定时提醒，标识用于按用户去重
/// </summary>
public sealed record TaskNotification
{
    /// <summary>
    /// 通知唯一标识，用于用户级去重
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// 通知所属任务标识
    /// </summary>
    public string TaskId { get; init; } = null!;

    /// <summary>
    /// 通知所属日期
    /// </summary>
    public DateOnly Date { get; init; }

    /// <summary>
    /// 是否为成功结果或普通提醒
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 桌面通知标题
    /// </summary>
    public string Title { get; init; } = null!;

    /// <summary>
    /// 桌面通知正文
    /// </summary>
    public string Message { get; init; } = null!;

    /// <summary>
    /// 通知生成的本机时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
