using System.Text.Json.Serialization;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 任务最近一次执行状态，保存首轮快照和防重标记，不单独生成每日记录
/// </summary>
public sealed record ExecutionState
{
    /// <summary>
    /// 本轮任务所属日期
    /// </summary>
    [JsonRequired]
    public DateOnly Date { get; init; }

    /// <summary>
    /// 首轮固定的配置快照，重试和核验始终沿用
    /// </summary>
    [JsonRequired]
    public TaskSchedule Plan { get; init; } = null!;

    /// <summary>
    /// 已开始的执行轮数
    /// </summary>
    [JsonRequired]
    public int Attempts { get; init; }

    /// <summary>
    /// 本轮执行开始的本机时间
    /// </summary>
    [JsonRequired]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// 下一轮重试时间，结束后为空
    /// </summary>
    [JsonRequired]
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// 是否已尝试提交工时，置位后禁止再次提交
    /// </summary>
    [JsonRequired]
    public bool PostAttempted { get; init; }

    /// <summary>
    /// 本轮任务是否已经结束
    /// </summary>
    [JsonRequired]
    public bool Complete { get; init; }

    /// <summary>
    /// 最近执行结果或失败原因
    /// </summary>
    [JsonRequired]
    public string Result { get; init; } = null!;

    /// <summary>
    /// 通知任务生成的待投递消息
    /// </summary>
    public TaskNotification? Notification { get; init; }

    /// <summary>
    /// 通知是否已被托盘确认接收
    /// </summary>
    public bool Delivered { get; init; }
}
