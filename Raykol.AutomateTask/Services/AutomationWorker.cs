using System.ComponentModel.DataAnnotations;
using Raykol.AutomateTask.Enums;
using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 跟踪各任务的独立执行轮次，停止时等待正在运行的任务退出
/// </summary>
public sealed class AutomationWorker(ITaskService taskService, ILogger<AutomationWorker> log) : BackgroundService
{
    private readonly DateTimeOffset boot = DateTimeOffset.Now;

    /// <summary>
    /// 每项任务最多运行一轮，耗时 CRM 请求不阻塞其他通知计划
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new Dictionary<string, Task>();
        log.LogInformation("多任务服务启动；错过首轮时间不会补跑");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var id in running.Where(item => item.Value.IsCompleted).Select(item => item.Key).ToArray())
                {
                    running.Remove(id);
                }

                foreach (var task in taskService.Tasks)
                {
                    if (!running.ContainsKey(task.Id))
                        running[task.Id] = RunTask(task, stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主停止会同时取消所有任务的请求
        }
        finally
        {
            await Task.WhenAll(running.Values);
            log.LogInformation("多任务服务停止");
        }
    }

    /// <summary>
    /// 隔离单个任务错误，实时错误只在变化时记录
    /// </summary>
    private async Task RunTask(TaskConfig task, CancellationToken ct)
    {
        try
        {
            if (task.Type == TaskType.WorkHours)
            {
                if (!taskService.TryBeginCrmOperation(task.Id))
                    return;

                try
                {
                    await TickWorkHours(task, ct);
                }
                finally
                {
                    taskService.EndCrmOperation(task.Id);
                }
            }
            else
                await TickNotification(task, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常停止，由各任务的持久化状态决定重启后的处理
        }
        catch (Exception ex)
        {
            var error = "任务准备失败：" + WorkHoursService.GetErrorMessage(ex);

            if (taskService.SetStatus(task.Id, error))
                log.LogError("[任务:{TaskId}] {TaskName}：{Reason}", task.Id, task.Name, error);
        }
    }

    /// <summary>
    /// 仅按计划生成一次持久化提醒，不需要工时实例或 CRM 会话
    /// </summary>
    private async Task TickNotification(TaskConfig task, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var config = (NotificationConfig)task.Settings;
        config.Validate();
        var state = taskService.ReadState(task.Id, date);

        if (state?.Complete == true)
            return;

        if (state == null)
        {
            var due = TaskService.Due(date, config.RunAt);

            if (!config.Enabled || now < due || boot > due)
                return;

            state = new ExecutionState
            {
                Date = date,
                Plan = config,
                Attempts = 0,
                StartedAt = now,
                RetryAt = now,
                PostAttempted = false,
                Complete = false,
                Result = ""
            };

            if (now - due >= TimeSpan.FromMinutes(1))
            {
                Finish(task, state with { Attempts = 1 }, "跳过：错过首轮触发窗口");
                return;
            }
        }

        var plan = (NotificationConfig)state.Plan;

        if (!config.Enabled)
        {
            Finish(task, state with { Attempts = Math.Max(1, state.Attempts) }, "跳过：任务已关闭");
            return;
        }

        if (state.RetryAt > now)
            return;

        if (state.Attempts >= plan.MaxAttempts)
        {
            Finish(task, state, "失败：重试已耗尽");
            return;
        }

        state = state with { Attempts = state.Attempts + 1, RetryAt = now.AddSeconds(plan.RetryIntervalSeconds) };
        taskService.SaveState(task.Id, state);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Min(110, plan.RetryIntervalSeconds - 1)));

        try
        {
            if (!await taskService.ShouldRun(plan, date, deadline.Token))
            {
                Finish(task, state, "跳过：不在所选重复日期");
                return;
            }

            if (!taskService.GetTask(task.Id).Settings.Enabled || date != DateOnly.FromDateTime(DateTime.Today))
            {
                Finish(task, state, "跳过：任务已关闭或日期已变化");
                return;
            }

            var notification = new TaskNotification
            {
                Id = $"{task.Id}:{date:yyyy-MM-dd}",
                TaskId = task.Id,
                Date = date,
                Success = true,
                Title = plan.Title,
                Message = plan.Content,
                CreatedAt = now
            };
            Finish(task, state with { Notification = notification }, "成功：提醒已生成，等待托盘接收");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = "失败阶段[判断通知重复日期及生成提醒]：" + WorkHoursService.GetErrorMessage(ex);

            Retry(task, state, reason);
        }
    }

    /// <summary>
    /// 由后台循环按本机时间顺序检查计划或重试
    /// </summary>
    private async Task TickWorkHours(TaskConfig task, CancellationToken ct)
    {
        var workHoursService = taskService.GetWorkHours(task.Id);
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.DateTime);
        var config = workHoursService.ReadConfig();
        var state = taskService.ReadState(task.Id, date);

        if (state?.Complete == true)
            return;

        if (state == null)
        {
            var due = TaskService.Due(date, config.RunAt);

            if (!config.Enabled || now < due || boot > due)
                return;

            if (now - due >= TimeSpan.FromMinutes(1))
            {
                Finish(task, new ExecutionState
                {
                    Date = date,
                    Plan = config,
                    Attempts = 1,
                    StartedAt = now,
                    RetryAt = null,
                    PostAttempted = false,
                    Complete = false,
                    Result = ""
                }, "跳过：错过首轮触发窗口");
                return;
            }

            state = new ExecutionState
            {
                Date = date,
                Plan = config,
                Attempts = 0,
                StartedAt = now,
                RetryAt = now,
                PostAttempted = false,
                Complete = false,
                Result = ""
            };
        }

        var plan = (WorkHoursConfig)state.Plan;

        // 禁用后仍允许核实已发送的请求，但绝不再发送新的保存
        if (!config.Enabled && !state.PostAttempted)
        {
            Finish(task, state with { Attempts = Math.Max(1, state.Attempts) }, "跳过：自动填报已关闭");
            return;
        }

        if (state.RetryAt > now)
            return;

        if (state.Attempts >= plan.MaxAttempts)
        {
            Finish(task, state, state.PostAttempted ? "结果不明：重试核实已耗尽，请人工检查，禁止重发" : "失败：重试已耗尽");
            return;
        }

        // 各轮以开始时间计时，若中途服务重启，仍沿用原定下一轮时间
        state = state with
        {
            Attempts = state.Attempts + 1,
            StartedAt = now,
            RetryAt = now.AddSeconds(plan.RetryIntervalSeconds)
        };
        taskService.SaveState(task.Id, state);
        using var roundDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        roundDeadline.CancelAfter(TimeSpan.FromSeconds(Math.Min(110, plan.RetryIntervalSeconds - 1)));
        var requestToken = roundDeadline.Token;
        var stage = "核实已尝试保存的结果";

        try
        {
            if (state.PostAttempted)
            {
                await Verify(task, workHoursService, state, requestToken);
                return;
            }

            stage = "判断重复日期及工作日日历";

            if (!await taskService.ShouldRun(plan, date, requestToken))
            {
                Finish(task, state, "跳过：不在所选重复日期");
                return;
            }

            stage = "核对本人身份及读取当天月表";
            var day = await workHoursService.Read(plan, date, requestToken);

            if (day.HasRecords)
            {
                Finish(task, state, "跳过：当天已有记录");
                return;
            }

            stage = "校验每日填报表单";
            var body = await workHoursService.Prepare(day, plan, date, requestToken);
            stage = "保存前复查本人身份及当天记录";
            var fresh = await workHoursService.Read(plan, date, requestToken);

            if (fresh.HasRecords || fresh.PersonId != day.PersonId || fresh.Account != day.Account)
            {
                Finish(task, state, "跳过：保存前记录或本人身份发生变化");
                return;
            }

            stage = "保存前校验配置及日期";
            requestToken.ThrowIfCancellationRequested();
            var latest = workHoursService.ReadConfig();

            if (!latest.Enabled || latest.Account != plan.Account || latest.GetCrmOrigin() != plan.GetCrmOrigin())
            {
                Finish(task, state, "跳过：保存前开关、绑定账号或 CRM 站点发生变化");
                return;
            }

            if (date != DateOnly.FromDateTime(DateTime.Today))
            {
                Finish(task, state, "跳过：已跨日期");
                return;
            }

            stage = "持久化保存意图";
            state = state with { PostAttempted = true, Result = "保存结果待核实，禁止再次 POST" };
            taskService.SaveState(task.Id, state);
            stage = "提交当天工时";
            await workHoursService.Save(day, body, date, requestToken);
            stage = "核实保存结果";
            await Verify(task, workHoursService, state, requestToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ValidationException ex) when (!state.PostAttempted)
        {
            // 配置、身份或日期校验失败不重试；已尝试保存时仍只读核实
            Finish(task, state, $"跳过[{stage}]：{WorkHoursService.GetErrorMessage(ex)}");
        }
        catch (Exception ex)
        {
            var reason = $"失败阶段[{stage}]：{WorkHoursService.GetErrorMessage(ex)}";
            Retry(task, state, state.PostAttempted ? "保存结果不明，仅查询核实；" + reason : reason);
        }
    }

    /// <summary>
    /// 用权威月表逐格验证，空白也绝不重发保存
    /// </summary>
    private async Task Verify(TaskConfig task, IWorkHoursService workHoursService, ExecutionState state, CancellationToken ct)
    {
        var plan = (WorkHoursConfig)state.Plan;
        var actual = await workHoursService.Read(plan, state.Date, ct);
        var selected = plan.GetTimeSlots().ToHashSet();
        var expected = WorkHoursConfig.TimeLabels.Select(time => selected.Contains(time) ? plan.Project : "-");

        if (actual.Account == plan.Account && actual.Projects.SequenceEqual(expected))
            Finish(task, state, $"成功：{plan.GetTimeSlots().Length / 2.0:0.#} 小时，全部时间格及项目核对通过");
        else
            Retry(task, state, "结果不明[核实保存结果]：时间格或项目不一致；只读核实，禁止重发");
    }

    /// <summary>
    /// 按首轮配置快照控制重试次数，保存请求已尝试时仅查询核实
    /// </summary>
    private void Retry(TaskConfig task, ExecutionState state, string result)
    {
        if (state.Attempts >= state.Plan.MaxAttempts)
        {
            Finish(task, state, "失败：" + result + $"；已达 {state.Plan.MaxAttempts} 轮，请人工检查");
            return;
        }

        var status = result + $"；下一轮 {state.RetryAt!.Value.ToLocalTime():MM-dd HH:mm:ss}";
        taskService.SetStatus(task.Id, status);
        taskService.SaveState(task.Id, state with { Result = status });
        log.LogWarning("[任务:{TaskId}] {TaskName}：重试 {Attempt}/{MaxRetries}：{Result}",
            task.Id, task.Name, state.Attempts, state.Plan.MaxRetries, status);
    }

    /// <summary>
    /// 持久化终态，终态不得再次触发
    /// </summary>
    private void Finish(TaskConfig task, ExecutionState state, string result)
    {
        taskService.SaveState(task.Id, state with { Complete = true, RetryAt = null, Result = result });
        taskService.SetStatus(task.Id, result);
        var level = result.StartsWith("失败", StringComparison.Ordinal) ? LogLevel.Error
            : result.StartsWith("结果不明", StringComparison.Ordinal) ? LogLevel.Warning
            : LogLevel.Information;
        log.Log(level, "[任务:{TaskId}] {TaskName}：{Result}", task.Id, task.Name, result);
    }
}
