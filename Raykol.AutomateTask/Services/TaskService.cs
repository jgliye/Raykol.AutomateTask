using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Raykol.AutomateTask.Enums;
using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 管理独立任务记录、CRM 绑定、执行计划和统一日志
/// </summary>
public sealed class TaskService(IBaseConfigService<TaskConfig> taskConfigService, ILogger<TaskService> log)
    : ITaskService, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, WorkHoursService> workHours = [];
    private readonly Dictionary<string, string> pendingAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> crmBusy = [];
    private readonly ConcurrentDictionary<string, string> statuses = new();
    private readonly HttpClient calendar = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 4 * 1024 * 1024
    };

    /// <summary>
    /// 直接读取各任务记录，不再维护全局任务目录
    /// </summary>
    public IReadOnlyList<TaskConfig> Tasks => taskConfigService.GetList(BaseConfigType.ExecutionConfig);

    /// <summary>
    /// 按任务标识读取唯一配置记录
    /// </summary>
    public TaskConfig GetTask(string id)
    {
        var task = taskConfigService.GetByKey(id, BaseConfigType.ExecutionConfig)
            ?? throw new ValidationException("任务不存在，请刷新列表");

        if (task.Id != id)
            throw new InvalidDataException("任务标识与配置键不一致");

        task.Validate();
        return task;
    }

    /// <summary>
    /// 工时任务独占自己的网络连接，存储仍复用同一条任务记录
    /// </summary>
    public IWorkHoursService GetWorkHours(string id)
    {
        if (GetTask(id).Type != TaskType.WorkHours)
            throw new ValidationException("该任务无需 CRM 登录");

        lock (gate)
        {
            if (!workHours.TryGetValue(id, out var workHoursService))
            {
                workHoursService = new WorkHoursService(taskConfigService, id);
                workHours.Add(id, workHoursService);
            }

            return workHoursService;
        }
    }

    /// <summary>
    /// 每项工时任务独占自己的会话轮次，不阻塞其他任务
    /// </summary>
    public bool TryBeginCrmOperation(string id)
    {
        lock (gate)
        {
            return crmBusy.Add(id);
        }
    }

    /// <summary>
    /// 操作完成或取消后释放该项会话标记
    /// </summary>
    public void EndCrmOperation(string id)
    {
        lock (gate)
        {
            crmBusy.Remove(id);
        }
    }

    /// <summary>
    /// 返回管理页所需数据，避免向客户端暴露加密会话和执行快照
    /// </summary>
    public IReadOnlyList<TaskOverview> GetOverview()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTimeOffset.Now;
        var result = new List<TaskOverview>();

        foreach (var task in Tasks)
        {
            try
            {
                task.Validate();
                var config = task.Settings;
                var state = task.LastExecution?.Date == today ? task.LastExecution : null;
                result.Add(new TaskOverview
                {
                    Id = task.Id,
                    Name = task.Name,
                    Type = task.Type,
                    Settings = config,
                    NextRun = state is { Complete: false, RetryAt: not null } ? state.RetryAt
                        : config.Enabled ? Next(now, config, state?.Complete == true) : null,
                    Result = state?.Result ?? GetStatus(task.Id),
                    Attempt = state?.Attempts ?? 0,
                    MaxAttempts = state?.Plan.MaxAttempts ?? config.MaxAttempts,
                    Crm = task.Type == TaskType.WorkHours ? GetWorkHours(task.Id).Crm : null
                });
            }
            catch (Exception ex)
            {
                result.Add(new TaskOverview
                {
                    Id = task.Id,
                    Name = task.Name,
                    Type = task.Type,
                    Settings = null,
                    NextRun = null,
                    Result = "配置或状态读取失败：" + WorkHoursService.GetErrorMessage(ex),
                    Attempt = 0,
                    MaxAttempts = 0,
                    Crm = null
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 创建默认关闭的任务，仅新增一条完整记录
    /// </summary>
    public TaskConfig Create(TaskCreateRequest request)
    {
        lock (gate)
        {
            ValidateName(request.Name);

            if (Tasks.Count >= 200 || request.Type is not (TaskType.WorkHours or TaskType.Notification))
                throw new ValidationException("任务类型无效或已达到 200 个任务上限");

            TaskSchedule settings = request.Type == TaskType.WorkHours ? new WorkHoursConfig() : new NotificationConfig
            {
                Title = request.Name.Trim(),
                Content = request.Name.Trim(),
                RunAt = "12:00",
                RepeatMode = "Weekly"
            };
            var task = new TaskConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = request.Name.Trim(),
                Type = request.Type,
                Settings = settings
            };
            task.Validate();
            taskConfigService.SaveByKey(task.Id, task, BaseConfigType.ExecutionConfig);
            log.LogInformation("[任务:{TaskId}] {TaskName}：任务已创建，默认关闭", task.Id, task.Name);
            return task;
        }
    }

    /// <summary>
    /// 只合并可编辑配置，最新会话及执行状态保持原值
    /// </summary>
    public void Save(string id, TaskEditRequest request)
    {
        lock (gate)
        {
            var task = GetTask(id);

            if (pendingAccounts.ContainsValue(id))
                throw new ValidationException("该任务正在登录，请完成后再修改配置");

            ValidateName(request.Name, id);
            var changedOrigin = false;
            taskConfigService.UpdateByKey(task.Id, current =>
            {
                TaskSchedule settings = request.Settings;

                if (current.Type == TaskType.WorkHours && settings is WorkHoursConfig config)
                {
                    var saved = (WorkHoursConfig)current.Settings;
                    changedOrigin = saved.GetCrmOrigin() != config.GetCrmOrigin();

                    if (changedOrigin && crmBusy.Contains(id))
                        throw new ValidationException("该任务正在填报或检查 CRM，请完成后再更换站点");

                    settings = config with { Account = changedOrigin ? "" : saved.Account };
                }
                else if (current.Type != TaskType.Notification || settings is not NotificationConfig)
                {
                    throw new ValidationException("配置与任务类型不匹配");
                }

                var updated = current with
                {
                    Name = request.Name.Trim(),
                    Settings = settings,
                    CrmSession = changedOrigin ? null : current.CrmSession
                };
                updated.Validate();
                return updated;
            }, BaseConfigType.ExecutionConfig);

            if (changedOrigin && workHours.Remove(id, out var workHoursService))
                workHoursService.Dispose();

            log.LogInformation("[任务:{TaskId}] {TaskName}：配置已保存", id, request.Name.Trim());
        }
    }

    /// <summary>
    /// 切换开关时保留任务的其他已保存设置
    /// </summary>
    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            var task = GetTask(id);
            Save(id, new TaskEditRequest
            {
                Name = task.Name,
                Settings = task.Settings with { Enabled = enabled }
            });
        }
    }

    /// <summary>
    /// 预留账号后登录，防止同一账号绑定到多个任务
    /// </summary>
    public async Task<CrmSessionStatus> Login(string id, CrmLoginRequest request, CancellationToken ct)
    {
        var task = GetTask(id);
        IWorkHoursService workHoursService;
        var account = request.Username?.Trim().ToUpperInvariant() ?? "";

        if (!WorkHoursConfig.IsValidAccount(account))
            throw new ValidationException("请输入有效的 RK 员工账号");

        lock (gate)
        {
            var bound = Tasks.FirstOrDefault(other => other.Id != id &&
                other.Settings is WorkHoursConfig config &&
                string.Equals(config.Account, account, StringComparison.OrdinalIgnoreCase));

            if (bound != null)
                throw new ValidationException($"该 CRM 账号已绑定任务「{bound.Name}」，请先退出该任务的 CRM 会话");

            if (pendingAccounts.ContainsKey(account) || crmBusy.Contains(id))
                throw new ValidationException("该账号或任务正在进行 CRM 操作，请稍后重试");

            workHoursService = GetWorkHours(id);
            crmBusy.Add(id);
            pendingAccounts.Add(account, id);
        }

        try
        {
            var config = workHoursService.ReadConfig();
            var verified = await workHoursService.Login(account, request.Password ?? "", config, ct);
            taskConfigService.UpdateByKey(task.Id, current =>
            {
                var settings = (WorkHoursConfig)current.Settings;

                if (settings.GetCrmOrigin() != config.GetCrmOrigin())
                    throw new ValidationException("登录期间 CRM 地址已变更，请重新登录");

                return current with { Settings = settings with { Account = verified } };
            }, BaseConfigType.ExecutionConfig);
            log.LogInformation("[任务:{TaskId}] {TaskName}：CRM 登录及本人身份核对成功", id, task.Name);
            return workHoursService.Crm;
        }
        catch (Exception ex)
        {
            log.LogWarning("[任务:{TaskId}] {TaskName}：CRM 登录失败：{Reason}",
                id, task.Name, WorkHoursService.GetErrorMessage(ex));
            workHoursService.Logout();
            throw;
        }
        finally
        {
            lock (gate)
            {
                pendingAccounts.Remove(account);
                crmBusy.Remove(id);
            }
        }
    }

    /// <summary>
    /// 一次更新清空会话和账号绑定，执行意图继续保留
    /// </summary>
    public void Logout(string id)
    {
        lock (gate)
        {
            if (crmBusy.Contains(id))
                throw new ValidationException("该任务正在填报或检查 CRM，请完成后再退出");

            var task = GetTask(id);

            if (task.Settings is not WorkHoursConfig)
                throw new ValidationException("该任务无需 CRM 登录");

            taskConfigService.UpdateByKey(task.Id, current => current with
            {
                CrmSession = null,
                Settings = ((WorkHoursConfig)current.Settings) with { Account = "" }
            }, BaseConfigType.ExecutionConfig);

            if (workHours.Remove(id, out var workHoursService))
                workHoursService.Dispose();

            log.LogInformation("[任务:{TaskId}] {TaskName}：已退出 CRM 会话并释放账号绑定", id, task.Name);
        }
    }

    /// <summary>
    /// 实际检查会话，状态变化时记录一条简洁日志
    /// </summary>
    public async Task<CrmSessionStatus> CheckCrm(string id, CancellationToken ct)
    {
        var task = GetTask(id);
        IWorkHoursService workHoursService;

        lock (gate)
        {
            workHoursService = GetWorkHours(id);

            if (!crmBusy.Add(id))
                return workHoursService.Crm;
        }

        try
        {
            var old = workHoursService.Crm.State;
            var state = await workHoursService.CheckSession(ct);

            if (old != state.State)
                log.Log(state.State == "LoggedIn" ? LogLevel.Information : LogLevel.Warning,
                    "[任务:{TaskId}] {TaskName}：CRM {State}：{Reason}", id, task.Name, state.State, state.Message);

            return state;
        }
        finally
        {
            EndCrmOperation(id);
        }
    }

    /// <summary>
    /// 获取准备阶段的实时提示，不建立历史内存队列
    /// </summary>
    public string GetStatus(string id) => statuses.TryGetValue(id, out var result) ? result : "等待计划时间";

    /// <summary>
    /// 更新实时提示，供调度失败日志去重
    /// </summary>
    public bool SetStatus(string id, string result)
    {
        var changed = GetStatus(id) != result;
        statuses[id] = result;
        return changed;
    }

    /// <summary>
    /// 读取指定日期的最近一次执行状态，不创建新的每日记录
    /// </summary>
    public ExecutionState? ReadState(string id, DateOnly date)
    {
        var state = GetTask(id).LastExecution;

        if (state?.Date > date)
            throw new InvalidDataException("当前日期早于最近执行日期，已停止执行");

        return state?.Date == date ? state : null;
    }

    /// <summary>
    /// 只更新运行部分，保存配置及会话的并发更新不会丢失
    /// </summary>
    public void SaveState(string id, ExecutionState state)
    {
        taskConfigService.UpdateByKey(id, current =>
        {
            var previous = current.LastExecution;

            if (previous?.Date > state.Date)
                throw new InvalidDataException("禁止用过期轮次覆盖最近执行状态");

            if (previous?.Date == state.Date &&
                ((previous.Complete && !state.Complete) || (previous.PostAttempted && !state.PostAttempted)))
                throw new InvalidDataException("禁止覆盖已保存的执行终态或提交标记");

            if (previous?.Notification?.Id != null && previous.Notification.Id == state.Notification?.Id && previous.Delivered)
                state = state with { Delivered = true };

            var updated = current with { LastExecution = state };
            updated.Validate();
            return updated;
        }, BaseConfigType.ExecutionConfig);
    }

    /// <summary>
    /// 按触发时间返回当前用户尚未确认的当天通知
    /// </summary>
    public TaskNotification? GetNotification(IReadOnlySet<string> receipts)
    {
        var date = DateOnly.FromDateTime(DateTime.Today);
        var pending = new List<TaskNotification>();

        foreach (var task in Tasks)
        {
            try
            {
                task.Validate();
                var state = task.LastExecution?.Date == date ? task.LastExecution : null;
                TaskNotification? notification = state?.Notification;

                if (task.Type == TaskType.WorkHours && state?.Complete == true &&
                    (state.Result.StartsWith("成功", StringComparison.Ordinal) ||
                        state.Result.StartsWith("失败", StringComparison.Ordinal) || state.Result.StartsWith("结果不明", StringComparison.Ordinal)))
                {
                    var success = state.Result.StartsWith("成功", StringComparison.Ordinal);
                    notification = new TaskNotification
                    {
                        Id = $"{task.Id}:{date:yyyy-MM-dd}:{success}",
                        TaskId = task.Id,
                        Date = date,
                        Success = success,
                        Title = task.Name,
                        Message = success ? "工时填报成功，已核对通过" : "工时填报失败或结果不明，请打开任务查看日志",
                        CreatedAt = state.StartedAt
                    };
                }

                if (notification != null && !receipts.Contains(notification.Id))
                    pending.Add(notification);
            }
            catch (Exception ex)
            {
                var error = "通知状态读取失败：" + WorkHoursService.GetErrorMessage(ex);

                if (SetStatus(task.Id, error))
                    log.LogWarning("[任务:{TaskId}] {TaskName}：{Reason}", task.Id, task.Name, error);
            }
        }

        return pending.OrderBy(item => item.CreatedAt).FirstOrDefault();
    }

    /// <summary>
    /// 合并首个托盘回执，其他用户仍按自己的回执接收
    /// </summary>
    public void Acknowledge(TaskNotification notification)
    {
        var task = GetTask(notification.TaskId);

        if (task.Type != TaskType.Notification)
            return;

        var delivered = false;
        taskConfigService.UpdateByKey(task.Id, current =>
        {
            if (current.LastExecution is not { } state || state.Notification?.Id != notification.Id || state.Delivered)
                return current;

            delivered = true;
            return current with { LastExecution = state with { Delivered = true, Result = "成功：通知已送达托盘" } };
        }, BaseConfigType.ExecutionConfig);

        if (delivered)
            log.LogInformation("[任务:{TaskId}] {TaskName}：通知已送达托盘", task.Id, task.Name);
    }

    /// <summary>
    /// 校验单行任务名称，避免列表混淆及日志换行
    /// </summary>
    private void ValidateName(string name, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 || name.Any(char.IsControl) ||
            Tasks.Any(task => task.Id != id && task.Name == name.Trim()))
            throw new ValidationException("任务名称须为 1–40 字、不能含控制字符，且不能重复");
    }

    /// <summary>
    /// 从最近两个日志文件读取末尾 200 行，允许同时写入和清理
    /// </summary>
    public string ReadLogs(string? id = null, string? level = null)
    {
        if (id != null)
            GetTask(id);

        if (level is not (null or "INFO" or "WARN" or "ERROR"))
            throw new ValidationException("日志级别无效");

        var directory = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "Logs"));

        if (!directory.Exists)
            return "暂无日志";

        var files = directory.EnumerateFiles("*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(2)
            .Reverse();
        var lines = new Queue<string>(200);

        foreach (var file in files)
        {
            FileStream stream;

            try
            {
                stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                // 文件可能刚被 NLog 保留策略清理，继续读取其他文件
                continue;
            }

            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if ((id != null && !line.Contains($"[任务:{id}]", StringComparison.Ordinal)) ||
                    (level != null && !line.Contains($"|{level}|", StringComparison.Ordinal)))
                    continue;

                if (lines.Count == 200)
                    lines.Dequeue();

                lines.Enqueue(line);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 判断重复日期，仅工作日模式请求并交叉核对年度与每日节假日历
    /// </summary>
    public async Task<bool> ShouldRun(TaskSchedule config, DateOnly date, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (config.RepeatMode == "Weekly")
            return config.WeekDays.Contains(WeekDay(date));

        if (config.RepeatMode != "Workdays")
            throw new InvalidDataException("重复方式无效");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        using var annualDocument = JsonDocument.Parse(await calendar.GetStringAsync(
            $"https://timor.tech/api/holiday/year/{date.Year}/", deadline.Token));
        using var dailyDocument = JsonDocument.Parse(await calendar.GetStringAsync(
            $"https://timor.tech/api/holiday/info/{date:yyyy-MM-dd}", deadline.Token));
        var annual = annualDocument.RootElement;
        var daily = dailyDocument.RootElement;

        try
        {
            var holidays = annual.GetProperty("holiday");
            var entries = holidays.EnumerateObject().ToArray();

            if (annual.GetProperty("code").GetInt32() != 0 || entries.Length < 10 ||
                holidays.GetProperty("01-01").GetProperty("date").GetString() != $"{date.Year}-01-01" ||
                holidays.GetProperty("10-01").GetProperty("date").GetString() != $"{date.Year}-10-01" ||
                entries.Any(e => !DateOnly.TryParseExact(e.Value.GetProperty("date").GetString(), "yyyy-MM-dd", out var d) ||
                    d.Year != date.Year || e.Name != d.ToString("MM-dd") ||
                    e.Value.GetProperty("holiday").ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                throw new InvalidDataException("年度节假日历不完整");

            var weekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            var hasException = holidays.TryGetProperty(date.ToString("MM-dd"), out var exception);
            var expected = hasException ? (exception.GetProperty("holiday").GetBoolean() ? 2 : 3) : (weekday > 5 ? 1 : 0);
            var type = daily.GetProperty("type");

            if (daily.GetProperty("code").GetInt32() != 0 || type.GetProperty("type").GetInt32() != expected ||
                type.GetProperty("week").GetInt32() != weekday ||
                (hasException && daily.GetProperty("holiday").GetProperty("holiday").GetBoolean() != exception.GetProperty("holiday").GetBoolean()))
                throw new InvalidDataException("年度和每日节假日历不一致");

            return expected is 0 or 3;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("节假日历接口数据格式异常");
        }
    }

    /// <summary>
    /// 转换为配置使用的周一至周日编号
    /// </summary>
    public static int WeekDay(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    /// <summary>
    /// 按系统本地时区计算某日的计划时刻
    /// </summary>
    public static DateTimeOffset Due(DateOnly date, string time) =>
        new(date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Local));

    /// <summary>
    /// 计算下一次首轮时刻，不补跑错过的任务
    /// </summary>
    public static DateTimeOffset Next(DateTimeOffset now, TaskSchedule config, bool skipToday = false)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        config.ValidateSchedule();

        for (var offset = skipToday ? 1 : 0; offset <= 7; offset++)
        {
            var candidate = date.AddDays(offset);
            var due = Due(candidate, config.RunAt);

            if (due > now && (config.RepeatMode == "Workdays" || config.WeekDays.Contains(WeekDay(candidate))))
                return due;
        }

        throw new InvalidDataException("没有可执行的重复日期");
    }

    /// <summary>
    /// 宿主停止后释放各任务独立的网络连接
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var workHoursService in workHours.Values)
            {
                workHoursService.Dispose();
            }

            workHours.Clear();
        }

        calendar.Dispose();
    }
}
