param(
    [string]$OutName = ""   # 可选：指定产物文件名（默认 dist\Rx工具箱.exe）
)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$verFile = Join-Path $root "VERSION"
if (-not (Test-Path $verFile)) { throw "找不到 VERSION" }
$verFull = (Get-Content $verFile -Raw).Trim()

$csc = $null
foreach ($c in @(
        "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe")) {
    if (Test-Path $c) { $csc = $c; break }
}
if (-not $csc) { throw "未找到 csc.exe" }

$srcFiles = Get-ChildItem (Join-Path $root "src") -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $srcFiles -or $srcFiles.Count -eq 0) { throw "未找到 src\*.cs 源码文件" }
$prog = Join-Path $root "build\program.cs"
$icon = Join-Path $root "rx.ico"

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem $dist -Filter *.exe -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$args0 = @(
    "/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/utf8output",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll"
)
if (Test-Path $icon) { $args0 += "/win32icon:$icon" }

# Windows OCR（零体积原生系统 API，无额外第三方依赖）
$winmd = "C:\Windows\System32\WinMetadata"
$wrGac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime\v4.0_4.0.0.0__b77a5c561934e089\System.Runtime.WindowsRuntime.dll"
if (Test-Path $winmd) {
    foreach ($m in @("Windows.Media.winmd", "Windows.Foundation.winmd", "Windows.Graphics.winmd", "Windows.Storage.winmd", "Windows.Globalization.winmd")) {
        $f = Join-Path $winmd $m
        if (Test-Path $f) { $args0 += "/r:" + $f }
    }
    $coreRt = Join-Path (Split-Path $csc -Parent) "System.Runtime.dll"
    if (Test-Path $coreRt) { $args0 += "/r:" + $coreRt }
    if (Test-Path $wrGac) { $args0 += "/r:" + $wrGac }
}

if ($OutName -and $OutName.Trim().Length -gt 0) {
    $out = Join-Path $dist $OutName
} else {
    $out = Join-Path $dist "Rx工具箱.exe"
}
& $csc ($args0 + @("/out:$out", $prog) + $srcFiles)
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

Write-Output ""
Write-Output "  产物："
Get-ChildItem $dist -Filter *.exe | ForEach-Object {
    "    {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB)
}
