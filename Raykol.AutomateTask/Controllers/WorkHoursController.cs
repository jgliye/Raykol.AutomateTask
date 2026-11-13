using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Services;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Controllers;

/// <summary>
/// 工时管理 JSON 接口，业务请求统一交给工时服务
/// </summary>
[ApiController]
[Route("api/work-hours")]
public sealed class WorkHoursController(
    WorkHoursService workHours, WorkHoursWorker worker,
    IAntiforgery antiforgery,
    LogReader logs, ILogger<WorkHoursController> log) : ControllerBase
{
    /// <summary>
    /// 获取页面写操作使用的防伪令牌，无需管理登录
    /// </summary>
    [HttpGet("session")]
    public IActionResult Session() => Ok(new
    {
        requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
    });

    /// <summary>
    /// 读取当前配置、计划和执行状态
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        var settings = workHours.ReadConfig();
        var now = DateTimeOffset.Now;
        var state = worker.ReadState(DateOnly.FromDateTime(now.DateTime));

        return Ok(new WorkHoursStatus
        {
            Settings = settings,
            Slots = settings.GetTimeSlots(),
            TimeLabels = WorkHoursConfig.TimeLabels,
            Result = state?.Result ?? worker.Status,
            Attempt = state?.Attempts ?? 0,
            MaxAttempts = state?.Plan.MaxAttempts ?? settings.MaxAttempts,
            NextRun = settings.Enabled ? WorkHoursService.Next(now, settings) : null,
            RetryAt = state?.RetryAt
        });
    }

    /// <summary>
    /// 保存可编辑配置，绑定账号始终沿用已核实身份
    /// </summary>
    [HttpPost("config")]
    public IActionResult SaveConfig(WorkHoursConfig settings)
    {
        var current = workHours.ReadConfig();
        settings = settings with { Account = current.Account };
        workHours.SaveConfig(settings);

        if (current.GetCrmOrigin() != settings.GetCrmOrigin())
        {
            workHours.Logout();
            log.LogInformation("CRM 站点已变更，会话已清除，请重新登录");
        }

        log.LogInformation("配置保存；执行时间已重新计算");
        return Ok(new { message = "配置已保存；当前任务沿用原重试设置" });
    }

    /// <summary>
    /// 登录 CRM 并绑定服务器返回的本人身份，不保存密码
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(CrmLoginRequest request, CancellationToken ct)
    {
        try
        {
            var settings = workHours.ReadConfig();
            var account = await workHours.Login(request.Username, request.Password, settings, ct);
            workHours.SaveConfig(settings with { Account = account });
            log.LogInformation("CRM 登录及本人身份核对成功；会话已加密保存");
            return Ok(new { message = "CRM 登录及本人身份核对成功" });
        }
        catch
        {
            workHours.Logout();
            log.LogWarning("CRM 登录失败；未保留本次会话");
            throw;
        }
    }

    /// <summary>
    /// 清除 CRM 会话，不撤销已保存的执行意图
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        workHours.Logout();
        log.LogInformation("已退出 CRM 会话");
        return Ok(new { message = "已退出 CRM 会话" });
    }

    /// <summary>
    /// 只读核对重复日期、本人月表和表单，不发送工时保存请求
    /// </summary>
    [HttpPost("preflight")]
    public async Task<IActionResult> Preflight(CancellationToken ct)
    {
        var settings = workHours.ReadConfig();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var scheduledDay = await workHours.ShouldRun(settings, date, ct);
        var day = await workHours.Read(settings, date, ct);

        if (!day.HasRecords)
            await workHours.Prepare(day, settings, date, ct);

        log.LogInformation("只读预检完成；未发送工时保存请求");
        var result = day.HasRecords ? "当天已有记录，将整天跳过" : "表单与项目验证通过，未保存工时";
        return Ok(new { message = $"日期：{date:yyyy-MM-dd}；账号：{day.Account}；符合重复规则：{(scheduledDay ? "是" : "否")}；{result}" });
    }

    /// <summary>
    /// 按需读取最近日志，不建立服务端历史日志缓存
    /// </summary>
    [HttpGet("logs")]
    public IActionResult Logs() => Ok(new { text = logs.Tail() });
}
