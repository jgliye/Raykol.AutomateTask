$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$project = Join-Path $root 'Raykol.AutomateTask\Raykol.AutomateTask.csproj'
$metadata = & dotnet msbuild $project -nologo '-getProperty:MSBuildProjectName,Version'
if ($LASTEXITCODE -ne 0) { throw '读取项目名和版本号失败' }
$properties = ($metadata | Out-String | ConvertFrom-Json).Properties
$projectName = $properties.MSBuildProjectName
$version = $properties.Version
if ($projectName -notmatch '^[A-Za-z0-9._-]+$' -or $version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)*$') {
    throw '项目名或版本号不能用于发布目录命名'
}
$output = Join-Path $root ('publish\' + $projectName + '-' + $version)
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output + '.zip'))) {
    throw '该版本的发布目录或分享包已存在，拒绝覆盖；请调整项目 Version 后再发布'
}
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw '发布失败' }
$trayProject = Join-Path $root 'Raykol.AutomateTask.Tray\Raykol.AutomateTask.Tray.csproj'
& dotnet publish $trayProject -c Release -r win-x64 --self-contained true "-p:Version=$version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $output 'Tray')
if ($LASTEXITCODE -ne 0) { throw '托盘发布失败' }
foreach ($name in @('Install-Service.ps1','Uninstall-Service.ps1','Initialize-Data.ps1','Open-Management.ps1','安装服务.cmd','卸载服务.cmd','打开管理页面.cmd')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output
[IO.Directory]::CreateDirectory((Join-Path $output 'Logs')) | Out-Null
if (Test-Path -LiteralPath (Join-Path $output 'AutomateTask.db')) {
    throw '分享包检查失败：包含个人配置或运行数据'
}
Compress-Archive -LiteralPath $output -DestinationPath ($output + '.zip')
Write-Host ('发布目录：' + $output)
Write-Host ('分享包：' + $output + '.zip')
