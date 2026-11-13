# 睿科自动化任务（.NET 9）

使用 Visual Studio 2022（支持 .NET 9 的版本）打开 `Raykol.AutomateTask.sln`。后端是 ASP.NET Core Web API，管理页面使用 Vue 3 + Ant Design Vue，另有 WinForms 托盘通知项目。开发和编译需要 Node.js 20.19+（20.x）、22.13+（22.x）或 24+，部署运行只需要发布包，不需要 Node.js、IIS 或前端开发服务器。

## 目录

```text
Raykol.AutomateTask.sln
Raykol.AutomateTask/
  Program.cs            IoC、Windows 服务、回环端点和路由
  Controllers/          WorkHoursController：/api/work-hours JSON 接口
  ClientApp/            Vue 前端工程、锁定依赖及构建/代码规范配置
    src/api/            work-hours.js：同源请求、管理会话与防伪令牌
    src/views/          WorkHours.vue：配置、CRM 会话与日志管理
    src/assets/styles/  全局样式与页面样式
    src/App.vue         应用入口、中文语言包与主题配置
    src/main.js         Vue 挂载入口
    public/             本地站点图标，沿用程序 ico
  Models/               BaseConfigEntry、WorkHoursConfig、执行状态、CRM 数据与 WorkHoursStatus
  Enums/                通用配置分类 BaseConfigType
  Services/             IBaseConfigService<T> / BaseConfigService<T>：通用配置查询与保存
                        WorkHoursService：工时配置、外部请求、解析、会话及重复日期
                        WorkHoursWorker：后台轮询、重试及状态流转
                        NotificationService：本机终态通知通道
  Utils/                SerializeHelper、RequestGuardMiddleware、LogReader
  Properties/           Visual Studio 启动配置
  wwwroot/Web/          自动生成的前端打包产物，不手工修改
  Assets/               程序图标
  appsettings.json      通用启动参数，当前为空
  appsettings.Development.json 开发环境启动参数覆盖，当前为空
  appsettings.Production.json 发布运行地址，默认 http://127.0.0.1:18558
  NLog.config           日志格式、按月分卷和保留策略
  AutomateTask.db       运行时在 EXE 根目录创建，不进入源码或发布包
Raykol.AutomateTask.Tray/ 用户会话托盘，登录启动、提醒与管理入口
scripts/                发布、安装、卸载和本机管理入口
```

## 编译与验证

在解决方案目录执行，构建会按锁文件恢复前端依赖并自动打包到 wwwroot/Web，然后编译应用和托盘。当前使用项目级编译命令：

