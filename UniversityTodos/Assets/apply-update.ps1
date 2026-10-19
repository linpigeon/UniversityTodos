<#
  更新替换器（由主程序释放到临时目录后启动）。

  职责只有一件：**在父进程退出后，把目标 exe 换成新版本，然后重启它。**

  为什么必须是独立进程：Windows 上运行中的 exe 不能覆盖、不能删除，
  但**可以改名**（已实测）。所以父进程先启动本脚本、再自行退出，
  等锁释放后这里再替换；直接覆盖失败时用「改名让位」兜底。

  安全铁律：**本脚本只允许修改一个文件——目标 exe。**
  不删目录、不动 exe 旁边的 *.WebView2（那是登录态缓存）、不碰 %LOCALAPPDATA%。

  用 Windows PowerShell 5.1 兼容语法书写（不依赖 PowerShell 7）。
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$TargetExe,
  [Parameter(Mandatory = $true)][string]$NewExe,
  [Parameter(Mandatory = $true)][int]$ParentPid,
  [string]$ExpectedSha256 = '',
  # 重启时要带上的参数，**每行一个**地写在这个文件里，而不是走命令行。
  #
  # 为什么不用 string[] 参数：PowerShell 从命令行绑定数组时，
  # `-RestartArgs a b` 只把 a 绑给参数、b 被当成无法识别的位置参数，
  # 脚本会直接报错退出（实测）。而应用侧无论用 ArgumentList 还是空格拼接，
  # 都绕不开这个绑定规则。用文件传递彻底规避，且不受参数里的空格/引号/& 影响。
  [string]$ArgsFile = '',
  [switch]$Restart
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:TEMP 'UniversityTodos-update.log'

function Write-Log {
  param([string]$Message)
  $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
  try { Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8 } catch { }
  Write-Host $line
}

function Get-Sha256 {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return $null }
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

