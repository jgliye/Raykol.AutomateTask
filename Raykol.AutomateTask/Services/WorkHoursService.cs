using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Services;

/// <summary>
/// 集中处理工时请求、CRM 页面解析、会话、重复日期判断和日志读取
/// </summary>
public sealed class WorkHoursService : IWorkHoursService, IDisposable
{
    private static readonly string[] AllowedPaths = ["/login", "/index", "/oa/hour/", "/oa/hour/desc", "/oa/hour/edit"];
    private readonly CookieContainer cookies = new();
    private readonly HttpClient http;
    private readonly IBaseConfigService<WorkHoursConfig> configs;
    private readonly IBaseConfigService<string> sessions;
    private readonly HttpClient calendar;
    private Uri? activeOrigin;
    private string? pendingSession;

    /// <summary>
    /// 创建独立的 CRM 与日历连接，禁用自动重定向和日历 Cookie
    /// </summary>
    public WorkHoursService(IBaseConfigService<WorkHoursConfig> configs, IBaseConfigService<string> sessions,
        HttpMessageHandler? transport = null, HttpMessageHandler? calendarTransport = null)
    {
        this.configs = configs;
        this.sessions = sessions;
        var handler = transport ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = cookies,
            UseCookies = true
        };

        http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 4 * 1024 * 1024
        };
        calendar = new HttpClient(calendarTransport ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 4 * 1024 * 1024
        };
        var encrypted = sessions.GetByKey("WorkHoursSession");
        pendingSession = encrypted == null ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(encrypted), null, DataProtectionScope.LocalMachine));
    }

    /// <summary>
    /// 通过通用配置服务读取工时配置，首次缺失时使用模型默认值初始化
    /// </summary>
    public WorkHoursConfig ReadConfig()
    {
        var config = configs.GetByKey();

        if (config == null)
        {
            config = new WorkHoursConfig();
            SaveConfig(config);
        }

        config.GetTimeSlots();
        return config;
    }

    /// <summary>
    /// 验证后整体保存工时配置，不改写 appsettings 文件
    /// </summary>
    public void SaveConfig(WorkHoursConfig config)
    {
        config.GetTimeSlots();
        configs.Save(config);
    }

    /// <summary>
    /// 返回用于日志的受控失败原因，不输出凭据、请求正文或原始异常
    /// </summary>
    public static string GetErrorMessage(Exception error) => error switch
    {
        ValidationException or InvalidDataException => error.Message,
        HttpRequestException http when http.StatusCode is { } code => $"HTTP {(int)code}（{http.HttpRequestError}）",
        HttpRequestException http => $"网络请求失败（{http.HttpRequestError}）",
        OperationCanceledException => "请求已取消或执行超时",
        TimeoutException => "操作超时",
        JsonException => "响应或配置 JSON 格式错误",
        CryptographicException => "CRM 会话解密失败，请重新登录",
        UnauthorizedAccessException => "文件或数据库访问权限不足",
        IOException => $"文件读写失败（{error.GetType().Name}）",
        _ => $"程序异常（{error.GetType().Name}）"
    };

    /// <summary>
    /// 从最近两个日志文件读取末尾 200 行，允许同时写入和清理
    /// </summary>
    public string ReadLogs()
    {
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
                if (lines.Count == 200)
                    lines.Dequeue();

                lines.Enqueue(line);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 切换源站时清空 Cookie，只恢复与完整源站匹配的加密会话
    /// </summary>
    private void SelectOrigin(Uri origin)
    {
        origin = new WorkHoursConfig { CrmUrl = origin.AbsoluteUri }.GetCrmOrigin();

        if (activeOrigin == origin)
            return;

        foreach (Cookie cookie in cookies.GetAllCookies())
        {
            cookie.Expired = true;
        }

        activeOrigin = origin;

        if (pendingSession is not { } stored)
            return;

        pendingSession = null;
        var session = SerializeHelper.Deserialize<StoredSession>(stored);

        if (session == null || !Uri.TryCreate(session.Origin, UriKind.Absolute, out var savedOrigin) || savedOrigin != origin)
            return;

        foreach (var cookie in session.Cookies)
        {
            if (!string.Equals(cookie.Domain.TrimStart('.'), origin.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("持久化会话域名错误");

            cookies.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
            {
                Secure = true,
                HttpOnly = true,
                Expires = cookie.Expires
            });
        }
    }

    /// <summary>
    /// 向已验证地址发起单次请求，不输出响应正文
    /// </summary>
    private async Task<string> Request(string path, int timeout, CancellationToken ct, List<KeyValuePair<string, string>>? body = null)
    {
        var origin = activeOrigin ?? throw new InvalidDataException("尚未选择 CRM 站点");
        var uri = new Uri(origin, path);

        if (!AllowedPaths.Contains(uri.AbsolutePath))
            throw new InvalidDataException("禁止访问该 CRM 接口");

        Target(origin, uri.AbsoluteUri, uri.AbsolutePath);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri);

        if (body != null)
        {
            request.Content = new FormUrlEncodedContent(body);
            request.Headers.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = uri;
        }

        using var response = await http.SendAsync(request, deadline.Token);
        Persist();

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            var next = response.Headers.Location ?? throw new InvalidDataException("CRM 重定向地址缺失");
            var target = new Uri(uri, next);
            Target(origin, target.AbsoluteUri, target.AbsolutePath);

            // 保存响应无论重定向到何处都不重发，交给月表核实
            if (body != null)
                return "";

            if (target.AbsolutePath == "/login")
                throw new ValidationException("CRM 登录已过期");

            throw new InvalidDataException("CRM 返回未预期重定向");
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"CRM 返回 HTTP {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync(deadline.Token);
    }

    /// <summary>
    /// 使用仅保留于本次请求内存的密码重新登录
    /// </summary>
    public async Task<string> Login(string username, string password, WorkHoursConfig config, CancellationToken ct)
    {
        username = username.ToUpperInvariant();

        if (!WorkHoursConfig.IsValidAccount(username) || password.Length is < 1 or > 256 ||
            (config.Account != "" && config.Account != username))
            throw new InvalidDataException("请输入与绑定账号一致的 RK 员工账号和密码");

        Logout();
        var origin = config.GetCrmOrigin();
        SelectOrigin(origin);
        var html = await Request("/login", config.TimeoutSeconds, ct);
        var doc = Parse(html);
        var forms = doc.Descendants("form")
            .Where(f => f.Descendants("input").Any(i => Attribute(i, "name") == "username") &&
                f.Descendants("input").Any(i => Attribute(i, "name") == "password"))
            .ToArray();

        if (forms.Length != 1 || forms[0].Descendants("input").Count() != 2 ||
            !string.Equals(Attribute(forms[0], "method"), "post", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CRM 登录表单变化，无法安全登录");

        Target(origin, Attribute(forms[0], "action") is { Length: > 0 } action ? action : "/login", "/login", []);
        await Request("/login", config.TimeoutSeconds, ct, [new("username", username), new("password", password)]);
        var (_, account) = Identity(await Request("/index", config.TimeoutSeconds, ct), username);
        Persist();

        return account;
    }

    /// <summary>
    /// 读取当前登录人、本人月份与当天月表
    /// </summary>
    public async Task<TimesheetDay> Read(WorkHoursConfig config, DateOnly date, CancellationToken ct)
    {
        if (config.Account == "")
            throw new ValidationException("请先登录并绑定本人账号");

        var origin = config.GetCrmOrigin();
        SelectOrigin(origin);
        var (name, account) = Identity(await Request("/index", config.TimeoutSeconds, ct), config.Account);
        var list = Parse(await Request("/oa/hour/", config.TimeoutSeconds, ct));
        var candidates = new List<string>();

        foreach (var a in list.Descendants("a").Where(a => a.Attributes.Contains("href")))
        {
            if (!(a.Ancestors("tr").FirstOrDefault()?.ChildNodes
                .Any(c => c.NodeType == HtmlNodeType.Element && Text(c).Trim() == name) ?? false))
                continue;

            var raw = Attribute(a, "href");

            if (!Uri.TryCreate(origin, raw, out var uri) || uri.AbsolutePath != "/oa/hour/desc")
                continue;

            var q = QueryHelpers.ParseQuery(uri.Query);

            if (q["syear"] != date.Year.ToString() || q["smonth"] != date.Month.ToString())
                continue;

            var candidateId = q["sueid"].ToString();

            if (!Regex.IsMatch(candidateId, @"^\d+$"))
                throw new InvalidDataException("CRM 本人人员编号无效");

            Target(origin, raw, "/oa/hour/desc", new()
            {
                ["sueid"] = candidateId,
                ["syear"] = date.Year.ToString(),
                ["smonth"] = date.Month.ToString()
            });
            candidates.Add(uri.PathAndQuery);
        }

        if (candidates.Count != 1)
            throw new InvalidDataException("无法唯一定位本人的当月工时");

        var month = candidates[0];
        var id = QueryHelpers.ParseQuery(new Uri(origin, month).Query)["sueid"].ToString();

        var doc = Parse(await Request(month, config.TimeoutSeconds, ct));
        var tables = doc.SelectNodes("//*[@id='table4']");

        if (tables?.Count != 1)
            throw new InvalidDataException("CRM 月度表不唯一");

        var rows = tables[0].SelectNodes("./tr | ./tbody/tr")?.ToArray() ?? [];

        if (rows.Length != 29)
            throw new InvalidDataException("CRM 月度表行数变化");

        var header = Cells(rows[1]);

        if (header.Length < 2 || !new[] { $"{date.Year}-{date.Month}", $"{date.Year}-{date.Month:D2}" }.Contains(Text(header[0]).Trim()))
            throw new InvalidDataException("CRM 月度表年月错误");

        var columns = Enumerable.Range(1, header.Length - 1)
            .Where(i => Text(header[i]).Trim() == date.Day.ToString())
            .ToArray();

        if (columns.Length != 1)
            throw new InvalidDataException("CRM 当天列不唯一");

        var projects = new List<string>();

        for (var i = 0; i < 27; i++)
        {
            var cells = Cells(rows[i + 2]);

            if (cells.Length != header.Length || Text(cells[0]).Trim() != WorkHoursConfig.TimeLabels[i])
                throw new InvalidDataException("CRM 时间轴结构变化");

            projects.Add(Text(cells[columns[0]]).Trim());
        }

        var path = $"/oa/hour/edit?shman={id}&syear={date.Year}&smonth={date.Month}&sday={date.Day}";
        var buttons = rows[0].Descendants("button")
            .Where(b => Attribute(b, "class")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains("fill") && Attribute(b, "data-id") == date.Day.ToString())
            .ToArray();

        if (buttons.Length != 1)
            throw new InvalidDataException("CRM 每日汇报按钮不唯一");

        var links = Regex.Matches(Attribute(buttons[0], "onclick"), @"['"" ](/oa/hour/edit\?[^'"" ]+)['""]");

        if (links.Count != 1)
            throw new InvalidDataException("CRM 每日汇报按钮地址无法识别");

        Target(origin, links[0].Groups[1].Value, "/oa/hour/edit", EditQuery(id, date));

        return new(account, id, path, [.. projects]) { CrmOrigin = origin };
    }

    /// <summary>
    /// 读取并验证本人当天的保存表单
    /// </summary>
    public async Task<List<KeyValuePair<string, string>>> Prepare(TimesheetDay day, WorkHoursConfig config, DateOnly date, CancellationToken ct)
    {
        var origin = config.GetCrmOrigin();

        if (day.CrmOrigin != origin)
            throw new ValidationException("CRM 站点已变化，请重新查询本人记录");

        SelectOrigin(origin);
        Target(origin, day.EditPath, "/oa/hour/edit", EditQuery(day.PersonId, date));

        var doc = Parse(await Request(day.EditPath, config.TimeoutSeconds, ct));
        var forms = doc.Descendants("form")
            .Where(f => f.Descendants().Any(n => Attribute(n, "name") == "hourtime[]"))
            .ToArray();

        if (forms.Length != 1)
            throw new InvalidDataException("CRM 每日表单不唯一");

        var form = forms[0];

        if (!string.Equals(Attribute(form, "method"), "post", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CRM 每日表单提交方法变化");

        Target(origin, Attribute(form, "action") is { Length: > 0 } action ? action : day.EditPath,
            "/oa/hour/edit", EditQuery(day.PersonId, date));
        var title = doc.SelectSingleNode("//title") is { } titleNode ? Text(titleNode) : "";

        // CRM 弹窗返回的表单片段没有 title，日期已通过本人月表按钮及编辑地址参数校验
        if (title.Length > 0 && !Regex.IsMatch(title, $@"{date.Year}\D+0?{date.Month}\D+0?{date.Day}(?:\D|$)"))
            throw new InvalidDataException("CRM 每日表单标题日期不匹配");

        var times = form.Descendants().Where(n => Attribute(n, "name") == "hourtime[]").ToArray();
        var checks = form.Descendants().Where(n => Attribute(n, "name") == "acheck[]").ToArray();
        var projects = form.Descendants().Where(n => Attribute(n, "name") == "hxmhid[]").ToArray();

        if (times.Length != 27 || checks.Length != 27 || projects.Length != 27 ||
            times.Where((t, i) => Attribute(t, "value") != WorkHoursConfig.TimeLabels[i]).Any())
            throw new InvalidDataException("CRM 每日表单时间字段变化");

        var selected = config.GetTimeSlots().ToHashSet();
        var body = new List<KeyValuePair<string, string>>();

        for (var i = 0; i < 27; i++)
        {
            var active = selected.Contains(WorkHoursConfig.TimeLabels[i]);

            if (active && !projects[i].Descendants("option").Any(o => Attribute(o, "value") == config.Project))
                throw new ValidationException("配置项目不在本人表单选项中");

            body.Add(new("hourtime[]", WorkHoursConfig.TimeLabels[i]));
            body.Add(new("acheck[]", active ? "1" : "0"));
            body.Add(new("hxmhid[]", active ? config.Project : ""));
        }

        var fields = form.Descendants()
            .Where(n => n.Name is "input" or "select" or "textarea" or "button" && n.Attributes.Contains("name"));

        foreach (var field in fields)
        {
            var name = Attribute(field, "name");

            if (name is "hourtime[]" or "acheck[]" or "hxmhid[]")
                continue;

            if (Attribute(field, "type") != "hidden" || name is not ("__RequestVerificationToken" or "_token" or "csrf_token"))
                throw new InvalidDataException("CRM 表单存在未授权字段，停止保存");

            if (body.Any(p => p.Key == name))
                throw new InvalidDataException("CRM 隐藏字段重复");

            body.Add(new(name, Attribute(field, "value")));
        }

        return body;
    }

    /// <summary>
    /// 只向严格验证的本人当天地址发送一次保存
    /// </summary>
    public async Task Save(TimesheetDay day, List<KeyValuePair<string, string>> body, DateOnly date, CancellationToken ct)
    {
        if (date != DateOnly.FromDateTime(DateTime.Today))
            throw new ValidationException("日期已变化，禁止保存非当天记录");

        SelectOrigin(day.CrmOrigin);
        Target(day.CrmOrigin, day.EditPath, "/oa/hour/edit", EditQuery(day.PersonId, date));
        await Request(day.EditPath, 30, ct, body);
    }

    /// <summary>
    /// 清理内存和持久化会话
    /// </summary>
    public void Logout()
    {
        pendingSession = null;

        foreach (Cookie cookie in cookies.GetAllCookies())
        {
            cookie.Expired = true;
        }

        sessions.DeleteByKey("WorkHoursSession");
    }

    /// <summary>
    /// 只序列化源站 Cookie，不保存密码
    /// </summary>
    private void Persist()
    {
        var json = SerializeHelper.Serialize(new StoredSession(activeOrigin!.AbsoluteUri,
            [.. cookies.GetAllCookies().Cast<Cookie>()
                .Where(c => !c.Expired && string.Equals(c.Domain.TrimStart('.'), activeOrigin.Host, StringComparison.OrdinalIgnoreCase))
                .Select(c => new StoredCookie(c.Name, c.Value, c.Domain, c.Path, c.Expires))]));
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.LocalMachine);
        sessions.SaveByKey("WorkHoursSession", Convert.ToBase64String(encrypted));
    }

    /// <summary>
    /// 释放连接
    /// </summary>
    public void Dispose()
    {
        http.Dispose();
        calendar.Dispose();
    }

    /// <summary>
    /// 判断重复日期，仅工作日模式请求并交叉核对年度与每日节假日历
    /// </summary>
    public async Task<bool> ShouldRun(WorkHoursConfig config, DateOnly date, CancellationToken ct)
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
    public static DateTimeOffset Next(DateTimeOffset now, WorkHoursConfig config)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        config.GetTimeSlots();

        for (var offset = 0; offset <= 7; offset++)
        {
            var candidate = date.AddDays(offset);
            var due = Due(candidate, config.RunAt);

            if (due > now && (config.RepeatMode == "Workdays" || config.WeekDays.Contains(WeekDay(candidate))))
                return due;
        }

        throw new InvalidDataException("没有可执行的重复日期");
    }

    /// <summary>
    /// 解析页面中的当前身份
    /// </summary>
    public static (string Name, string Account) Identity(string html, string expected)
    {
        var doc = Parse(html);
        var match = Regex.Match(Text(doc.SelectSingleNode("//body") ?? doc),
            @"登[陆录]用户[：:]\s*([^\r\n<>]+?)\((RK\d+)\)");

        if (!match.Success)
            throw new ValidationException("CRM 登录已过期，请重新登录");

        if (!string.Equals(match.Groups[2].Value, expected, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("CRM 当前账号与绑定账号不符");

        return (match.Groups[1].Value.Trim(), match.Groups[2].Value);
    }

    /// <summary>
    /// 验证同源地址与精确查询参数
    /// </summary>
    public static Uri Target(Uri origin, string address, string path, Dictionary<string, string>? query = null)
    {
        if (origin.Scheme != Uri.UriSchemeHttps || !Uri.TryCreate(origin, address, out var uri) ||
            uri.Scheme != origin.Scheme || uri.Host != origin.Host ||
            uri.Port != origin.Port || uri.UserInfo != "" || uri.Fragment != "" || uri.AbsolutePath != path)
            throw new InvalidDataException("CRM 目标地址不符合安全限制");

        var actual = QueryHelpers.ParseQuery(uri.Query);

        if (query is not null &&
            (actual.Count != query.Count || query.Any(k =>
                !actual.TryGetValue(k.Key, out var v) || v.Count != 1 || v[0] != k.Value)))
            throw new InvalidDataException("CRM 目标参数不匹配");

        return uri;
    }

    /// <summary>
    /// 本人当天编辑地址的精确参数
    /// </summary>
    public static Dictionary<string, string> EditQuery(string id, DateOnly date) => new()
    {
        ["shman"] = id,
        ["syear"] = date.Year.ToString(),
        ["smonth"] = date.Month.ToString(),
        ["sday"] = date.Day.ToString()
    };

    /// <summary>
    /// 只取表格行的直接单元格，忽略空白和注释节点
    /// </summary>
    private static HtmlNode[] Cells(HtmlNode row) => [.. row.ChildNodes.Where(n => n.Name is "td" or "th")];

    /// <summary>
    /// 从已下载的字符串解析 HTML，不发起网络请求
    /// </summary>
    private static HtmlNode Parse(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        return document.DocumentNode;
    }

    /// <summary>
    /// 读取并解码节点文本
    /// </summary>
    private static string Text(HtmlNode node) => HtmlEntity.DeEntitize(node.InnerText);

    /// <summary>
    /// 读取并解码属性，缺少属性时返回空字符串
    /// </summary>
    private static string Attribute(HtmlNode node, string name) => HtmlEntity.DeEntitize(node.GetAttributeValue(name, ""));
}

