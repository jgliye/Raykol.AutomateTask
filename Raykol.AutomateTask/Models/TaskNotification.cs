namespace Raykol.AutomateTask.Models;

/// <summary>
/// 托盘可读取的最终结果，不包含账号、项目或 CRM 会话
/// </summary>
public sealed record TaskNotification(DateOnly Date, bool Success);
