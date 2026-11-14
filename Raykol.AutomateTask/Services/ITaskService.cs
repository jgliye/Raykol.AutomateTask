using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 多任务配置、独立运行状态、CRM 绑定和统一日志入口
/// </summary>
public interface ITaskService
{
    /// <summary>
    /// 各任务的完整记录
    /// </summary>
    IReadOnlyList<TaskConfig> Tasks { get; }

    /// <summary>
    /// 获取任务列表及最近运行结果
    /// </summary>
    IReadOnlyList<TaskOverview> GetOverview();

    /// <summary>
    /// 获取指定任务，标识不存在时拒绝操作
    /// </summary>
    TaskConfig GetTask(string id);

    /// <summary>
    /// 获取工时任务独占的 CRM 业务实例
    /// </summary>
    IWorkHoursService GetWorkHours(string id);

    /// <summary>
    /// 短暂登记单项 CRM 操作，防止填报期间切换账号或会话
    /// </summary>
    bool TryBeginCrmOperation(string id);

    /// <summary>
    /// 结束已登记的 CRM 操作
    /// </summary>
    void EndCrmOperation(string id);

    /// <summary>
    /// 新增默认关闭的任务，只创建一条完整记录
    /// </summary>
    TaskConfig Create(TaskCreateRequest request);

    /// <summary>
    /// 验证后保存任务名称与对应类型的配置
    /// </summary>
    void Save(string id, TaskEditRequest request);

    /// <summary>
    /// 独立切换指定任务开关
    /// </summary>
    void SetEnabled(string id, bool enabled);

    /// <summary>
    /// 登录 CRM 并拒绝同一账号重复绑定不同任务
    /// </summary>
    Task<CrmSessionStatus> Login(string id, CrmLoginRequest request, CancellationToken ct);

    /// <summary>
    /// 退出 CRM 并释放该账号的任务绑定
    /// </summary>
    void Logout(string id);

    /// <summary>
    /// 实际检查单个工时任务的 CRM 状态
    /// </summary>
    Task<CrmSessionStatus> CheckCrm(string id, CancellationToken ct);

    /// <summary>
    /// 判断重复日期，仅工作日模式访问公共节假日日历
    /// </summary>
    Task<bool> ShouldRun(TaskSchedule config, DateOnly date, CancellationToken ct);

    /// <summary>
    /// 获取未持久化运行结果时的实时提示
    /// </summary>
    string GetStatus(string id);

    /// <summary>
    /// 更新实时提示，返回内容是否发生变化
    /// </summary>
    bool SetStatus(string id, string result);

    /// <summary>
    /// 读取指定任务的最近执行状态，按日期判断是否已执行
    /// </summary>
    ExecutionState? ReadState(string id, DateOnly date);

    /// <summary>
    /// 更新任务记录中的执行意图及结果，不额外新增每日记录
    /// </summary>
    void SaveState(string id, ExecutionState state);

    /// <summary>
    /// 取得当前用户尚未确认的当天通知
    /// </summary>
    TaskNotification? GetNotification(IReadOnlySet<string> receipts);

    /// <summary>
    /// 记录托盘已接收提醒，不修改工时记录
    /// </summary>
    void Acknowledge(TaskNotification notification);

    /// <summary>
    /// 按任务和级别读取最近日志
    /// </summary>
    string ReadLogs(string? id = null, string? level = null);
}
