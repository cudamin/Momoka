[CmdletBinding()]
param(
    [string]$Repo = '',
    [switch]$Tests,
    [switch]$SkipBuild,
    [switch]$SkipDuplication
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$LASTEXITCODE = 0
# 子脚本与 dotnet 都按 UTF-8 写控制台；不解码成 UTF-8 的话中文汇总会显示成乱码。
$previousOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
# 参数的默认值在脚本根路径就绪前求值（-File 调用时 $PSScriptRoot 可能还是空串）。
if (-not $Repo) { $Repo = Join-Path $PSScriptRoot '..' }

# 一个入口跑完这套质量工具。它不发明新规则，只是把仓库既有的闸门与新增的检查串起来：
#   编译 + 空白        —— 闸门 1 的两条，参数与 docs/开发与验证.md 完全一致
#   脚本检查           —— tools/check-scripts.ps1（BOM、LF、语法）
#   文档链接           —— tools/check-docs.py（相对路径、锚点与技能索引）
#   官网部署镜像       —— tools/check-website-mirror.py（静态源文件与 dist）
#   文档命令           —— tools/check-doc-commands.ps1（PowerShell 代码块，只解析）
#   证据凭据           —— tools/check-report-credentials.py（只报位置与类别）
#   架构依赖           —— tests/Momoka.ArchitectureTests（NetArchTest）
#   重复代码           —— tools/check-duplication.ps1（jscpd，预算制）
#   Git 变更检查       —— tools/check-diff.ps1（CRLF、末尾换行、BOM、空白）
#   单元测试（可选）    —— tools/run-regressions.ps1 -All（离线专项；外部 DOM 记为 deferred）
#
# 单独跑某一项时直接用对应脚本或 dotnet 命令；这里不复制它们的参数表。

$root = [IO.Path]::GetFullPath((Join-Path $Repo '.'))
if (-not (Test-Path -LiteralPath (Join-Path $root 'Momoka.sln') -PathType Leaf)) { throw "不是 Momoka 工作树：$root" }
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "找不到 $dotnet；本项目固定用用户级 .NET 10 SDK。" }

$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$evidence = Join-Path $root "artifacts/quality/$stamp"
[IO.Directory]::CreateDirectory($evidence) | Out-Null

$results = [Collections.Generic.List[object]]::new()

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Output ''
    Write-Output "== $Name =="
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        if ($LASTEXITCODE -ne 0) { throw "退出码 $LASTEXITCODE" }
        $results.Add([pscustomobject]@{ Name = $Name; Ok = $true; Detail = "$([Math]::Round($watch.Elapsed.TotalSeconds, 1)) s" })
        Write-Output "[通过] $Name"
    } catch {
        $results.Add([pscustomobject]@{ Name = $Name; Ok = $false; Detail = $_.Exception.Message })
        Write-Output "[失败] $Name —— $($_.Exception.Message)"
    }
}

function Invoke-ChildScript([string]$Name, [string]$File, [string[]]$Arguments) {
    Invoke-Step $Name {
        $hostExe = (Get-Process -Id $PID).Path
        & $hostExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root $File) @Arguments
    }
}

