#requires -Version 5.1
<#
.SYNOPSIS
    构建 Windows 安装包（NSIS）。

.DESCRIPTION
    载荷默认取 publish\trim（先跑 tools\publish.ps1 -Mode trim）。
    本机没有 NSIS 时会自动下载官方便携版到 %TEMP%，不写系统目录、不需要管理员。
    产物默认 publish\UniversityTodos-Setup-<Version>.exe。

.PARAMETER Version
    版本号；省略时读 Directory.Build.props 的 <Version>。

.PARAMETER PayloadDir
    要打包的程序目录，默认 publish\trim，目录里必须有 UniversityTodos.Desktop.exe。

.PARAMETER NsisDir
    已解压的 NSIS 目录（包含 Bin\makensis.exe）。省略时自动下载 3.11 便携版。

.PARAMETER OutFile
    输出 exe 路径；省略时 publish\UniversityTodos-Setup-<Version>.exe。
#>
param(
    [string]$Version = '',
    [string]$PayloadDir = 'publish\trim',
    [string]$NsisDir = '',
    [string]$OutFile = '',
    # 仅用于测试迁移页：把「旧数据目录」指到临时目录；正式构建不要传。
    [string]$OldDataDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-ProjectVersion {
    $props = Join-Path $root 'Directory.Build.props'
    $m = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>'
    if (-not $m) { throw "Directory.Build.props 里没有 <Version>" }
    return $m.Matches[0].Groups[1].Value.Trim()
}

if (-not $Version) { $Version = Get-ProjectVersion }
$Version = $Version.TrimStart('v', 'V')

$payloadAbs = [System.IO.Path]::GetFullPath((Join-Path $root $PayloadDir))
$payloadExe = Join-Path $payloadAbs 'UniversityTodos.Desktop.exe'
if (-not (Test-Path $payloadExe)) {
    throw "载荷里没有 UniversityTodos.Desktop.exe：$payloadAbs`n请先运行：.\tools\publish.ps1 -Mode trim -NoPdb"
}

if (-not $OutFile) { $OutFile = Join-Path $root "publish\UniversityTodos-Setup-$Version.exe" }
$outAbs = [System.IO.Path]::GetFullPath($OutFile)
$outDir = Split-Path -Parent $outAbs
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# ---- NSIS ----
$makensis = $null
if ($NsisDir) {
    $makensis = Get-ChildItem $NsisDir -Recurse -Filter makensis.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\Bin$' } |
        Select-Object -First 1
}

if (-not $makensis) {
    if (-not $NsisDir) { $NsisDir = Join-Path $env:TEMP 'UniversityTodos-nsis-3.11' }
    $makensis = Get-ChildItem $NsisDir -Recurse -Filter makensis.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\Bin$' } |
        Select-Object -First 1
}

if (-not $makensis) {
    Write-Host '== 本机没有 NSIS，下载官方便携版（约 2.3 MB）==' -ForegroundColor Cyan
    $zip = Join-Path $env:TEMP 'nsis-3.11.zip'
    $url = 'https://sourceforge.net/projects/nsis/files/NSIS%203/3.11/nsis-3.11.zip/download'
    if (-not (Test-Path $zip) -or (Get-Item $zip).Length -lt 2000000) {
        if (Test-Path $zip) { Remove-Item $zip -Force }
        & curl.exe -L --retry 5 --retry-delay 2 --max-time 600 -o $zip $url
        if ($LASTEXITCODE -ne 0) { throw "NSIS 下载失败（curl exit $LASTEXITCODE）" }
    }
    if (-not (Test-Path $zip)) { throw 'NSIS 下载失败：文件不存在' }
    if (-not (Test-Path $NsisDir)) { New-Item -ItemType Directory -Force -Path $NsisDir | Out-Null }
    Expand-Archive -Path $zip -DestinationPath $NsisDir -Force
    $makensis = Get-ChildItem $NsisDir -Recurse -Filter makensis.exe |
        Where-Object { $_.DirectoryName -match '\\Bin$' } |
        Select-Object -First 1
    if (-not $makensis) { throw "NSIS 解压后没找到 Bin\makensis.exe：$NsisDir" }
}

Write-Host "== 构建安装包 ==" -ForegroundColor Cyan
Write-Host "   NSIS    : $($makensis.FullName)"
Write-Host "   载荷    : $payloadAbs"
Write-Host "   版本    : $Version"
Write-Host "   输出    : $outAbs"

$nsi = Join-Path $root 'installer\UniversityTodos.nsi'
$extraDefines = @()
if ($OldDataDir) { $extraDefines += "/DOLD_DATA_DIR=$([System.IO.Path]::GetFullPath($OldDataDir))" }
& $makensis.FullName /V2 "/DPAYLOAD_DIR=$payloadAbs" "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$outAbs" @extraDefines $nsi
if ($LASTEXITCODE -ne 0) { throw "makensis 失败（exit $LASTEXITCODE）" }
if (-not (Test-Path $outAbs)) { throw "安装包没有生成：$outAbs" }

$file = Get-Item $outAbs
$hash = (Get-FileHash $outAbs -Algorithm SHA256).Hash
Write-Host ''
Write-Host '== 完成 ==' -ForegroundColor Green
Write-Host "   安装包 : $($file.FullName)"
Write-Host ("   大小   : {0:N1} MB" -f ($file.Length / 1MB))
Write-Host "   SHA256 : $hash"
Write-Host ''
Write-Host "静默安装：`"$($file.FullName)`" /S /D=<目标目录>" -ForegroundColor Yellow
Write-Host '注意：安装目录必须可写（默认 %LOCALAPPDATA%\Programs\大学事项综合待办，无需管理员）。' -ForegroundColor Yellow
