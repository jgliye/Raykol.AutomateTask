using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 后台任务所需的业务边界，支持隔离外部请求进行状态验证
/// </summary>
public interface IWorkHoursService
{
    /// <summary>
    /// 读取并验证当前工时配置
    /// </summary>
    WorkHoursConfig ReadConfig();

    /// <summary>
    /// 根据重复规则判断当天是否执行
    /// </summary>
    Task<bool> ShouldRun(WorkHoursConfig config, DateOnly date, CancellationToken ct);

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
