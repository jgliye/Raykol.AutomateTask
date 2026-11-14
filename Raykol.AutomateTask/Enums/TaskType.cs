namespace Raykol.AutomateTask.Enums;

/// <summary>
/// 自动化任务类型，数值同时用于配置存储和前后端接口
/// </summary>
public enum TaskType
{
    /// <summary>
    /// CRM 工时汇报
    /// </summary>
    WorkHours = 1,

    /// <summary>
    /// 定时桌面通知
    /// </summary>
    Notification = 2
}