```powershell
dotnet build .\Raykol.AutomateTask\Raykol.AutomateTask.csproj -c Release
dotnet build .\Raykol.AutomateTask.Tray\Raykol.AutomateTask.Tray.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

前端单独开发时，在 `Raykol.AutomateTask/ClientApp` 执行 `npm ci`、`npm run dev`。Vite 开发服务器使用 5173 端口，通过同源代理连接本机 18558 后端端口；后端端口不同可设置 BACKEND_URL。`npm run lint` 检查代码，`npm run format:check` 检查格式，`npm run format` 格式化。Vue 单文件组件统一按 `<style>`、`<template>`、`<script setup>` 排列，ESLint 会检查顺序。前端行宽为 120 字符，属性不强制逐行，复杂校验规则集中放在脚本中。Vue 文件使用大驼峰，普通 JS/CSS 文件使用小写短横线，C# 文件与类型同名；main.js、vite.config.js 等入口和工具配置保留约定名称。当前只有一个页面，不引入 Router、Pinia 或 Axios。

发布到全新的 `publish/项目名-Version/`，包含服务单文件 EXE、`Tray/` 中的自包含托盘 EXE、wwwroot/Web 静态资源、空白默认配置、日志配置、脚本、空 Logs/data 目录及 ZIP 分享包。前端依赖全部打包到本地资源，无 CDN 依赖；ClientApp 源码和 node_modules 不会复制到发布包。wwwroot/Web 随编译和发布复制到程序根目录，后端通过 WebRootPath 指向该目录，管理页面仍从根地址访问。目录及 ZIP 名称自动读取项目名和 Version；同版本产物存在时拒绝覆盖，请更新项目版本后发布。不会复制个人数据库、会话、日志或运行历史。源码固定 `net9.0-windows`，未安装或切换到 .NET 10。

## 部署与使用

安装会真实注册并启动 Windows 服务，须在审阅产物、确认部署后由本人以管理员身份运行发布目录内的“安装服务.cmd”。服务名称为 `Raykol.AutomateTask`，使用独立虚拟服务账号 `NT SERVICE\Raykol.AutomateTask`，不是 LocalSystem；设为开机自启并配置故障恢复。安装脚本不修改旧版 `Raykol-Auto-Working-Hours` 计划任务。首次工时配置由模型默认值初始化并存入数据库，自动填报默认关闭，验收和旧任务切换前请保持关闭。

安装后仅管理员可以修改程序文件，服务可创建根目录数据库及其日志文件，并写入受限 data、Logs。普通用户可运行托盘、直接打开本机管理页面，但不能直接读取数据库、会话或日志文件。仅在专用发布目录运行安装脚本，切勿在源码目录、项目父目录或共享目录运行 ACL 初始化。初始化失败时不会启动服务；注册记录可使用卸载脚本清除后重试。全新数据默认关闭自动填报；已有配置以数据库中的开关和状态为准。

安装脚本注册 HKLM Run 托盘登录启动项，下次用户登录时在用户会话运行；当前会话可手动运行 `Tray/Raykol.AutomateTask.Tray.exe`。托盘无需管理员权限，不保持浏览器打开，每十秒通过本机管道读取当天最终成功或失败结果。提醒不包含账号、项目或会话；每个 Windows 用户独立去重，重启托盘可补提醒当天尚未记录的终态，不补历史日期，不弹重试或正常跳过。用户回执只保存一条日期与结果，位于 `%LOCALAPPDATA%/Raykol.AutomateTask/`。退出托盘不停止后台任务；无人登录时服务仍执行，但没有桌面弹窗。采用 Windows 自带 NotifyIcon 提醒，通知关闭、勿扰模式等系统设置可能抑制显示，不保证送达，也不提供点击操作和通知中心持久历史。

管道按服务目录隔离，仅本机交互登录身份可读取终态和写入单字节回执，不能通过它修改配置或调用 CRM；拒绝网络登录身份。托盘右键“打开管理页面”或双击直接打开本机页面，无需管理员授权。开发调试托盘时传入服务程序输出目录的绝对路径，例如 `Raykol.AutomateTask.Tray.exe "D:\Projects\Raykol.AutomateTask\Raykol.AutomateTask\bin\Debug\net9.0-windows"`，先单独启动开发后端；编译不会自动启动托盘。

直接访问 `http://127.0.0.1:18558`，或运行“打开管理页面.cmd”。页面打开后即可查看状态和配置，登录本人 CRM，不需要管理密钥、管理登录或管理员授权。打开页面脚本从 appsettings.Production.json 的 Urls 读取发布运行地址。

页面采用 Vue 单页界面，后端只返回 JSON，不包含 cshtml 或 Razor。`GET /api/work-hours/session` 自动获取防伪令牌，写操作携带 X-CSRF-TOKEN 请求头和防伪 Cookie，无需用户操作。RequestGuardMiddleware 保留本机地址、Host、响应头及受控异常处理。框架防伪数据仍使用本机 DPAPI 保护，与用户访问页面的登录无关。

