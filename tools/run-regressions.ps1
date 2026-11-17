[CmdletBinding()]
param(
    [switch]$List,
    [string[]]$Suite,
    [switch]$All,
    [switch]$IncludeExternal,
    [string]$ExternalNode,
    [string]$ExternalNodeModules,
    [string]$ExternalBrowser,
    [string]$Dotnet = (Join-Path $HOME '.dotnet/dotnet.exe'),
    [string]$Packages = (Join-Path $HOME '.nuget/packages'),
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = [Text.UTF8Encoding]::new($false)

# dotnet 按 UTF-8 写控制台，而 Windows PowerShell 5.1 默认按当前代码页（本机 936）解码：
# 构建摘要里的「0 个警告」会先变成乱码，Build-Current 的文字判断便永远不匹配。统一按 UTF-8 解码。
$previousOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = $utf8

# 空仓库（离线夹具）没有 HEAD。返回空串，而不是让 git 的 fatal 在 Stop 模式下变成终止错误。
function Get-TreeHead([string]$Tree) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $value = @(& git -C $Tree rev-parse HEAD 2>$null) | Select-Object -First 1
        if ($null -eq $value) { return '' }
        return ([string]$value).Trim()
    } finally {
        $ErrorActionPreference = $saved
    }
}

function Resolve-TreePath([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw "清单路径必须相对工作树：$Relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $root $Relative))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "清单路径越过工作树：$Relative"
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "清单文件不存在：$Relative" }
    return $path
}

function Expand-Arguments($Entry, [string]$Evidence) {
    foreach ($argument in @($Entry.arguments)) {
        $value = ([string]$argument).Replace('{root}', $root).Replace('{shell}', $shell)
        $value = $value.Replace('{output}', $Evidence).Replace('{dotnet}', $dotnetPath).Replace('{packages}', $packagesPath)
        if ($value -match '\{[^}]+\}') { throw "未知清单参数：$argument" }
        $value
    }
}

function Invoke-Logged([string]$Executable, [string[]]$Arguments, [string]$Log) {
    # Each argument is passed as its own value. Never evaluate a manifest string as shell code.
    # Windows PowerShell turns redirected native stderr into an ErrorRecord. Expected diagnostics
    # must stay in the log without interrupting the child before its real exit code is observed.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        & $Executable @Arguments *> $Log
        $code = $global:LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    Get-Content -LiteralPath $Log -Tail 8 | ForEach-Object { Write-Host $_ }
    if ($null -eq $code) { throw "未取得进程退出码；日志 $Log" }
    if ($code -ne 0) { throw "退出码 $code；日志 $Log" }
}

function Build-Current([string]$Project, [string]$Evidence, [string]$Prefix) {
    Invoke-Logged $dotnetPath (@('restore', $Project, '--source', $packagesPath, '--disable-parallel',
        '-p:NuGetAudit=false') + $buildFlags) (Join-Path $Evidence "$Prefix-restore.log")
    Invoke-Logged $dotnetPath (@('build', $Project, '-c', 'Release', '--no-restore') + $buildFlags) (Join-Path $Evidence "$Prefix-build.log")
    $buildLog = Get-Content -LiteralPath (Join-Path $Evidence "$Prefix-build.log") -Raw
    if ($buildLog -notmatch '(?m)^\s*0 (个警告|Warning\(s\))\s*$' -or $buildLog -notmatch '(?m)^\s*0 (个错误|Error\(s\))\s*$') {
        throw "当前 Release 构建未确认零警告零错误；不运行旧产物。日志：$Prefix-build.log"
    }
}

