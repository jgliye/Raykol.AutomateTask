namespace Raykol.AutomateTask.Models;

/// <summary>
/// 本人的当前月与当天服务器记录
/// </summary>
public sealed record TimesheetDay
{
    /// <summary>
    /// 已核实的当前 CRM 账号
    /// </summary>
    public string Account { get; init; } = null!;

    /// <summary>
    /// 本人月表对应的员工标识
    /// </summary>
    public string PersonId { get; init; } = null!;

    /// <summary>
    /// 本人当天填报页面的相对地址
    /// </summary>
    public string EditPath { get; init; } = null!;

    /// <summary>
    /// 当天各半小时时间格的项目记录
    /// </summary>
    public string[] Projects { get; init; } = null!;

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
public sealed record CrmLoginRequest
{
    /// <summary>
    /// 本人员工账号
    /// </summary>
    public string Username { get; init; } = null!;

    /// <summary>
    /// 仅用于本次登录的密码，不作持久化
    /// </summary>
    public string Password { get; init; } = null!;
}

/// <summary>
/// 保险箱中的 Cookie 字段
/// </summary>
internal sealed record StoredCookie
{
    /// <summary>
    /// Cookie 名称
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// Cookie 值
    /// </summary>
    public string Value { get; init; } = null!;

    /// <summary>
    /// Cookie 所属域名
    /// </summary>
    public string Domain { get; init; } = null!;

    /// <summary>
    /// Cookie 作用路径
    /// </summary>
    public string Path { get; init; } = null!;

    /// <summary>
    /// Cookie 过期时间
    /// </summary>
    public DateTime Expires { get; init; }
}

/// <summary>
/// 将会话与协议、域名和端口绑定，避免更换站点时串用会话
/// </summary>
internal sealed record StoredSession
{
    /// <summary>
    /// 会话所属的完整 CRM 源站
    /// </summary>
    public string Origin { get; init; } = null!;

    /// <summary>
    /// 加密保存的 CRM Cookie 集合
    /// </summary>
    public List<StoredCookie> Cookies { get; init; } = null!;
}
