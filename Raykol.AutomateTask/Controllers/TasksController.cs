using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Services;

namespace Raykol.AutomateTask.Controllers;

/// <summary>
/// 任务管理与统一日志入口，CRM 操作仅适用于工时任务
/// </summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController(ITaskService taskService, ILogger<TasksController> log) : ControllerBase
{
    /// <summary>
    /// 读取任务列表及各自配置，不请求外部站点
    /// </summary>
    [HttpGet]
    public IActionResult List() => Ok(new { tasks = taskService.GetOverview(), timeLabels = WorkHoursConfig.TimeLabels });

    /// <summary>
    /// 创建默认关闭的工时或通知任务
    /// </summary>
    [HttpPost]
    public IActionResult Create(TaskCreateRequest request) => Ok(new { id = taskService.Create(request).Id });

    /// <summary>
    /// 保存选中任务的配置
    /// </summary>
    [HttpPut("{id}")]
    public IActionResult Save(string id, TaskEditRequest request)
    {
        taskService.Save(id, request);
        return Ok(new { message = "任务已保存；已开始的轮次沿用原计划快照" });
    }

    /// <summary>
    /// 单独切换任务开关
    /// </summary>
    [HttpPost("{id}/enabled")]
    public IActionResult SetEnabled(string id, TaskEnabledRequest request)
    {
        taskService.SetEnabled(id, request.Enabled);
        return Ok(new { message = request.Enabled ? "任务已启用" : "任务已停用" });
    }

    /// <summary>
    /// 登录并核实本人身份，同时拒绝跨任务重复绑定账号
    /// </summary>
    [HttpPost("{id}/crm/login")]
    public async Task<IActionResult> Login(string id, CrmLoginRequest request, CancellationToken ct) =>
        Ok(await taskService.Login(id, request, ct));

    /// <summary>
    /// 清除会话并释放账号绑定，不撤销已保存的执行意图
    /// </summary>
    [HttpPost("{id}/crm/logout")]
    public IActionResult Logout(string id)
    {
        taskService.Logout(id);
        return Ok(new { message = "已退出 CRM" });
    }

    /// <summary>
    /// 实际检查会话，网络故障与登录失效分别返回
    /// </summary>
    [HttpPost("{id}/crm/check")]
    public async Task<IActionResult> CheckCrm(string id, CancellationToken ct) => Ok(await taskService.CheckCrm(id, ct));

    /// <summary>
    /// 只读核对本人月表和表单，不发送工时保存请求
    /// </summary>
    [HttpPost("{id}/preflight")]
    public async Task<IActionResult> Preflight(string id, CancellationToken ct)
    {
        var task = taskService.GetTask(id);
        var stage = "读取工时配置";

        if (!taskService.TryBeginCrmOperation(id))
            throw new ValidationException("该任务正在进行 CRM 操作，请稍后再预检");

        try
        {
            var workHoursService = taskService.GetWorkHours(id);
            var settings = workHoursService.ReadConfig();
            var date = DateOnly.FromDateTime(DateTime.Today);
            stage = "判断重复日期及工作日日历";
            var scheduledDay = await taskService.ShouldRun(settings, date, ct);
            stage = "核对本人身份及读取当天月表";
            var day = await workHoursService.Read(settings, date, ct);

            if (!day.HasRecords)
            {
                stage = "校验每日填报表单";
                await workHoursService.Prepare(day, settings, date, ct);
            }

            log.LogInformation("[任务:{TaskId}] {TaskName}：只读预检完成；未发送工时保存请求", id, task.Name);
            var result = day.HasRecords ? "当天已有记录，将整天跳过" : "表单与项目验证通过，未保存工时";
            return Ok(new { message = $"日期：{date:yyyy-MM-dd}；账号：{day.Account}；符合重复规则：{(scheduledDay ? "是" : "否")}；{result}" });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("[任务:{TaskId}] {TaskName}：只读预检失败[{Stage}]：{Reason}",
                id, task.Name, stage, WorkHoursService.GetErrorMessage(ex));
            throw;
        }
        finally
        {
            taskService.EndCrmOperation(id);
        }
    }

    /// <summary>
    /// 按任务和级别读取最近日志，不建立历史日志内存缓存
    /// </summary>
    [HttpGet("logs")]
    public IActionResult Logs(string? taskId = null, string? level = null) => Ok(new { text = taskService.ReadLogs(taskId, level) });
}