function Read-CaseCounts([string]$Log) {
    $value = Get-Content -LiteralPath $Log -Raw
    $patterns = @(
        '共\s*(?<total>\d+)\s*项[，,]\s*通过\s*(?<passed>\d+)[，,]\s*失败\s*(?<failed>\d+)[，,]\s*跳过\s*(?<skipped>\d+)',
        'TOTAL\s+(?<total>\d+);\s*PASS\s+(?<passed>\d+);\s*FAIL\s+(?<failed>\d+);\s*SKIP\s+(?<skipped>\d+)',
        '(?<passed>\d+) passed,\s*(?<failed>\d+) failed,\s*(?<skipped>\d+) skipped'
    )
    foreach ($pattern in $patterns) {
        $matches = [regex]::Matches($value, $pattern)
        if ($matches.Count -gt 0) {
            $last = $matches[$matches.Count - 1]
            return [ordered]@{ passed = [int]$last.Groups['passed'].Value; failed = [int]$last.Groups['failed'].Value;
                skipped = [int]$last.Groups['skipped'].Value }
        }
    }
    $atomic = [regex]::Match($value, '(?s)\{\s*"complete"\s*:\s*true,.*?"finishedAt"\s*:\s*"[^"]+"\s*\}')
    if ($atomic.Success) {
        $summary = $atomic.Value | ConvertFrom-Json
        return [ordered]@{ passed = $summary.productPassed; failed = $summary.productFailed + $summary.controlsFailed;
            skipped = $summary.skipped + $summary.productNotExecuted + $summary.controlsNotExecuted;
            controlsPassed = $summary.controlsPassed }
    }
    return $null # Some original wrappers use JSON evidence; don't invent their case totals.
}

