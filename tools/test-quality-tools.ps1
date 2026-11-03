[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# tools/check-diff.ps1 与 tools/check-duplication.ps1 的离线回归。不联网、不构建产品：
# 前者在临时 git 仓库里造行尾/BOM/末尾换行问题，后者直接喂一份假的 jscpd 报告验预算。
# 这不是产品行为验收，只证明两个脚本该红时红、该绿时绿。

$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$root = Join-Path $repo ('artifacts\tool-tests\' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$utf8Bom = [Text.UTF8Encoding]::new($true)
$utf8 = [Text.UTF8Encoding]::new($false)
$passed = 0

function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "[失败] $Name" }
    $script:passed++
    Write-Output "[通过] $Name"
}

function Save-Bytes([string]$File, [byte[]]$Bytes) {
    $directory = Split-Path -Parent $File
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { $null = New-Item -ItemType Directory -Path $directory }
    [IO.File]::WriteAllBytes($File, $Bytes)
}

function Run-Script([string]$File, [string[]]$Arguments = @()) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    # 被调脚本按 UTF-8 写控制台（check-diff 为了读 git 路径会显式设 UTF-8）；父进程若仍按
    # 936 解码，中文断言会拿到乱码。这一小段统一按 UTF-8 收。
    $savedEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    try {
        $output = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $File @Arguments 2>&1)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    } finally {
        [Console]::OutputEncoding = $savedEncoding
        $ErrorActionPreference = $saved
    }
}

$diffChecker = Join-Path $PSScriptRoot 'check-diff.ps1'
$dupChecker = Join-Path $PSScriptRoot 'check-duplication.ps1'

# ---- check-diff -------------------------------------------------------------------------------
$fixture = Join-Path $root 'diff'
$null = New-Item -ItemType Directory -Path $fixture
& git -C $fixture init --quiet
& git -C $fixture config user.email 'fixture@example.invalid'
& git -C $fixture config user.name 'fixture'

# 干净的新文件：UTF-8 无 BOM、LF、末尾有换行。
$clean = Join-Path $fixture 'src/Clean.cs'
Save-Bytes $clean ([Text.Encoding]::UTF8.GetBytes("namespace Fixture;`n`ninternal static class Clean { }`n"))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 0) 'check-diff 接受 LF/UTF-8/末尾换行'
Check ($result.Output -match '检查了 1 个文本文件') 'check-diff 报告检查数量'

# CRLF：上一轮真实事故的形态。
$crlf = Join-Path $fixture 'src/Crlf.cs'
Save-Bytes $crlf ([Text.Encoding]::UTF8.GetBytes("namespace Fixture;`r`n`r`ninternal static class Crlf { }`r`n"))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 1) 'check-diff 拒绝 CRLF'
Check ($result.Output -match 'Crlf.cs 使用了 CRLF') 'check-diff 点名 CRLF 文件'
Remove-Item -LiteralPath $crlf

# 末尾没有换行。
$noNewline = Join-Path $fixture 'src/NoNewline.cs'
Save-Bytes $noNewline ([Text.Encoding]::UTF8.GetBytes('namespace Fixture;'))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 1) 'check-diff 拒绝缺少末尾换行'
Remove-Item -LiteralPath $noNewline

# 普通文本文件带 BOM。
$bom = Join-Path $fixture 'src/Bom.cs'
Save-Bytes $bom ([byte[]]([Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes("namespace Fixture;`n")))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 1) 'check-diff 拒绝 .cs 的 UTF-8 BOM'
Remove-Item -LiteralPath $bom

# PowerShell 脚本缺 BOM。
$ps1 = Join-Path $fixture 'tools/no-bom.ps1'
Save-Bytes $ps1 ([Text.Encoding]::UTF8.GetBytes("Write-Output 'fixture'`n"))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 1) 'check-diff 拒绝缺 BOM 的 .ps1'
Remove-Item -LiteralPath $ps1

# 二进制文件不按文本规则看：含 NUL 的 png 应该被跳过。
$binary = Join-Path $fixture 'src/asset.png'
Save-Bytes $binary ([byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 0) 'check-diff 跳过二进制文件'
Remove-Item -LiteralPath $binary

# 非法 UTF-8（GBK 写过一次的中文注释）。
$bad = Join-Path $fixture 'src/BadEncoding.cs'
Save-Bytes $bad ([byte[]](0x2F, 0x2F, 0xD6, 0xD0, 0xCE, 0xC4, 0x0A))
$result = Run-Script $diffChecker @('-Repo', $fixture)
Check ($result.ExitCode -eq 1) 'check-diff 拒绝非法 UTF-8'
Remove-Item -LiteralPath $bad

# ---- check-duplication ------------------------------------------------------------------------
$dupDirectory = Join-Path $root 'dup'
$null = New-Item -ItemType Directory -Path $dupDirectory

function Save-Report([string]$File, [int]$Clones, [double]$Percentage) {
    $json = @{
        duplicates = @()
        statistics = @{
            formats = @{}
            total = @{ clones = $Clones; duplicatedLines = 10; duplicatedTokens = 100; lines = 1000; percentage = $Percentage; sources = 10; tokens = 1000 }
        }
    } | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($File, $json, $utf8)
}

$within = Join-Path $dupDirectory 'within.json'
Save-Report $within 10 0.5
$result = Run-Script $dupChecker @('-ExistingReport', $within)
Check ($result.ExitCode -eq 0) 'check-duplication 接受预算内的报告'
Check ($result.Output -match '0\.50%') 'check-duplication 打印实际百分比'

$overPercent = Join-Path $dupDirectory 'over-percent.json'
Save-Report $overPercent 10 2.5
$result = Run-Script $dupChecker @('-ExistingReport', $overPercent)
Check ($result.ExitCode -eq 1) 'check-duplication 拒绝超重复率'
Check ($result.Output -match '超出重复率预算') 'check-duplication 说明超预算项'

$overClones = Join-Path $dupDirectory 'over-clones.json'
Save-Report $overClones 120 0.2
$result = Run-Script $dupChecker @('-ExistingReport', $overClones)
Check ($result.ExitCode -eq 1) 'check-duplication 拒绝超克隆段数'

$missing = Join-Path $dupDirectory 'missing.json'
$result = Run-Script $dupChecker @('-ExistingReport', $missing)
Check ($result.ExitCode -ne 0) 'check-duplication 拒绝不存在的报告'

Write-Output ''
Write-Output ''
Write-Output "共 $passed 项，全部通过。"
exit 0
