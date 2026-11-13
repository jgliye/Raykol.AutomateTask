# 睿科自动化任务

基于 .NET 9、Vue 3 和 Ant Design Vue 的本机工时填报工具，支持 Windows 服务和托盘提醒。

## 目录

```text
Raykol.AutomateTask.sln
Raykol.AutomateTask/
  Controllers/  工时管理接口
  Services/     配置、CRM 请求与解析、调度、日志读取和通知
  Models/       配置、实体和执行状态
  Enums/        通用配置分类
  Utils/        JSON 工具、请求校验和通知协议
  ClientApp/    Vue 前端源码
  wwwroot/Web/  自动生成的前端构建产物
Raykol.AutomateTask.Tray/  用户会话托盘
scripts/                 发布、安装、卸载和打开页面脚本
```

## 开发

使用支持 .NET 9 的 Visual Studio 2022 打开解决方案，F5 启动主程序。前端构建需要 Node.js 20.19+（20.x）、22.13+（22.x）或 24+。

```powershell
dotnet build .\Raykol.AutomateTask.sln -c Release
```

构建自动将前端打包到 `wwwroot/Web`。单独开发前端时，在 `ClientApp` 执行 `npm ci`、`npm run dev`；规范检查使用 `npm run lint` 和 `npm run format:check`。

## 使用

- 服务启动后访问 `http://127.0.0.1:18558`，无需管理密钥，登录本人 CRM 后配置任务
- 自动填报默认关闭，默认本机时间 19:00；重复方式为周一至周日多选或中国法定工作日，仅后者请求节假日日历
- CRM 地址、工时时间段、执行时间及失败重试间隔和次数均可在页面配置
- “重置配置”丢弃未保存的修改，重新读取已保存配置；CRM 密码仅用于本次登录，会话加密保存
- 当天已有记录、登录过期或错过执行时间时跳过；保存结果不明时只查询核实，不重复提交，不提交审批
- 托盘提醒当天最终成功或失败结果；退出托盘不停止服务，无人登录时不会弹出桌面提醒

## 配置与日志

- `appsettings*.json` 只保存启动参数；开发地址在 `Properties/launchSettings.json`，服务地址在 `appsettings.Production.json`
- `AutomateTask.db` 位于运行根目录，保存业务配置、加密会话和执行状态
- `NLog.config` 管理日志格式和归档；日志位于根目录 `Logs`，每月一个文件，超过 10 MB 分卷，保留 30 天，页面显示最近 200 行

## 部署

运行 `scripts/Publish.ps1` 生成 `publish/项目名-版本号` 及 ZIP；同版本拒绝覆盖。运行发布目录的“安装服务.cmd”或“卸载服务.cmd”管理服务，需要管理员权限。“打开管理页面.cmd”无需管理员权限。

仅分享干净发布包，不包含数据库、会话和日志。首次使用先保持自动填报关闭，完成 CRM 登录、配置和只读预检后再启用；与旧任务切换时避免同时运行。
