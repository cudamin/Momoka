[CmdletBinding()]
param(
    [string]$Repo = '',
    [string]$Source = 'src',
    [double]$MaxPercent = 1.0,
    [int]$MaxClones = 70,
    [string]$ExistingReport,
    [string]$ReportDirectory,
    [string]$JscpdVersion = '5.4.2'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# 参数的默认值在脚本根路径就绪前求值（-File 调用时 $PSScriptRoot 可能还是空串）。
if (-not $Repo) { $Repo = Join-Path $PSScriptRoot '..' }

# 重复代码报告。用 jscpd 而不是 SonarQube：SonarQube 的 C# 分析要商业版服务器，为一份重复率
# 报告养一台 Java 服务不划算；jscpd 是 MIT、只读源码、单文件报告，C# tokenizer 够用。
#
# 预算不是「必须为 0」：0.62% 这个基线里有 63 段，其中飞牛四个设置页的骨架重复是已知的、
# 有理由的重复（同一个官方页面模板的四个薄封装）。预算卡的是「别再涨」，不是逼人为了指标拆掉
# 可读性更好的重复。超预算时先看报告里的新克隆，再决定是改代码还是改预算——改预算要写理由。

$root = [IO.Path]::GetFullPath((Join-Path $Repo '.'))
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "工作树不存在：$root" }

$report = $null
$reportPath = $null

if ($ExistingReport) {
    $reportPath = [IO.Path]::GetFullPath($ExistingReport)
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "报告不存在：$reportPath" }
} else {
    $npx = Get-Command npx -ErrorAction SilentlyContinue
    if (-not $npx) { throw '找不到 npx；重复代码检查需要 Node.js（本机已有 v22）。' }

    $sourcePath = [IO.Path]::GetFullPath((Join-Path $root $Source))
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) { throw "源码目录不存在：$sourcePath" }

    if (-not $ReportDirectory) {
        $ReportDirectory = Join-Path $root ('artifacts/quality/duplication-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
    }
    $ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
    [IO.Directory]::CreateDirectory($ReportDirectory) | Out-Null
    $log = Join-Path $ReportDirectory 'jscpd.log'
    $reportPath = Join-Path $ReportDirectory 'jscpd-report.json'

    Write-Output "正在扫描 $sourcePath（jscpd $JscpdVersion，C#，min-lines 12 / min-tokens 80）"
    & $npx.Source --yes "jscpd@$JscpdVersion" $sourcePath --format csharp --min-lines 12 --min-tokens 80 `
        --mode strict --reporters json --output $ReportDirectory --ignore '**/obj/**,**/bin/**' *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $log -Tail 15 | ForEach-Object { Write-Output $_ }
        throw "jscpd 退出码 $LASTEXITCODE；完整日志 $log"
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "jscpd 没有生成报告：$reportPath（日志 $log）" }
}

$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $report.PSObject.Properties['statistics']) { throw "报告缺少 statistics：$reportPath" }
$total = $report.statistics.total
if (-not $total) { throw "报告缺少 statistics.total：$reportPath" }

$clones = [int]$total.clones
$percentage = [double]$total.percentage
$lines = [int]$total.lines
$duplicatedLines = [int]$total.duplicatedLines

Write-Output ("重复代码：{0} 段克隆、{1} / {2} 行，占 {3:F2}%（预算 ≤ {4:F2}%、≤ {5} 段）" -f `
    $clones, $duplicatedLines, $lines, $percentage, $MaxPercent, $MaxClones)
Write-Output "报告：$reportPath"

$over = $false
if ($percentage -gt $MaxPercent) { Write-Output "超出重复率预算：$([Math]::Round($percentage, 2))% > $MaxPercent%"; $over = $true }
if ($clones -gt $MaxClones) { Write-Output "超出克隆段数预算：$clones > $MaxClones"; $over = $true }

if ($over) {
    Write-Output '先看报告里的新克隆；确认是刻意重复再调整预算，并在提交说明里写理由。'
    exit 1
}
Write-Output '重复代码在预算内。'
exit 0
