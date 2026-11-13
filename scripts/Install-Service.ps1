$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'Raykol.AutomateTask.exe'
if (!(Test-Path -LiteralPath $exe)) { throw '请从发布目录运行安装脚本' }
$tray = Join-Path $root 'Tray\Raykol.AutomateTask.Tray.exe'
if (!(Test-Path -LiteralPath $tray)) { throw '发布包缺少托盘程序' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '请右键安装服务.cmd，以管理员身份运行' }
if (Get-Service -Name 'Raykol.AutomateTask' -ErrorAction SilentlyContinue) { throw '服务已存在，请先检查现有服务，不覆盖安装' }
& sc.exe create Raykol.AutomateTask binPath= ('"' + $exe + '"') start= auto obj= 'NT SERVICE\Raykol.AutomateTask' DisplayName= '睿科自动化任务'
if ($LASTEXITCODE -ne 0) { throw '注册服务失败' }
& sc.exe sidtype Raykol.AutomateTask unrestricted
if ($LASTEXITCODE -ne 0) { throw '服务 SID 设置失败；未启动服务' }
& (Join-Path $root 'Initialize-Data.ps1') -Root $root
& sc.exe failure Raykol.AutomateTask reset= 86400 actions= restart/60000/restart/120000/restart/300000
if ($LASTEXITCODE -ne 0) { throw '失败恢复设置失败；未启动服务' }
& sc.exe description Raykol.AutomateTask '本机管理页和工作日工时服务；仅保存每日汇报，不提交审批'
Start-Service -Name Raykol.AutomateTask
$startup = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $startup -Force | Out-Null
New-ItemProperty -Path $startup -Name 'Raykol.AutomateTask.Tray' -Value ('"' + $tray + '" "' + $root + '"') -PropertyType String -Force | Out-Null
Write-Host '服务已设置为开机自启；托盘将在用户下次登录时启动，也可手动运行 Tray 中的 EXE'
Write-Host '全新工时配置默认关闭自动填报；请在管理页面登录并配置任务'
