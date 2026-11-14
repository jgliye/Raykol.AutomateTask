using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 单个工时任务的配置读取、CRM 会话和填报约束
/// </summary>
public interface IWorkHoursService
{
    /// <summary>
    /// 最近一次实际 CRM 身份检查结果
    /// </summary>
    CrmSessionStatus Crm { get; }

    /// <summary>
    /// 读取并验证当前工时配置
    /// </summary>
    WorkHoursConfig ReadConfig();

    /// <summary>
    /// 只读检查当前 CRM 会话
    /// </summary>
    Task<CrmSessionStatus> CheckSession(CancellationToken ct);

    /// <summary>
    /// 登录 CRM 并返回核实后的本人账号
    /// </summary>
    Task<string> Login(string username, string password, WorkHoursConfig config, CancellationToken ct);

    /// <summary>
    /// 清除 CRM 会话
    /// </summary>
    void Logout();

    /// <summary>
    /// 只读查询本人当天
    /// </summary>
    Task<TimesheetDay> Read(WorkHoursConfig config, DateOnly date, CancellationToken ct);

    /// <summary>
    /// 校验每日表单并准备保存正文
    /// </summary>
    Task<List<KeyValuePair<string, string>>> Prepare(TimesheetDay day, WorkHoursConfig config, DateOnly date, CancellationToken ct);

    /// <summary>
    /// 单次保存，调用者必须先持久化未知状态
    /// </summary>
    Task Save(TimesheetDay day, List<KeyValuePair<string, string>> body, DateOnly date, CancellationToken ct);
}