全新安装在页面登录本人 CRM。密码仅用于本次 HTTPS 登录，不写入配置或日志；Cookie 使用本机 DPAPI 加密后，通过通用配置服务存入 SQLite，数据库文件受 Windows ACL 保护。程序不定时保存密码或自动重新登录。管理页面显示绑定账号；点击“只读预检”实时检查会话、法定日历、月表和项目选项。当前页面没有手工保存工时或提交审批的按钮。

VS 使用项目自带的 Development 启动配置直接 F5 即可，服务启动后浏览器访问 `http://127.0.0.1:18558`，不再生成或读取 management.key，也不生成带密钥的快捷入口。默认端口为 18558，首次工时配置默认关闭自动填报；默认构建不会启动应用、访问真实 CRM 或安装服务。

程序通过全局命名互斥量限制同一台机器只运行一个应用进程，包括 Windows 服务与手动运行的 EXE；不再创建 instance.lock 文件。开发调试地址在 Properties/launchSettings.json 的 applicationUrl 配置，发布和 Windows 服务地址在 appsettings.Production.json 的 Urls 配置，两处默认 http://127.0.0.1:18558。框架自动绑定端点，Program.cs 不读取 Port 或调用 Listen；打开页面脚本读取发布运行地址。

## 业务及故障处理

- 默认本机时间 19:00，直接使用系统当前时间，不注入 TimeProvider，也不做 UTC 或固定时区换算。重复方式二选一：`Weekly` 为周一至周日自选多选，`weekDays` 使用 1–7；`Workdays` 为中国法定工作日（默认）。只有 `Workdays` 请求 Timor 年度和每日接口，必须一致；`Weekly` 完全不请求日历，按勾选星期执行，忽略节假日及补班。
- 默认包含 09:00–11:30、13:00–18:30 的末尾标签，共 18 格 / 9 小时；不包含末尾为 16 格 / 8 小时。12:00、12:30 固定休息。
- 登录过期、身份不符、当天月表已有任意记录直接跳过。不补齐、不覆盖，不补填错过的日期或首轮时间；服务启动晚于执行时刻，即使仍在一分钟触发窗口内也不补跑。
- 网络、服务器或日历异常按 WorkHoursConfig.RetryIntervalSeconds 配置的间隔重试（默认 120 秒，范围 10–86400 秒）；WorkHoursConfig.MaxRetries 为首次失败后的额外重试次数（默认 5，范围 0–20，0 表示不重试），总轮数为 MaxRetries + 1。管理页可直接修改并保存到数据库。间隔按每轮开始时间计算，单轮超时小于重试间隔且最多 110 秒。后台循环等待当前轮完成后再检查下一轮，管理接口与后台任务之间不设置排队锁；网络请求有独立超时和停止取消。
- 每日状态在 POST 前通过 SQLite 原子提交落盘，使用 FULL 同步；响应丢失或未知时永久标记本日已尝试，只读月表核查，重启也不会重发 POST。成功必须全部 27 格及项目逐格一致，HTTP 200 本身不代表成功。
- 若核实耗尽或跨日仍未确认，人工查看 CRM；不要删除数据库或执行快照来强制重跑。损坏的数据库或快照会阻止执行，不会自动覆盖或重置。
- 保存的是首轮配置快照，包含项目、时间段、重试间隔和次数；执行途中修改配置对下次任务生效，当前任务及重启恢复沿用原快照。禁用后停止尚未发送的任务；已发送请求仍可只读核实。关闭并不会撤销已经保存的记录。
- 使用 FreeSql.Provider.Sqlite，数据库为 EXE 根目录下的 `AutomateTask.db`。`Program.cs` 单例注册 IFreeSql，启用 UseAutoSyncStructure(true)，由实体 CRUD 按需建表。配置、执行状态和加密会话统一保存到 BaseConfigEntry，包含 Key、Type、Name、Category、Content、CreateTime、UpdateTime，Key 与 Type 联合唯一；Content 保存完整 JSON 对象。
- WorkHoursService 通过 `IBaseConfigService<WorkHoursConfig>` 读取和保存工时配置，默认键为 WorkHoursConfig。新增任务定义自己的 XxxConfig，并注入 `IBaseConfigService<XxxConfig>` 即可复用，无需再增加配置文件存储类或仓储层。通用服务保留参考实现按 ID、键、名称和分类查询保存的同步与异步 API，共享写入锁，配置日志不输出 JSON 内容。
- `appsettings*.json` 只保存软件启动参数。开发地址放在 launchSettings.json 的 applicationUrl，Windows 服务默认使用 Production 环境，由 appsettings.Production.json 的 Urls 配置地址。主 appsettings.json 保持为空，避免其中的 Urls 覆盖开发启动配置。修改启动地址后重启服务生效，保持使用本机地址 127.0.0.1；管理边界直接校验连接的实际本机端口。工时配置、执行状态和加密会话保存在数据库；WorkHoursConfig 首次缺失时使用模型默认值创建，与 appsettings 文件及开发环境配置无关。项目按全新数据库使用，不提供历史数据导入、升级或兼容处理。
- SerializeHelper 统一序列化、反序列化、JSON 检查以及同步/异步文件读写，采用小驼峰和中文可读编码；文件保存使用唯一临时文件、落盘后原子替换。损坏的配置直接抛出异常，不能按缺失配置重新初始化。数据库使用无池短连接和 FULL 同步，ORM 随应用关闭释放；日志仍由 NLog 写入 Logs，不在内存保留历史。
- 日志由 NLog 写入 EXE 根目录 `Logs/`，按本机年月命名（如 `2026-11.log`），超过 10 MB（10485760 字节）递增分卷（如 `2026-11_001.log`），默认保留 30 天。`NLog.config` 集中配置格式、分卷和保留策略，修改后自动重载；该文件随编译和发布复制到程序目录。日志时间采用本机时区，与任务执行使用相同的本机时间。格式为 `${longdate}|${uppercase:${level}}|${message}${newline}${exception}`，异常占下一行，没有异常时保留空行。页面只读取最近两个文件的末尾 200 行，并支持日志写入期间刷新。每次执行仅输出一条最终成功、失败或跳过日志，异常时输出必要的重试及下一次时间；保留服务启停、登录、配置保存等事件。业务日志使用 ILogger，成功为 INFO、重试为 WARN、最终失败为 ERROR，不记录密码、Cookie、认证头、请求正文或原始异常。文件同步追加并及时刷新，无后台日志队列或历史日志缓存；尚未进行长期运行内存压测

