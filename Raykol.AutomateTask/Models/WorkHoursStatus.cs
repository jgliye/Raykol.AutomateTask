namespace Raykol.AutomateTask.Models;

/// <summary>
/// 工时管理接口的状态数据，不包含 CRM 密码或会话
/// </summary>
public sealed class WorkHoursStatus
{
    /// <summary>
    /// 当前持久化配置
    /// </summary>
    public required WorkHoursConfig Settings { get; init; }

    /// <summary>
    /// 当前计划的全部时间格
    /// </summary>
    public required string[] Slots { get; init; }

    /// <summary>
    /// 服务端支持的完整时间标签，供前端选择和预览
    /// </summary>
    public required string[] TimeLabels { get; init; }

    /// <summary>
    /// 最近一次执行结果
    /// </summary>
    public required string Result { get; init; }

    /// <summary>
    /// 当天已执行轮数
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// 当天最多执行轮数
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// 下一次首轮触发时刻
    /// </summary>
    public DateTimeOffset? NextRun { get; init; }

    /// <summary>
    /// 当前任务下一轮重试时刻
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }
}