Push-Location -LiteralPath $root
try {
    $buildPassed = $false
    if (-not $SkipBuild) {
        Invoke-Step '编译（Release，0 警告 0 错误）' {
            $log = Join-Path $evidence 'build.log'
            # 2026-11-15：不再传 -m:1 / BuildInParallel=false / UseSharedCompilation=false /
            # MSBuildNodeReuse=false。实测全方案重编 7 分 22 秒 → 2 分 33 秒，0 警告 0 错误。
            $buildArgs = @('build', './Momoka.sln', '-c', 'Release', '--no-restore')
            Write-Output "正在编译（Release，日志 $log）……"
            # 原生命令写到 stderr 的一行不能在 Stop 模式下变成异常；成败只看退出码与告警计数。
            $savedEa = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try { & $dotnet @buildArgs *> $log } finally { $ErrorActionPreference = $savedEa }
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Output $_ }
                throw "构建失败，退出码 $LASTEXITCODE，日志 $log"
            }
            $lines = @(Get-Content -LiteralPath $log)
            $warnings = @($lines | Where-Object { "$_" -match ': warning ' }).Count
            $errors = @($lines | Where-Object { "$_" -match ': error ' }).Count
            if ($warnings -gt 0 -or $errors -gt 0) { throw "构建有 $warnings 条警告、$errors 条错误，日志 $log" }
        }
        $buildPassed = $results[$results.Count - 1].Ok

        Invoke-Step '空白检查（dotnet format whitespace）' {
            $log = Join-Path $evidence 'format-whitespace.log'
            # dotnet format 会把「加载工作区时遇到警告」写到 stderr，退出码仍是 0；不能用异常判成败。
            $savedEa = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try { & $dotnet format whitespace ./Momoka.sln --verify-no-changes --no-restore *> $log } finally { $ErrorActionPreference = $savedEa }
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Output $_ }
                throw "格式检查失败，退出码 $LASTEXITCODE，日志 $log"
            }
        }
    }

    Invoke-ChildScript 'PowerShell 脚本检查' 'tools/check-scripts.ps1' @()

    Invoke-Step '文档链接检查（相对路径、锚点与技能索引）' {
        $log = Join-Path $evidence 'docs-links.log'
        $savedEa = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { & py (Join-Path $root 'tools/check-docs.py') *> $log } finally { $ErrorActionPreference = $savedEa }
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Output $_ }
            throw "文档链接检查失败，日志 $log"
        }
    }

    Invoke-Step '官网静态部署镜像检查（源文件与 dist）' {
        $checkLog = Join-Path $evidence 'website-mirror.log'
        $selfTestLog = Join-Path $evidence 'website-mirror-self-test.log'
        $savedEa = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & py (Join-Path $root 'tools/check-website-mirror.py') *> $checkLog
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath $checkLog -Tail 20 | ForEach-Object { Write-Output $_ }
                throw "官网静态部署镜像检查失败，日志 $checkLog"
            }
            & py (Join-Path $root 'tools/check-website-mirror.py') --self-test *> $selfTestLog
        } finally { $ErrorActionPreference = $savedEa }
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $selfTestLog -Tail 20 | ForEach-Object { Write-Output $_ }
            throw "官网静态部署镜像自测失败，日志 $selfTestLog"
        }
    }

    Invoke-ChildScript '文档命令语法（仅解析）' 'tools/check-doc-commands.ps1' @()

    Invoke-Step '报告凭据扫描（PROGRESS、台账与归档）' {
        $log = Join-Path $evidence 'report-credentials.log'
        $savedEa = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { & py (Join-Path $root 'tools/check-report-credentials.py') *> $log } finally { $ErrorActionPreference = $savedEa }
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Output $_ }
            throw "报告凭据扫描失败，日志 $log"
        }
    }

    if (-not $SkipBuild -and -not $buildPassed) {
        $results.Add([pscustomobject]@{ Name = '架构依赖（NetArchTest）'; Ok = $false; Detail = '未执行：当前产品构建未通过。' })
        Write-Output '[未执行] 架构依赖：当前产品构建未通过，不能使用旧 DLL。'
    } elseif ($SkipBuild) {
        # Skipping the whole solution build does not authorize trusting stale product assemblies.
        Invoke-ChildScript '架构依赖（当前产品编译后）' 'tools/run-regressions.ps1' @('-Suite', 'Momoka.ArchitectureTests')
    } else { Invoke-Step '架构依赖（NetArchTest）' {
        $log = Join-Path $evidence 'architecture.log'
        & $dotnet run --project ./tests/Momoka.ArchitectureTests/Momoka.ArchitectureTests.csproj `
            -c Release --no-build --no-restore -- $root *> $log
        Get-Content -LiteralPath $log -Tail 12 | ForEach-Object { Write-Output $_ }
    } }

    if (-not $SkipDuplication) {
        Invoke-ChildScript '重复代码（jscpd 预算）' 'tools/check-duplication.ps1' @('-ReportDirectory', (Join-Path $evidence 'duplication'))
    }

    Invoke-ChildScript 'Git 变更检查' 'tools/check-diff.ps1' @()

    if ($Tests) {
        Invoke-ChildScript '离线专项（外部 DOM 记为 deferred）' 'tools/run-regressions.ps1' @('-All', '-OutputDirectory', (Join-Path $evidence 'regressions'))
    }
} finally {
    Pop-Location
    [Console]::OutputEncoding = $previousOutputEncoding
}

Write-Output ''
Write-Output '== 汇总 =='
$results | ForEach-Object { Write-Output ("{0}  {1}  {2}" -f $(if ($_.Ok) { '[通过]' } else { '[失败]' }), $_.Name, $_.Detail) }
$failed = @($results | Where-Object { -not $_.Ok }).Count
Write-Output ''
Write-Output "共 $($results.Count) 项，通过 $($results.Count - $failed)，失败 $failed。证据：$evidence"
if ($failed -gt 0) { exit 1 }
exit 0
