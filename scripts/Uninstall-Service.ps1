$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '请右键卸载服务.cmd，以管理员身份运行' }
$startup = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $startup -Name 'Raykol.AutomateTask.Tray' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $startup -Name 'Raykol.AutomateTask.Tray'
}
$service = Get-Service -Name Raykol.AutomateTask -ErrorAction SilentlyContinue
if (!$service) { Write-Host '服务未安装'; exit 0 }
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name Raykol.AutomateTask
    $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(60))
}
& sc.exe delete Raykol.AutomateTask
if ($LASTEXITCODE -ne 0) { throw '卸载服务失败' }
Write-Host '服务及托盘登录启动项已卸载；已打开的托盘请右键退出，保留当前数据库和日志'
