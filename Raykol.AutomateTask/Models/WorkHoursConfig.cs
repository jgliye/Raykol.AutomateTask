using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 工时任务配置，整体映射到通用配置服务
/// </summary>
public sealed record WorkHoursConfig
{
    /// <summary>
    /// CRM 完整半小时时间标签，配置和解析共用
    /// </summary>
    public static readonly string[] TimeLabels = [.. Enumerable.Range(0, 27).Select(i => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(510 + i * 30)).ToString("HH:mm"))];

    /// <summary>
    /// 是否自动填报，首次发布默认关闭
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// CRM HTTPS 站点根地址，可包含端口，不包含业务路径
    /// </summary>
    public string CrmUrl { get; init; } = "https://crm.raykol.com";

    /// <summary>
    /// 首次登录后绑定的员工账号
    /// </summary>
    public string Account { get; init; } = "";

    /// <summary>
    /// 填报项目号
    /// </summary>
    public string Project { get; init; } = "RRD068-W26977";

    /// <summary>
    /// 本机时间每日执行时刻
    /// </summary>
    public string RunAt { get; init; } = "19:00";

    /// <summary>
    /// 重复方式：Weekly 自选星期，Workdays 中国法定工作日
    /// </summary>
    public string RepeatMode { get; init; } = "Workdays";

    /// <summary>
    /// 自选星期，1 为周一，7 为周日
    /// </summary>
    public int[] WeekDays { get; init; } = [1, 2, 3, 4, 5];

    /// <summary>
    /// 上午首格
    /// </summary>
    public string MorningStart { get; init; } = "09:00";

    /// <summary>
    /// 上午末格或结束时刻
    /// </summary>
    public string MorningEnd { get; init; } = "11:30";

    /// <summary>
    /// 下午首格
    /// </summary>
    public string AfternoonStart { get; init; } = "13:00";

    /// <summary>
    /// 下午末格或结束时刻
    /// </summary>
    public string AfternoonEnd { get; init; } = "18:30";

    /// <summary>
    /// 是否填写末尾标签
    /// </summary>
    public bool IncludeEnd { get; init; } = true;

    /// <summary>
    /// 单个网络请求超时秒数
    /// </summary>
    public int TimeoutSeconds { get; init; } = 20;

    /// <summary>
    /// 失败后各轮开始时间的间隔秒数
    /// </summary>
    public int RetryIntervalSeconds { get; init; } = 120;

    /// <summary>
    /// 首次执行失败后允许的重试次数，0 表示不重试
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// 首轮和重试合计的最大执行轮数
    /// </summary>
    [JsonIgnore]
    public int MaxAttempts => MaxRetries + 1;

    /// <summary>
    /// 校验配置并返回全部工作格
    /// </summary>
    public string[] GetTimeSlots()
    {
        GetCrmOrigin();

        if (RepeatMode is not ("Weekly" or "Workdays") || WeekDays == null ||
            WeekDays.Any(d => d is < 1 or > 7) || WeekDays.Distinct().Count() != WeekDays.Length ||
            (RepeatMode == "Weekly" && WeekDays.Length == 0))
            throw new ValidationException("请选择有效重复方式，自选星期至少勾选一天（周一至周日）");

        if (!Regex.IsMatch(Project ?? "", @"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") ||
            !(Account == "" || IsValidAccount(Account)))
            throw new ValidationException("项目号或员工账号无效");

        if (!TimeOnly.TryParseExact(RunAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            TimeoutSeconds is < 5 or > 90 ||
            RetryIntervalSeconds is < 10 or > 86400 || MaxRetries is < 0 or > 20)
            throw new ValidationException("执行时间、超时或重试参数无效（重试间隔 10–86400 秒，次数 0–20）");

        var endpoints = new[] { MorningStart, MorningEnd, AfternoonStart, AfternoonEnd };

        if (endpoints.Any(t => !TimeLabels.Contains(t)) ||
            string.CompareOrdinal(MorningStart, MorningEnd) > 0 ||
            string.CompareOrdinal(MorningEnd, AfternoonStart) >= 0 ||
            string.CompareOrdinal(AfternoonStart, AfternoonEnd) > 0)
            throw new ValidationException("首尾必须是有序且不重叠的半小时标签");

        var slots = TimeLabels.Where(t => t is not "12:00" and not "12:30" &&
            (Within(t, MorningStart, MorningEnd) || Within(t, AfternoonStart, AfternoonEnd))).ToArray();

        if (slots.Length == 0)
            throw new ValidationException("至少选择一个工作格");

        return slots;
    }

    /// <summary>
    /// 验证并规范化 CRM 源站，禁止 URL 内嵌凭据、路径或额外参数
    /// </summary>
    public Uri GetCrmOrigin()
    {
        if (!Uri.TryCreate(CrmUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "")
            throw new ValidationException("CRM 地址必须为 HTTPS 站点根地址，可包含端口，不能包含账号密码、路径、查询参数或片段");

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>
    /// 判断标签是否在所选区间
    /// </summary>
    private bool Within(string t, string start, string end) =>
        string.CompareOrdinal(t, start) >= 0 && string.CompareOrdinal(t, end) < (IncludeEnd ? 1 : 0);

    /// <summary>
    /// 配置校验和 CRM 登录共用员工账号格式
    /// </summary>
    public static bool IsValidAccount(string? account) => account != null && Regex.IsMatch(account, @"^RK\d+$");
}
