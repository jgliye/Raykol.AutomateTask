using System.ComponentModel.DataAnnotations;
using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 本机时间定时任务，持久化意图先于 POST，未知结果只核实不重发
/// </summary>
public sealed class WorkHoursWorker(
    IBaseConfigService<ExecutionState> states, IWorkHoursService workHours,
    ILogger<WorkHoursWorker> log) : BackgroundService
{
    private readonly DateTimeOffset boot = DateTimeOffset.Now;

    /// <summary>
    /// 上次受控运行结果
    /// </summary>
    public string Status { get; private set; } = "等待计划时间";

    /// <summary>
    /// 从通用配置条目读取每日状态，损坏时拒绝重置执行意图
    /// </summary>
    public ExecutionState? ReadState(DateOnly date)
    {
        var state = states.GetByKey($"{nameof(ExecutionState)}:{date:yyyy-MM-dd}");

        if (state != null)
        {
            state.Plan.GetTimeSlots();

            if (state.Date != date || state.Attempts < 1 || state.Attempts > state.Plan.MaxAttempts)
                throw new InvalidDataException("执行状态无效");
        }

        return state;
    }

    /// <summary>
    /// 按日期保存完整执行快照，发送 POST 前必须完成持久化
    /// </summary>
    private void SaveState(ExecutionState state) => states.SaveByKey($"{nameof(ExecutionState)}:{state.Date:yyyy-MM-dd}", state);

    /// <summary>
    /// 定时轮询，不在启动时补跑
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("服务启动；错过首轮时间不会补跑");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Tick(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var error = $"任务准备失败：{WorkHoursService.GetErrorMessage(ex)}；已停止本轮";

                    if (Status != error)
                    {
                        Status = error;
                        log.LogError("{Result}", Status);
                    }

                    // 存储异常不打印原始异常，下一次轮询仍会先读取持久化状态
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常服务停止，由持久化状态决定重启后是否只读核实
        }
        finally
        {
            log.LogInformation("服务停止");
        }
    }

    /// <summary>
    /// 由后台循环按本机时间顺序检查计划或重试
    /// </summary>
    public async Task Tick(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.DateTime);
        var config = workHours.ReadConfig();
        var state = ReadState(date);

        if (state?.Complete == true)
            return;

        if (state == null)
        {
            var due = WorkHoursService.Due(date, config.RunAt);

            if (!config.Enabled || now < due || boot > due)
                return;

            if (now - due >= TimeSpan.FromMinutes(1))
            {
                Finish(new(date, config, 1, now, null, false, false, ""), "跳过：错过首轮触发窗口");
                return;
            }

            state = new(date, config, 0, now, now, false, false, "");
        }

        // 禁用后仍允许核实已发送的请求，但绝不再发送新的保存
        if (!config.Enabled && !state.PostAttempted)
        {
            Finish(state with { Attempts = Math.Max(1, state.Attempts) }, "跳过：自动填报已关闭");
            return;
        }

        if (state.RetryAt > now)
            return;

        if (state.Attempts >= state.Plan.MaxAttempts)
        {
            Finish(state, state.PostAttempted ? "结果不明：重试核实已耗尽，请人工检查，禁止重发" : "失败：重试已耗尽");
            return;
        }

        // 各轮以开始时间计时，若中途服务重启，仍沿用原定下一轮时间
        state = state with
        {
            Attempts = state.Attempts + 1,
            StartedAt = now,
            RetryAt = now.AddSeconds(state.Plan.RetryIntervalSeconds)
        };
        SaveState(state);
        using var roundDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        roundDeadline.CancelAfter(TimeSpan.FromSeconds(Math.Min(110, state.Plan.RetryIntervalSeconds - 1)));
        var requestToken = roundDeadline.Token;
        var stage = "核实已尝试保存的结果";

        try
        {
            if (state.PostAttempted)
            {
                await Verify(state, requestToken);
                return;
            }

            stage = "判断重复日期及工作日日历";

            if (!await workHours.ShouldRun(state.Plan, date, requestToken))
            {
                Finish(state, "跳过：不在所选重复日期");
                return;
            }

            stage = "核对本人身份及读取当天月表";
            var day = await workHours.Read(state.Plan, date, requestToken);

            if (day.HasRecords)
            {
                Finish(state, "跳过：当天已有记录");
                return;
            }

            stage = "校验每日填报表单";
            var body = await workHours.Prepare(day, state.Plan, date, requestToken);
            stage = "保存前复查本人身份及当天记录";
            var fresh = await workHours.Read(state.Plan, date, requestToken);

            if (fresh.HasRecords || fresh.PersonId != day.PersonId || fresh.Account != day.Account)
            {
                Finish(state, "跳过：保存前记录或本人身份发生变化");
                return;
            }

            stage = "保存前校验配置及日期";
            requestToken.ThrowIfCancellationRequested();
            var latest = workHours.ReadConfig();

            if (!latest.Enabled || latest.Account != state.Plan.Account || latest.GetCrmOrigin() != state.Plan.GetCrmOrigin())
            {
                Finish(state, "跳过：保存前开关、绑定账号或 CRM 站点发生变化");
                return;
            }

            if (date != DateOnly.FromDateTime(DateTime.Today))
            {
                Finish(state, "跳过：已跨日期");
                return;
            }

            stage = "持久化保存意图";
            state = state with { PostAttempted = true, Result = "保存结果待核实，禁止再次 POST" };
            SaveState(state);
            stage = "提交当天工时";
            await workHours.Save(day, body, date, requestToken);
            stage = "核实保存结果";
            await Verify(state, requestToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ValidationException ex) when (!state.PostAttempted)
        {
            // 配置、身份或日期校验失败不重试；已尝试保存时仍只读核实
            Finish(state, $"跳过[{stage}]：{WorkHoursService.GetErrorMessage(ex)}");
        }
        catch (Exception ex)
        {
            var reason = $"失败阶段[{stage}]：{WorkHoursService.GetErrorMessage(ex)}";
            Retry(state, state.PostAttempted ? "保存结果不明，仅查询核实；" + reason : reason);
        }
    }

    /// <summary>
    /// 用权威月表逐格验证，空白也绝不重发保存
    /// </summary>
    private async Task Verify(ExecutionState state, CancellationToken ct)
    {
        var actual = await workHours.Read(state.Plan, state.Date, ct);
        var selected = state.Plan.GetTimeSlots().ToHashSet();
        var expected = WorkHoursConfig.TimeLabels.Select(time => selected.Contains(time) ? state.Plan.Project : "-");

        if (actual.Account == state.Plan.Account && actual.Projects.SequenceEqual(expected))
            Finish(state, $"成功：{state.Plan.GetTimeSlots().Length / 2.0:0.#} 小时，全部时间格及项目核对通过");
        else
            Retry(state, "结果不明[核实保存结果]：时间格或项目不一致；只读核实，禁止重发");
    }

    /// <summary>
    /// 按首轮配置快照控制重试次数，保存请求已尝试时仅查询核实
    /// </summary>
    private void Retry(ExecutionState state, string result)
    {
        if (state.Attempts >= state.Plan.MaxAttempts)
        {
            Finish(state, "失败：" + result + $"；已达 {state.Plan.MaxAttempts} 轮，请人工检查");
            return;
        }

        Status = result + $"；下一轮 {state.RetryAt!.Value.ToLocalTime():MM-dd HH:mm:ss}";
        SaveState(state with { Result = Status });
        log.LogWarning("重试 {Attempt}/{MaxRetries}：{Result}", state.Attempts, state.Plan.MaxRetries, Status);
    }

    /// <summary>
    /// 持久化终态，终态不得再次触发
    /// </summary>
    private void Finish(ExecutionState state, string result)
    {
        SaveState(state with { Complete = true, RetryAt = null, Result = result });
        Status = result;
        var level = result.StartsWith("失败", StringComparison.Ordinal) ? LogLevel.Error
            : result.StartsWith("结果不明", StringComparison.Ordinal) ? LogLevel.Warning
            : LogLevel.Information;
        log.Log(level, "{Result}", result);
    }
}