# 按 Windows 命令行规则给单个参数加引号（反斜杠只在引号前才需要翻倍）。
# 原先只做 `"..." + $_ -replace '"','\"' + '"'`，以反斜杠结尾的参数
# （如 `--target D:\sandbox\`）会把结尾的 `\"` 变成转义引号，后续参数被并进同一个参数。
function ConvertTo-QuotedArg {
  param([string]$Value)
  if ($Value -eq '') { return '""' }
  if ($Value -notmatch '[\s"]') { return $Value }

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.Append('"')
  $backslashes = 0
  foreach ($ch in $Value.ToCharArray()) {
    if ($ch -eq '\') { $backslashes++; continue }
    if ($ch -eq '"') {
      [void]$sb.Append('\' * ($backslashes * 2 + 1))
      [void]$sb.Append('"')
      $backslashes = 0
      continue
    }
    if ($backslashes -gt 0) { [void]$sb.Append('\' * $backslashes); $backslashes = 0 }
    [void]$sb.Append($ch)
  }
  if ($backslashes -gt 0) { [void]$sb.Append('\' * ($backslashes * 2)) }
  [void]$sb.Append('"')
  return $sb.ToString()
}

$exitCode = 0
# 失败时是否可以把目标 exe 当作「可信旧版本」重新拉起；替换/回滚成功前一直为 true。
$safeToRestart = $true
try {
  Write-Log "更新器启动：目标=$TargetExe"
  Write-Log "            新文件=$NewExe 父进程=$ParentPid"

  if (-not (Test-Path -LiteralPath $TargetExe)) { throw "目标程序不存在：$TargetExe" }
  if (-not (Test-Path -LiteralPath $NewExe)) { throw "新版本文件不存在：$NewExe" }

  # 进入替换前先确认新文件本身是完整的（校验放在最前面，避免把坏文件换上去）。
  if ($ExpectedSha256) {
    $actualNew = Get-Sha256 -Path $NewExe
    if ($actualNew -ne $ExpectedSha256.ToUpperInvariant()) {
      throw "新文件哈希不符：期望 $ExpectedSha256，实际 $actualNew"
    }
    Write-Log '新文件哈希校验通过'
  } else {
    Write-Log '警告：未提供哈希，跳过新文件校验（依赖主程序侧的解包校验）'
  }

  # ---- 等父进程退出 ----
  $deadline = (Get-Date).AddSeconds(30)
  while ((Get-Date) -lt $deadline) {
    $alive = $null
    try { $alive = Get-Process -Id $ParentPid -ErrorAction Stop } catch { $alive = $null }
    if ($null -eq $alive) { break }
    Start-Sleep -Milliseconds 250
  }
  $still = $null
  try { $still = Get-Process -Id $ParentPid -ErrorAction Stop } catch { $still = $null }
  if ($null -ne $still) {
    Write-Log '父进程 30 秒内未退出，强制结束它'
    try { Stop-Process -Id $ParentPid -Force -ErrorAction Stop } catch { }
    Start-Sleep -Milliseconds 1500
  }
  Write-Log '父进程已退出，开始替换'

  # ---- 替换：先无条件改名备份，再放入新文件 ----
  #
  # 顺序不能反过来。原先直接 `Move-Item -Force` 覆盖目标：目标没被占用时（最常见）
  # 这一步会成功，于是**根本不会创建 .old 备份**；一旦事后哈希校验失败，
  # 回滚分支因为找不到 .old 而直接 throw，坏/未验证的 exe 留在安装位置，
  # 与「替换后校验失败会回滚」的承诺相反。
  # 同卷改名是原子操作，而且文件被占用时也允许改名。
  $oldPath = "$TargetExe.old"
  $safeToRestart = $true   # 目标位置当前是否是「可信的旧版本」

  $backupDone = $false
  for ($i = 1; $i -le 20; $i++) {
    try {
      if (Test-Path -LiteralPath $oldPath) { Remove-Item -LiteralPath $oldPath -Force -ErrorAction SilentlyContinue }
      Move-Item -LiteralPath $TargetExe -Destination $oldPath -Force -ErrorAction Stop
      $backupDone = $true
      break
    } catch {
      Start-Sleep -Milliseconds 500
    }
  }
  if (-not $backupDone) {
    throw '替换失败：无法把旧版本改名备份（文件可能被其他程序占用，如杀毒软件/资源管理器）。'
  }

  $swapped = $false
  for ($i = 1; $i -le 20; $i++) {
    try {
      Move-Item -LiteralPath $NewExe -Destination $TargetExe -Force -ErrorAction Stop
      $swapped = $true
      $safeToRestart = $false   # 新文件尚未校验
      break
    } catch {
      Start-Sleep -Milliseconds 500
    }
  }

  if (-not $swapped) {
    # 新文件放不进去：把旧版本恢复回来，保证用户至少还能用。
    # 先标记「目标不可信」：跨卷 Move 中途失败可能留下截断的目标文件，不能当成旧版本重启。
    $safeToRestart = $false
    try {
      if (Test-Path -LiteralPath $TargetExe) { Remove-Item -LiteralPath $TargetExe -Force -ErrorAction SilentlyContinue }
      Move-Item -LiteralPath $oldPath -Destination $TargetExe -Force -ErrorAction Stop
      $safeToRestart = $true
      Write-Log '新文件落位失败，已恢复旧版本'
    } catch {
      Write-Log '新文件落位失败，且恢复旧版本失败——请手动重新安装'
    }
    throw '替换失败：新文件无法写入安装目录（杀毒软件/资源管理器可能占用）。'
  }

  # ---- 校验替换结果 ----
  if ($ExpectedSha256) {
    $actual = Get-Sha256 -Path $TargetExe
    if ($actual -ne $ExpectedSha256.ToUpperInvariant()) {
      # 换上去的文件不对：删掉坏目标、恢复旧版本。
      try {
        if (Test-Path -LiteralPath $TargetExe) { Remove-Item -LiteralPath $TargetExe -Force -ErrorAction Stop }
        Move-Item -LiteralPath $oldPath -Destination $TargetExe -Force -ErrorAction Stop
        $safeToRestart = $true
        Write-Log '替换后校验失败，已回滚到旧版本'
      } catch {
        Write-Log '替换后校验失败，且回滚失败——请手动重新安装'
      }
      throw "替换后校验失败：期望 $ExpectedSha256，实际 $actual"
    }
    Write-Log '替换后哈希校验通过'
  }
  $safeToRestart = $true

  # 清掉旧版本备份（回滚已不可能，但此刻新版本已验证可用）
  if (Test-Path -LiteralPath $oldPath) {
    try { Remove-Item -LiteralPath $oldPath -Force -ErrorAction Stop; Write-Log '已清理旧版本备份' }
    catch { Write-Log '旧版本备份删除失败（可忽略，下次启动会再试）' }
  }

  # ---- 重启 ----
  if ($Restart) {
    # 从文件读回要转发的参数（每行一个）。读不到就当没有参数，不要因此失败。
    $restartArgs = @()
    if ($ArgsFile -and (Test-Path -LiteralPath $ArgsFile)) {
      try {
        $restartArgs = @(Get-Content -LiteralPath $ArgsFile -Encoding UTF8 |
          Where-Object { $_.Length -gt 0 })
        Write-Log "将转发 $($restartArgs.Count) 个参数"
      } catch {
        Write-Log "读取参数文件失败（将不带参数重启）：$($_.Exception.Message)"
        $restartArgs = @()
      }
    }

    if ($restartArgs.Count -eq 0) {
      Start-Process -FilePath $TargetExe -WorkingDirectory (Split-Path -Parent $TargetExe)
    } else {
      # 逐个参数按命令行规则加引号，避免含空格的路径/反斜杠结尾被拆错。
      $argString = ($restartArgs | ForEach-Object { ConvertTo-QuotedArg $_ }) -join ' '
      Start-Process -FilePath $TargetExe -ArgumentList $argString -WorkingDirectory (Split-Path -Parent $TargetExe)
    }
    Write-Log '已重启新版本'
  } else {
    Write-Log '已替换（未要求重启）'
  }

  Write-Log '更新完成'
}
catch {
  $exitCode = 1
  Write-Log "更新失败：$($_.Exception.Message)"

  # 应用已经按「更新已就绪」退出了。失败时必须把旧版本重新拉起来，
  # 否则用户看到的是程序凭空消失，而失败原因只留在 %TEMP% 的日志里。
  # 只有目标位置确实是可信旧版本（$safeToRestart）时才重启——
  # 回滚失败时目标可能是个坏文件，那就不该执行它。
  if ($Restart -and $safeToRestart -and (Test-Path -LiteralPath $TargetExe)) {
    try {
      Start-Process -FilePath $TargetExe -WorkingDirectory (Split-Path -Parent $TargetExe)
      Write-Log '已重新启动旧版本'
    } catch {
      Write-Log "重新启动旧版本失败：$($_.Exception.Message)"
    }
  }
}

# ---- 清理本次下载目录 ----
# 只删「本次更新自己的下载目录」，且**只删它自己**：
# 传进来的是下载目录路径，不是安装目录——绝不在这里碰目标 exe 所在目录。
if ($NewExe) {
  try {
    $downloadDir = Split-Path -Parent $NewExe
    $tempRoot = [System.IO.Path]::GetTempPath().TrimEnd('\')
    # 双保险，避免递归删错目录：
    #   ① 必须在临时目录下；
    #   ② 路径里必须含「\UniversityTodos\」这一段（即 %TEMP%\UniversityTodos\update）。
    # 注意不能只比较叶子目录名：下载目录的叶子是 "update"，不是 "UniversityTodos"
    # ——演练时正是这里写错，导致清理被静默跳过。
    $looksOurs = $downloadDir -match '\\UniversityTodos\\[^\\]+$'
    if ($downloadDir -and
        $downloadDir.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        $looksOurs) {
      Remove-Item -LiteralPath $downloadDir -Recurse -Force -ErrorAction SilentlyContinue
      Write-Log "已清理下载目录：$downloadDir"
    } else {
      Write-Log "跳过下载目录清理（不像本程序的下载目录）：$downloadDir"
    }
  } catch { }
}

exit $exitCode
