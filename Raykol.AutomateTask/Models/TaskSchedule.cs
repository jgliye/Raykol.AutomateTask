using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Raykol.AutomateTask.Enums;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 工时和通知任务共用的本机时间计划与失败重试配置
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$task")]
[JsonDerivedType(typeof(WorkHoursConfig), (int)TaskType.WorkHours)]
[JsonDerivedType(typeof(NotificationConfig), (int)TaskType.Notification)]
public abstract record TaskSchedule
{
    /// <summary>
    /// 是否启用任务，新增任务默认关闭
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 每日本机时间执行时刻
    /// </summary>
    public string RunAt { get; init; } = "19:00";

    /// <summary>
    /// Weekly 自选星期，Workdays 法定工作日
    /// </summary>
    public string RepeatMode { get; init; } = "Workdays";

    /// <summary>
    /// 自选星期，1 为周一，7 为周日
    /// </summary>
    public int[] WeekDays { get; init; } = [1, 2, 3, 4, 5];

    /// <summary>
    /// 单次网络请求超时秒数
    /// </summary>
    public int TimeoutSeconds { get; init; } = 20;

    /// <summary>
    /// 失败后各轮开始时间的间隔秒数
    /// </summary>
    public int RetryIntervalSeconds { get; init; } = 120;

    /// <summary>
    /// 首次执行失败后的额外重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// 首轮和重试合计的最大执行轮数
    /// </summary>
    [JsonIgnore]
    public int MaxAttempts => MaxRetries + 1;

    /// <summary>
    /// 校验公共执行计划和重试参数
    /// </summary>
    public void ValidateSchedule()
    {
        if (RepeatMode is not ("Weekly" or "Workdays") || WeekDays == null ||
            WeekDays.Any(d => d is < 1 or > 7) || WeekDays.Distinct().Count() != WeekDays.Length ||
            (RepeatMode == "Weekly" && WeekDays.Length == 0))
            throw new ValidationException("请选择有效重复方式，自选星期至少勾选一天（周一至周日）");

        if (!TimeOnly.TryParseExact(RunAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            TimeoutSeconds is < 5 or > 90 || RetryIntervalSeconds is < 10 or > 86400 || MaxRetries is < 0 or > 20)
            throw new ValidationException("执行时间、超时或重试参数无效（重试间隔 10–86400 秒，次数 0–20）");
    }
}
