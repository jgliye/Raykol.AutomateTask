$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $root 'appsettings.Production.json') -Raw | ConvertFrom-Json
$url = [Uri]$config.Urls
if (!$url.IsAbsoluteUri -or $url.Scheme -ne 'http' -or $url.Host -ne '127.0.0.1') {
    throw '请在 appsettings.Production.json 中配置有效的本机服务地址'
}
Start-Process -FilePath $url.AbsoluteUri