try {
    if (($All -and $Suite) -or ($List -and ($All -or $Suite))) { throw '-List、-Suite 和 -All 不能混用。' }
    $gitRoot = (& git -C $root rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne $root) { throw '只能运行所属 Git 工作树的脚本。' }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'regression-suites.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) { throw '不支持的专项清单版本。' }
    $entries = @($manifest.suites)
    $ids = @{}
    foreach ($entry in $entries) {
        if ($entry.id -notmatch '^Momoka\.[A-Za-z0-9.]+$' -or $ids.ContainsKey($entry.id)) { throw "专项名称无效或重复：$($entry.id)" }
        $ids[$entry.id] = $entry
        if ($entry.project -ne "tests/$($entry.id)/$($entry.id).csproj") { throw "专项项目路径不匹配：$($entry.id)" }
        $null = Resolve-TreePath $entry.project
        $null = Resolve-TreePath $entry.readme
        if ($entry.mode -notin @('dotnet', 'wrapper', 'external')) { throw "未知执行方式：$($entry.id)" }
        if ($entry.mode -in @('wrapper', 'external')) { $null = Resolve-TreePath $entry.wrapper }
        if ([string]::IsNullOrWhiteSpace($entry.notes)) { throw "专项缺少范围说明：$($entry.id)" }
        foreach ($argument in @($entry.arguments)) {
            if ([string]$argument -match '\{(?!root\}|shell\}|output\}|dotnet\}|packages\})[^}]+\}') {
                throw "未知清单参数：$argument"
            }
        }
    }
    $projects = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File | ForEach-Object { $_.BaseName }
    })
    if (@(Compare-Object @($ids.Keys | Sort-Object) @($projects | Sort-Object)).Count -ne 0) {
        throw '清单与当前顶层测试项目不一致；请登记新增专项或复核退役，不能静默漏跑。'
    }
    $shellProject = Resolve-TreePath $manifest.shellProject
    $shell = [IO.Path]::GetFullPath((Join-Path $root $manifest.shellAssembly))
    if (-not $All -and -not $Suite) {
        $entries | Select-Object id, mode, requiresShell, notes | Format-Table -AutoSize -Wrap
        Write-Output "共 $($entries.Count) 个专项。默认只列清单；使用 -Suite 精确名称或 -All 显式执行。"
        exit 0
    }
    # Validate the entire requested selection before creating output, restoring or building anything.
    if ($Suite) {
        foreach ($name in $Suite) { if (-not $ids.ContainsKey($name)) { throw "未登记的专项：$name" } }
        $selected = @($Suite | Select-Object -Unique | ForEach-Object { $ids[$_] })
    } else { $selected = $entries }
    $dotnetPath = (Resolve-Path -LiteralPath $Dotnet).Path
    $packagesPath = (Resolve-Path -LiteralPath $Packages).Path
    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf) -or -not (Test-Path -LiteralPath $packagesPath -PathType Container)) {
        throw 'SDK 必须是文件，本机包缓存必须是目录。'
    }
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/regressions/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' + [guid]::NewGuid().ToString('N')) }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $output.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
        throw '输出必须是所属工作树 artifacts 下的新目录，不覆盖已有证据。'
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    # 2026-11-15：改为默认并行与共享编译（全方案重编实测 7 分 22 秒 → 2 分 33 秒）。
    $buildFlags = @()
    $hostExe = (Get-Process -Id $PID).Path
    $results = [Collections.Generic.List[object]]::new()
    $savedEnvironment = @{}
    $environment = @{ DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'; DOTNET_CLI_TELEMETRY_OPTOUT = '1';
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'; NUGET_CERT_REVOCATION_MODE = 'offline';
        RestoreSources = $packagesPath }
    foreach ($name in $environment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    Push-Location -LiteralPath $root
    try {
        # Match global.json's rollForward policy rather than an exact patch string: latestMinor may
        # resolve to a newer installed 10.x SDK, and that is not a reason to refuse the suites.
        $sdk = (& $dotnetPath --version).Trim()
        if ($LASTEXITCODE -ne 0) { throw '读取 SDK 版本失败。' }
        $sdkSpec = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk
        $expectedSdk = [version]'0.0'
        $actualSdk = [version]'0.0'
        if (-not [version]::TryParse([string]$sdkSpec.version, [ref]$expectedSdk)) { throw "global.json 的 SDK 版本无效：$($sdkSpec.version)" }
        if (-not [version]::TryParse($sdk, [ref]$actualSdk)) { throw "无法解析 SDK 版本：$sdk" }
        $rollForward = if ($sdkSpec.PSObject.Properties.Name -contains 'rollForward') { [string]$sdkSpec.rollForward } else { '' }
        $withinPolicy = $actualSdk.Major -eq $expectedSdk.Major -and
            ($actualSdk.Minor -gt $expectedSdk.Minor -or
                ($actualSdk.Minor -eq $expectedSdk.Minor -and $actualSdk.Build -ge $expectedSdk.Build))
        if (-not $withinPolicy) {
            throw "SDK 为 $sdk，不符合 global.json 固定版本 $($sdkSpec.version)（rollForward=$rollForward）。"
        }
        # External entries are not part of the offline green path. A selection that explicitly asks
        # for one still fails when its prerequisites were not supplied; -All just records them as
        # deferred so the offline summary can be green without pretending the DOM suite ran.
        $externalEntries = @($selected | Where-Object { $_.mode -eq 'external' })
        $requiredExternalDeferred = $false
        if ($IncludeExternal -and $externalEntries.Count -gt 0) {
            foreach ($path in @($ExternalNode, $ExternalNodeModules, $ExternalBrowser)) {
                if ([string]::IsNullOrWhiteSpace($path)) { throw '运行外部专项需要同时提供 -ExternalNode、-ExternalNodeModules 和 -ExternalBrowser。' }
            }
            if (-not (Test-Path -LiteralPath $ExternalNode -PathType Leaf)) { throw "外部 Node 不存在：$ExternalNode" }
            if (-not (Test-Path -LiteralPath $ExternalNodeModules -PathType Container)) { throw "外部 NodeModules 目录不存在：$ExternalNodeModules" }
            if (-not (Test-Path -LiteralPath $ExternalBrowser -PathType Leaf)) { throw "外部浏览器不存在：$ExternalBrowser" }
        }
        $shellBuilt = $false
        foreach ($entry in $selected) {
            $evidence = Join-Path $output $entry.id
            [IO.Directory]::CreateDirectory($evidence) | Out-Null
            $result = [ordered]@{ suite = $entry.id; status = 'failed'; phase = 'preflight'; counts = $null;
                notes = $entry.notes; evidence = $evidence; error = $null }
            try {
                Write-Host "正在执行 $($entry.id)"
                if ($entry.mode -eq 'external' -and -not $IncludeExternal) {
                    $result.status = 'deferred'
                    $result.error = "$($entry.notes) 外部前置未提供；本次离线汇总不计为通过。"
                    if (-not $All) { $requiredExternalDeferred = $true }
                    continue
                }
                $runArguments = @(Expand-Arguments $entry $evidence)
                $runLog = Join-Path $evidence 'run.log'
                if ($entry.mode -eq 'external') {
                    $result.phase = 'external'
                    Invoke-Logged $hostExe (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Resolve-TreePath $entry.wrapper),
                        '-Dotnet', $dotnetPath, '-Node', [IO.Path]::GetFullPath($ExternalNode),
                        '-NodeModules', [IO.Path]::GetFullPath($ExternalNodeModules),
                        '-Browser', [IO.Path]::GetFullPath($ExternalBrowser),
                        '-OutputRoot', (Join-Path $evidence 'external')) + $runArguments) $runLog
                } elseif ($entry.mode -eq 'wrapper') {
                    $result.phase = 'wrapper'
                    Invoke-Logged $hostExe (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Resolve-TreePath $entry.wrapper)) + $runArguments) $runLog
                } else {
                    if ($entry.requiresShell -and -not $shellBuilt) {
                        $result.phase = 'shell-build'
                        Build-Current $shellProject $evidence 'shell'
                        if (-not (Test-Path -LiteralPath $shell -PathType Leaf)) { throw '当前构建未产生清单指定的 Shell DLL。' }
                        $shellBuilt = $true
                    }
                    $project = Resolve-TreePath $entry.project
                    $result.phase = 'build'
                    Build-Current $project $evidence 'suite'
                    $result.phase = 'run'
                    Invoke-Logged $dotnetPath (@('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--') + $runArguments) $runLog
                }
                $result.counts = Read-CaseCounts $runLog
                $result.status = 'passed'
                if ($null -ne $result.counts -and ($result.counts.failed -gt 0 -or $result.counts.skipped -gt 0)) {
                    $result.status = 'incomplete'
                    $result.error = '用例含失败或跳过，不能算该专项全通过。'
                }
            } catch {
                $result.error = $_.Exception.Message
                Write-Host "$($entry.id) 失败：$($result.error)"
                if (Test-Path -LiteralPath (Join-Path $evidence 'run.log')) { $result.counts = Read-CaseCounts (Join-Path $evidence 'run.log') }
            } finally {
                $results.Add([pscustomobject]$result)
                $report = [ordered]@{ root = $root; head = (Get-TreeHead $root); sdk = $sdk;
                    manifestSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'regression-suites.json') -Algorithm SHA256).Hash;
                    includeExternal = [bool]$IncludeExternal;
                    deferredExternal = @($results | Where-Object { $_.status -eq 'deferred' } | ForEach-Object { $_.suite });
                    selected = @($selected | ForEach-Object { $_.id }); suites = @($results.ToArray()) }
                [IO.File]::WriteAllText((Join-Path $output 'summary.json'), ($report | ConvertTo-Json -Depth 8) + "`n", $utf8)
            }
        }
    } finally {
        Pop-Location
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        [Console]::OutputEncoding = $previousOutputEncoding
    }
    $results | Select-Object suite, status, phase, error | Format-Table -AutoSize -Wrap
    $failedCount = @($results | Where-Object { $_.status -in @('failed', 'incomplete') }).Count
    $deferredCount = @($results | Where-Object { $_.status -eq 'deferred' }).Count
    if ($deferredCount -gt 0) {
        Write-Output "外部专项 deferred（$deferredCount 套）：本次离线汇总不算外部通过；用 -IncludeExternal 同时提供 -ExternalNode、-ExternalNodeModules、-ExternalBrowser 运行。"
    } elseif ($failedCount -eq 0) {
        Write-Output '本次选择的专项全部通过。'
    }
    Write-Output "报告：$(Join-Path $output 'summary.json')。各套覆盖可能重叠，不合计用例数。"
    if ($failedCount -gt 0 -or $requiredExternalDeferred) { exit 1 }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
