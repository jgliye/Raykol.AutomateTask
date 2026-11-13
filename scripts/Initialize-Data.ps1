param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请以管理员身份运行，初始化需要设置服务目录 ACL'
}
$service = [Security.Principal.NTAccount]::new('NT SERVICE\Raykol.AutomateTask')
$admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$users = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
$owner = [Security.Principal.SecurityIdentifier]::new('S-1-3-0')
$inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$none = [Security.AccessControl.PropagationFlags]::None
$allow = [Security.AccessControl.AccessControlType]::Allow

# 服务可创建根目录数据库及日志文件，但不能修改已有 EXE；普通用户可读取托盘及打开页面所需文件
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner($admins)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($admins,'FullControl',$inherit,$none,$allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($system,'FullControl',$inherit,$none,$allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($service,'ReadAndExecute',$inherit,$none,$allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($service,'CreateFiles','None',$none,$allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($owner,'Modify','ObjectInherit','InheritOnly',$allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($users,'ReadAndExecute','None',$none,$allow))
Set-Acl -LiteralPath $Root -AclObject $acl

$tray = Join-Path $Root 'Tray'
$trayAcl = Get-Acl -LiteralPath $tray
$trayAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($users,'ReadAndExecute',$inherit,$none,$allow))
Set-Acl -LiteralPath $tray -AclObject $trayAcl

foreach ($name in @('data','Logs')) {
    $path = Join-Path $Root $name
    [IO.Directory]::CreateDirectory($path) | Out-Null
    $restricted = [Security.AccessControl.DirectorySecurity]::new()
    $restricted.SetAccessRuleProtection($true,$false)
    $restricted.SetOwner($admins)
    foreach ($entry in @($admins,$system)) {
        $restricted.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($entry,'FullControl',$inherit,$none,$allow))
    }
    $restricted.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($service,'Modify',$inherit,$none,$allow))
    Set-Acl -LiteralPath $path -AclObject $restricted
}
$fileAcl = [Security.AccessControl.FileSecurity]::new()
$fileAcl.SetAccessRuleProtection($true,$false)
$fileAcl.SetOwner($admins)
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($admins,'FullControl',$allow))
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($system,'FullControl',$allow))
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($service,'ReadAndExecute',$allow))
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($users,'ReadAndExecute',$allow))
foreach ($name in @('appsettings.json','appsettings.Production.json','Open-Management.ps1','打开管理页面.cmd')) {
    Set-Acl -LiteralPath (Join-Path $Root $name) -AclObject $fileAcl
}
Write-Host '服务数据目录已准备；没有启动服务'
