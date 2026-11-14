using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 工时任务配置，整体映射到通用配置服务
/// </summary>
public sealed record WorkHoursConfig : TaskSchedule
{
    /// <summary>
    /// CRM 完整半小时时间标签，配置和解析共用
    /// </summary>
    public static readonly string[] TimeLabels = [.. Enumerable.Range(0, 27).Select(i => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(510 + i * 30)).ToString("HH:mm"))];

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
    /// 校验配置并返回全部工作格
    /// </summary>
    public string[] GetTimeSlots()
    {
        GetCrmOrigin();
        ValidateSchedule();

        if (!Regex.IsMatch(Project ?? "", @"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") ||
            !(Account == "" || IsValidAccount(Account)))
            throw new ValidationException("项目号或员工账号无效");

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
