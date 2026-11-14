using FreeSql;
using Microsoft.AspNetCore.Mvc;
using NLog.Config;
using NLog.Extensions.Logging;
using Raykol.AutomateTask.Services;
using Raykol.AutomateTask.Utils;

// 所有运行文件均相对于 EXE 根目录，不使用 Windows 服务的 system32 工作目录
var root = AppContext.BaseDirectory;

// 保持命名互斥量句柄，限制同一台机器上的服务和手动启动进程只能运行一个
using var instanceMutex = new Mutex(false, @"Global\Raykol.AutomateTask", out var firstInstance);

if (!firstInstance)
{
    Console.WriteLine("自动化任务已在运行，当前进程退出");
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = root,
    WebRootPath = Path.Combine(root, "wwwroot", "Web")
});
builder.Services.AddSingleton(_ => new FreeSqlBuilder()
       .UseConnectionString(DataType.Sqlite, $"Data Source={Path.Combine(root, "AutomateTask.db")};Pooling=False;Synchronous=Full;Default Timeout=5")
       .UseAdoConnectionPool(true)
       .UseAutoSyncStructure(true)
       .Build());
builder.Services.AddSingleton(typeof(IBaseConfigService<>), typeof(BaseConfigService<>));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddWindowsService(options => options.ServiceName = "Raykol.AutomateTask");
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    var configuration = new XmlLoggingConfiguration(Path.Combine(root, "NLog.config"));
    loggingBuilder.AddNLog(configuration);
});
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = _ =>
        new BadRequestObjectResult(new { error = "输入参数无效，请检查必填项和数字范围" }));
builder.Services.AddSingleton<ITaskService, TaskService>();
builder.Services.AddHostedService<AutomationWorker>();
builder.Services.AddHostedService<NotificationService>();

await using var app = builder.Build();
app.UseMiddleware<RequestGuardMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
await app.RunAsync();
