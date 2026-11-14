using System.Text.Json.Serialization;
using Raykol.AutomateTask.Enums;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 每个任务独占一条配置记录，包含设置、加密会话和最近执行状态
/// </summary>
public sealed record TaskConfig
{
    /// <summary>
    /// 任务唯一标识
    /// </summary>
    [JsonRequired]
    public string Id { get; init; } = null!;

    /// <summary>
    /// 任务名称
    /// </summary>
    [JsonRequired]
    public string Name { get; init; } = null!;

    /// <summary>
    /// 任务类型
    /// </summary>
    [JsonRequired]
    public TaskType Type { get; init; }

    /// <summary>
    /// 当前保存的执行计划及业务配置
    /// </summary>
    [JsonRequired]
    public TaskSchedule Settings { get; init; } = null!;

    /// <summary>
    /// 仅工时任务使用的加密 CRM 会话
    /// </summary>
    public string? CrmSession { get; init; }

    /// <summary>
    /// 最近一次执行快照、结果和防重标记
    /// </summary>
    public ExecutionState? LastExecution { get; init; }

    /// <summary>
    /// 验证任务类型及运行快照，损坏状态不得被当作未执行
    /// </summary>
    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(Name) ||
            Name.Length > 40 || Name.Any(char.IsControl) ||
            (Type == TaskType.WorkHours && Settings is not WorkHoursConfig) ||
            (Type == TaskType.Notification && Settings is not NotificationConfig) ||
            Type is not (TaskType.WorkHours or TaskType.Notification))
            throw new InvalidDataException("任务配置无效");

        if (Settings is WorkHoursConfig workHours)
            workHours.GetTimeSlots();
        else
            ((NotificationConfig)Settings).Validate();

        if (LastExecution is not { } state)
            return;

        if (state.Plan == null || state.Attempts < 1 || state.Attempts > state.Plan.MaxAttempts ||
            (Type == TaskType.WorkHours && (state.Plan is not WorkHoursConfig || state.Notification != null)) ||
            (Type == TaskType.Notification && (state.Plan is not NotificationConfig || state.PostAttempted)) ||
            (state.Notification != null && (state.Notification.TaskId != Id || state.Notification.Date != state.Date)))
            throw new InvalidDataException("任务执行状态无效");

        if (state.Plan is WorkHoursConfig plan)
            plan.GetTimeSlots();
        else
            ((NotificationConfig)state.Plan).Validate();
    }
}

/// <summary>
/// 新增任务参数，不允许客户端指定任务标识
/// </summary>
public sealed record TaskCreateRequest
{
    /// <summary>
    /// 新任务名称
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// 新任务类型
    /// </summary>
    public TaskType Type { get; init; }
}

/// <summary>
/// 保存任务名称和类型对应的配置，不接受客户端会话或执行状态
/// </summary>
public sealed record TaskEditRequest
{
    /// <summary>
    /// 修改后的任务名称
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// 修改后的执行计划及业务配置
    /// </summary>
    public TaskSchedule Settings { get; init; } = null!;
}

/// <summary>
/// 独立任务开关
/// </summary>
public sealed record TaskEnabledRequest
{
    /// <summary>
    /// 是否启用任务
    /// </summary>
    public bool Enabled { get; init; }
}

/// <summary>
/// 列表和编辑页共用的任务状态，不包含 CRM 会话或密码
/// </summary>
public sealed record TaskOverview
{
    /// <summary>
    /// 任务唯一标识
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// 任务名称
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// 任务类型
    /// </summary>
    public TaskType Type { get; init; }

    /// <summary>
    /// 管理页可编辑配置，读取失败时为空
    /// </summary>
    public TaskSchedule? Settings { get; init; }

    /// <summary>
    /// 下次执行或重试的本机时间
    /// </summary>
    public DateTimeOffset? NextRun { get; init; }

    /// <summary>
    /// 最近执行结果或实时提示
    /// </summary>
    public string Result { get; init; } = null!;

    /// <summary>
    /// 当前执行轮数
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// 当前计划允许的最大执行轮数
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// 工时任务最近一次 CRM 会话检查结果
    /// </summary>
    public CrmSessionStatus? Crm { get; init; }
}

/// <summary>
/// CRM 最近一次实际检查结果，网络异常与登录失效分别显示
/// </summary>
public sealed record CrmSessionStatus
{
    /// <summary>
    /// CRM 会话状态
    /// </summary>
    public string State { get; init; } = null!;

    /// <summary>
    /// 会话检查结果提示
    /// </summary>
    public string Message { get; init; } = null!;

    /// <summary>
    /// 最近一次实际检查的本机时间
    /// </summary>
    public DateTimeOffset? CheckedAt { get; init; }
}
