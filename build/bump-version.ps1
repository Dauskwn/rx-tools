# 统一改版本号：src/MainForm.Core.cs (APP_VERSION + APP_VERSION_NUM) + build/program.cs + VERSION
# 用法： pwsh -File build\bump-version.ps1 1.7.5
param([Parameter(Mandatory=$true)][string]$ver)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($ver -notmatch '^\d+\.\d+(\.\d+)?$') { throw "版本号格式应为 1.7 或 1.7.5" }
$ver3 = if ($ver -match '^\d+\.\d+$') { "$ver.0" } else { $ver }

$core = Join-Path $root "src\MainForm.Core.cs"
$prog = Join-Path $root "build\program.cs"
$vf   = Join-Path $root "VERSION"

$today = Get-Date -Format 'yyyy-MM-dd'
$c = [IO.File]::ReadAllText($core, [Text.Encoding]::UTF8)
$new = [regex]::Replace($c, 'APP_VERSION\s*=\s*"V[\d\.]+"', 'APP_VERSION = "V' + $ver3 + '"')
$new = [regex]::Replace($new, 'APP_VERSION_NUM\s*=\s*"[\d\.]+"', 'APP_VERSION_NUM = "' + $ver3 + '"')
$new = [regex]::Replace($new, 'APP_BUILD_DATE\s*=\s*"[\d\-]+"', 'APP_BUILD_DATE = "' + $today + '"')
if ($new -eq $c -and $c -notmatch 'APP_VERSION\s*=\s*"V') { throw "src\MainForm.Core.cs 里找不到 APP_VERSION" }
[IO.File]::WriteAllText($core, $new, (New-Object Text.UTF8Encoding $false))

$p = [IO.File]::ReadAllText($prog, [Text.Encoding]::UTF8)
$p = [regex]::Replace($p, 'AssemblyVersion\("[\d\.]+"\)',     'AssemblyVersion("' + $ver3 + '.0")')
$p = [regex]::Replace($p, 'AssemblyFileVersion\("[\d\.]+"\)', 'AssemblyFileVersion("' + $ver3 + '.0")')
[IO.File]::WriteAllText($prog, $p, (New-Object Text.UTF8Encoding $false))

[IO.File]::WriteAllText($vf, $ver3 + "`r`n", (New-Object Text.UTF8Encoding $false))

Write-Output ("  APP_VERSION     = V" + $ver3)
Write-Output ("  APP_VERSION_NUM = " + $ver3)
Write-Output ("  APP_BUILD_DATE  = " + $today)
Write-Output ("  AssemblyVersion = " + $ver3 + ".0")
Write-Output ("  VERSION         = " + $ver3)
