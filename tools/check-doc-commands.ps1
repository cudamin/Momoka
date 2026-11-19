[CmdletBinding()]
param(
    [string]$Repo = '',
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $Repo) { $Repo = Split-Path -Parent $PSScriptRoot }
$repoRoot = (Resolve-Path -LiteralPath $Repo).Path

if (-not $Path) {
    $relative = @(& git -C $repoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw '无法枚举当前工作树的 Markdown 文档。' }
    $Path = @($relative | Sort-Object -Unique | ForEach-Object { Join-Path $repoRoot $_ })
}
if ($Path.Count -eq 0) { throw '没有可检查的 Markdown 文档。' }

$failures = [Collections.Generic.List[string]]::new()
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$blockCount = 0
foreach ($file in $Path) {
    $resolved = (Resolve-Path -LiteralPath $file).Path
    $lines = [IO.File]::ReadAllText($resolved, $utf8).TrimStart([char]0xFEFF) -split '\r?\n'
    $fence = ''
    $isPowerShell = $false
    $startLine = 0
    $body = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if (-not $fence) {
            if ($line -match '^ {0,3}(?<fence>`{3,}|~{3,})(?<info>[^\r\n]*)$') {
                $fence = $Matches['fence']
                $isPowerShell = $Matches['info'].Trim() -match '^(powershell|pwsh)(\s|$)'
                $startLine = $index + 2
                $body.Clear()
            }
            continue
        }
        $closing = '^ {0,3}' + [regex]::Escape([string]$fence[0]) + '{' + $fence.Length + ',}\s*$'
        if ($line -match $closing) {
            if ($isPowerShell) {
                $tokens = $null
                $errors = $null
                # ParseInput only builds an AST. Never invoke the example, AST or a script block.
                $null = [Management.Automation.Language.Parser]::ParseInput(($body -join "`n"), [ref]$tokens, [ref]$errors)
                $blockCount++
                foreach ($parseIssue in $errors) {
                    $number = $startLine + $parseIssue.Extent.StartLineNumber - 1
                    $failures.Add("${resolved}:${number}: $($parseIssue.ErrorId)")
                }
            }
            $fence = ''
            continue
        }
        if ($isPowerShell) { $body.Add($line) }
    }
    if ($fence -and $isPowerShell) { $failures.Add("${resolved}:$($startLine - 1): 未闭合的 PowerShell 代码块。") }
}

foreach ($failure in $failures) { Write-Output $failure }
Write-Output "文档命令语法检查：$($Path.Count) 个文档，$blockCount 个 PowerShell/pwsh 代码块，$($failures.Count) 个问题；仅解析，不执行命令。"
if ($failures.Count -gt 0) { exit 1 }
exit 0
