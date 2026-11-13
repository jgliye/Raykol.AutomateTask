namespace Raykol.AutomateTask.Models;

/// <summary>
/// 本人的当前月与当天服务器记录
/// </summary>
public sealed record TimesheetDay(string Account, string PersonId, string EditPath, string[] Projects)
{
    /// <summary>
    /// 读取本次记录的源站，保存时必须保持一致
    /// </summary>
    public required Uri CrmOrigin { get; init; }

    /// <summary>
    /// 任意时间格已有数据即跳过整天
    /// </summary>
    public bool HasRecords => Projects.Any(p => p != "-");
}

/// <summary>
/// 单次 CRM 登录请求，不作持久化
/// </summary>
public sealed record CrmLoginRequest(string Username, string Password);

/// <summary>
/// 保险箱中的 Cookie 字段
/// </summary>
internal sealed record StoredCookie(string Name, string Value, string Domain, string Path, DateTime Expires);

/// <summary>
/// 将会话与协议、域名和端口绑定，避免更换站点时串用会话
/// </summary>
internal sealed record StoredSession(string Origin, List<StoredCookie> Cookies);