CRM 站点地址由数据库中的 WorkHoursConfig.CrmUrl 配置，默认 https://crm.raykol.com，可在管理页修改。只接受 HTTPS 站点根地址，可带端口，不能包含账号密码、业务路径、查询参数或片段。只访问配置源站上明确允许的登录、身份、工时列表、本人当月与本人当天编辑路径。会话与源站（协议、域名和端口）绑定，修改地址后需重新登录；修改地址不会将原 Cookie 转发到新站点。首轮快照固定执行站点，保存前发现地址变化即跳过，已发送请求只查询原站点核实。跨域重定向不转发凭据，POST 重定向不重发。CRM HTML 使用 HtmlAgilityPack 1.13.1 解析并解码文本/属性实体；未知表单字段、结构或项目选项变化一律停止；需要根据只读证据调整解析器后重新验收。实现中没有月度审批接口。

卸载以管理员身份运行“卸载服务.cmd”，会停止并删除新服务、移除托盘登录启动项，保留当前数据库和日志，不触碰旧版任务。已打开的托盘请右键退出。分享仅使用 `Publish.ps1` 生成的全新干净 ZIP，不压缩运行中的个人目录。

## 验证边界

当前仅保留应用和托盘项目，Tests 项目已删除，暂不进行单元测试。编译通过不代表实际业务运行验证通过。真实登录、工时保存、服务安装与切换旧任务均应另行明确授权并验收。
