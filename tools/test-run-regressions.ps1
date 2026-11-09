[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/regression-runner-tests/' + [guid]::NewGuid().ToString('N')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith((Join-Path $root 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $output)) { throw '测试输出只能使用本树 artifacts 下的新目录。' }
$fixture = Join-Path $output 'anonymous repository'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'tools')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'packages')) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Fixture([string]$Relative, [string]$Text) {
    $path = Join-Path $fixture $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $encoding = if ($path.EndsWith('.ps1')) { [Text.UTF8Encoding]::new($true) } else { $utf8 }
    [IO.File]::WriteAllText($path, $Text, $encoding)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-regressions.ps1') -Destination (Join-Path $fixture 'tools/run-regressions.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'regression-suites.json') -Destination (Join-Path $fixture 'tools/regression-suites.json')
Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination (Join-Path $fixture 'global.json')
$manifest = [IO.File]::ReadAllText((Join-Path $fixture 'tools/regression-suites.json')) | ConvertFrom-Json
foreach ($entry in $manifest.suites) {
    Write-Fixture $entry.project '<Project />'
    Write-Fixture $entry.readme 'Anonymous runner fixture. No production code is executed.'
    if ($entry.mode -eq 'wrapper') { Write-Fixture $entry.wrapper 'throw "Wrapper must not run in this fixture."' }
}
Write-Fixture $manifest.shellProject '<Project />'
Write-Fixture $manifest.shellAssembly 'Anonymous placeholder; never loaded.'
$fake = @'
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = $PSScriptRoot
$command = if ($args.Count) { [string]$args[0] } else { '' }
[IO.File]::AppendAllText((Join-Path $root 'calls.log'), ((ConvertTo-Json -InputObject @($args) -Compress) + "`n"))
if ($command -eq '--version') { (Get-Content (Join-Path $root 'global.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sdk.version; exit 0 }
$mode = [IO.File]::ReadAllText((Join-Path $root 'mode.txt'))
if ($mode -eq $command + '-fail') { Write-Output ('synthetic ' + $command + ' failure'); exit 17 }
if ($command -eq 'build') {
    if ($mode -eq 'warning') { Write-Output '1 个警告' } else { Write-Output '0 个警告' }
    Write-Output '0 个错误'
}
if ($command -eq 'run') {
    if ($mode -in @('stderr-ok', 'stderr-fail')) { [Console]::Error.WriteLine('synthetic expected native stderr') }
    if ($mode -eq 'stderr-fail') { exit 17 }
    if ($mode -eq 'skip') { Write-Output '共 2 项，通过 1，失败 0，跳过 1'; exit 0 }
    Write-Output '共 2 项，通过 2，失败 0，跳过 0'
}
exit 0
'@
[IO.File]::WriteAllText((Join-Path $fixture 'fake-dotnet.ps1'), $fake.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($true))
& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw '匿名 Git 夹具初始化失败。' }
$hostExe = (Get-Process -Id $PID).Path
$runner = Join-Path $fixture 'tools/run-regressions.ps1'
$fakeDotnet = Join-Path $fixture 'fake-dotnet.ps1'
$suite = 'Momoka.SettingsSearchNavigationTests'
$cases = [Collections.Generic.List[object]]::new()

function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Invoke-Case([string]$Name, [string]$Mode, [string[]]$Selection, [int]$ExpectedExit, [string[]]$ExpectedCommands) {
    Write-Fixture 'mode.txt' $Mode
    Write-Fixture 'calls.log' ''
    $evidence = Join-Path $fixture "artifacts/$Name"
    $log = Join-Path $output "$Name.log"
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $hostExe -NoProfile -ExecutionPolicy Bypass -File $runner @Selection -Dotnet $fakeDotnet -Packages (Join-Path $fixture 'packages') -OutputDirectory $evidence *> $log
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    Check ($exitCode -eq $ExpectedExit) "$Name 退出码 $exitCode，期望 $ExpectedExit；$log"
    $commands = @(Get-Content -LiteralPath (Join-Path $fixture 'calls.log') -Encoding UTF8 | Where-Object { $_ } | ForEach-Object {
        $values = @((ConvertFrom-Json -InputObject $_)); [string]$values[0]
    })
    Check (($commands -join ',') -eq ($ExpectedCommands -join ',')) "$Name 调用顺序错误：$($commands -join ',')"
    $report = if (Test-Path -LiteralPath (Join-Path $evidence 'summary.json')) {
        Get-Content -LiteralPath (Join-Path $evidence 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    } else { $null }
    if ($Mode -eq 'skip') { Check ($report.suites[0].counts.skipped -eq 1 -and $report.suites[0].status -eq 'incomplete') '用例 skip 未记录为不完整。' }
    if ($Mode -eq 'build-fail') { Check ($report.suites[0].phase -eq 'build' -and $report.suites[0].status -eq 'failed') '编译失败阶段未记录。' }
    if ($Name -eq 'external') { Check ($report.suites[0].status -eq 'skipped') '外部依赖未记 skip。' }
    if ($Name -like 'native-stderr-*') {
        $runText = [IO.File]::ReadAllText((Join-Path $evidence "$suite/run.log"))
        Check ($runText.Contains('synthetic expected native stderr')) '原生 stderr 证据丢失。'
        if ($Mode -eq 'stderr-ok') {
            Check ($report.suites[0].status -eq 'passed' -and $report.suites[0].counts.passed -eq 2 -and
                $report.suites[0].counts.failed -eq 0 -and $report.suites[0].counts.skipped -eq 0) 'stderr 成功未保留完整用例结论。'
        } else {
            Check ($report.suites[0].status -eq 'failed' -and $report.suites[0].phase -eq 'run' -and
                $report.suites[0].error.Contains('退出码 17')) 'stderr 失败未取得真实退出码17。'
        }
    }
    $cases.Add([pscustomobject]@{ name = $Name; exit = $exitCode; commands = $commands; passed = $true })
    Write-Output "通过 $Name"
}

Invoke-Case 'list' 'ok' @('-List') 0 @()
Invoke-Case 'default-list' 'ok' @() 0 @()
Invoke-Case 'success' 'ok' @('-Suite', $suite) 0 @('--version', 'restore', 'build', 'run')
Invoke-Case 'site-dialog-flow' 'ok' @('-Suite', 'Momoka.SiteDialogFlowTests') 0 @('--version', 'restore', 'build', 'run')
Invoke-Case 'architecture-current-products' 'ok' @('-Suite', 'Momoka.ArchitectureTests') 0 @('--version', 'restore', 'build', 'restore', 'build', 'run')
$architectureCalls = @(Get-Content -LiteralPath (Join-Path $fixture 'calls.log') -Encoding UTF8 | Where-Object { $_ })
Check ($architectureCalls[2].Contains('Momoka.Shell.csproj')) '架构专项必须先构建当前 Shell 及其 Core 引用。'
Invoke-Case 'restore-failure' 'restore-fail' @('-Suite', $suite) 1 @('--version', 'restore')
Invoke-Case 'build-failure' 'build-fail' @('-Suite', $suite) 1 @('--version', 'restore', 'build')
Invoke-Case 'build-warning' 'warning' @('-Suite', $suite) 1 @('--version', 'restore', 'build')
Invoke-Case 'run-failure' 'run-fail' @('-Suite', $suite) 1 @('--version', 'restore', 'build', 'run')
Invoke-Case 'case-skip' 'skip' @('-Suite', $suite) 1 @('--version', 'restore', 'build', 'run')
$scriptDotnet = $fakeDotnet
$fakeDotnet = Join-Path $fixture 'native-dotnet.cmd'
[IO.File]::WriteAllText($fakeDotnet, '@echo off' + "`r`n" +
    '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-dotnet.ps1" %*' + "`r`n" +
    'exit /b %ERRORLEVEL%' + "`r`n", [Text.Encoding]::ASCII)
try {
    Invoke-Case 'native-stderr-success' 'stderr-ok' @('-Suite', $suite) 0 @('--version', 'restore', 'build', 'run')
    Invoke-Case 'native-stderr-failure' 'stderr-fail' @('-Suite', $suite) 1 @('--version', 'restore', 'build', 'run')
} finally { $fakeDotnet = $scriptDotnet }
# Exercise the actual invocation function with no process exit code after an earlier successful call.
$invokeTokens = $null
$invokeErrors = $null
$invokeTree = [Management.Automation.Language.Parser]::ParseFile($runner, [ref]$invokeTokens, [ref]$invokeErrors)
$invokeFunction = $invokeTree.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Logged' }, $true)
Check ($null -ne $invokeFunction -and $invokeErrors.Count -eq 0) '无法定位真实 Invoke-Logged。'
& {
    Invoke-Expression $invokeFunction.Extent.Text
    Write-Fixture 'no-exit.ps1' 'return'
    $global:LASTEXITCODE = 0
    $rejection = ''
    try { Invoke-Logged (Join-Path $fixture 'no-exit.ps1') @() (Join-Path $output 'missing-exit-code.log') }
    catch { $rejection = $_.Exception.Message }
    Check ($rejection.Contains('未取得进程退出码')) '未取得退出码时沿用了旧退出码0。'
}
$cases.Add([pscustomobject]@{ name = 'missing-exit-code'; passed = $true })
Invoke-Case 'unknown' 'ok' @('-Suite', 'Momoka.DoesNotExist') 1 @()
Invoke-Case 'external' 'ok' @('-Suite', 'Momoka.ServerSettingsTabs.Tests') 1 @('--version')
Invoke-Case 'wrapper-failure' 'ok' @('-Suite', 'Momoka.DetailDeleteCommandTests') 1 @('--version')
$deleteSource = [IO.File]::ReadAllText((Join-Path $root 'tests/Momoka.DetailDeleteCommandTests/Run-Fixture.ps1')).TrimStart([char]0xFEFF)
$deleteBoundary = '$commands = [Collections.Generic.List[string]]::new()'
$deleteEnd = $deleteSource.IndexOf($deleteBoundary, [StringComparison]::Ordinal)
Check ($deleteEnd -gt 0 -and $deleteSource.IndexOf($deleteBoundary, $deleteEnd + $deleteBoundary.Length, [StringComparison]::Ordinal) -lt 0) 'Delete 预检提取边界缺失或不唯一。'
$wrapper = @'
[IO.File]::WriteAllText((Join-Path $root 'delete-output.txt'), $evidence)
Write-Output '共 2 项，通过 2，失败 0，跳过 0'
'@
Write-Fixture 'tests/Momoka.DetailDeleteCommandTests/Run-Fixture.ps1' ($deleteSource.Substring(0, $deleteEnd) + $wrapper)
Invoke-Case 'wrapper-success' 'ok' @('-Suite', 'Momoka.DetailDeleteCommandTests') 0 @('--version')
Check ([IO.File]::ReadAllText((Join-Path $fixture 'delete-output.txt')) -eq [IO.Path]::GetFullPath((Join-Path $fixture 'artifacts/wrapper-success/Momoka.DetailDeleteCommandTests/delete'))) 'Delete 未采用原允许范围内的统一证据目录。'

# Execute the actual caller wrapper's complete path preflight, with only the build/run tail inert.
# The previous manifest supplied artifacts/regressions/... and this unchanged guard rejects it.
$callerSource = [IO.File]::ReadAllText((Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/Run-Caller.ps1')).TrimStart([char]0xFEFF)
$callerBoundary = '$utf8 = [Text.UTF8Encoding]::new($false)'
$callerEnd = $callerSource.IndexOf($callerBoundary, [StringComparison]::Ordinal)
Check ($callerEnd -gt 0 -and $callerSource.IndexOf($callerBoundary, $callerEnd + $callerBoundary.Length, [StringComparison]::Ordinal) -lt 0) 'Caller 预检提取边界缺失或不唯一。'
$callerTail = @'
[IO.File]::WriteAllText((Join-Path $root 'caller-output.txt'), $output)
Write-Output ('Caller evidence: ' + $output)
Write-Output '共 2 项，通过 2，失败 0，跳过 0'
'@
Write-Fixture 'tests/Momoka.NaturalContinuationCallerTests/Run-Caller.ps1' ($callerSource.Substring(0, $callerEnd) + $callerTail)
Invoke-Case 'caller-output-preflight' 'ok' @('-Suite', 'Momoka.NaturalContinuationCallerTests') 0 @('--version')
$callerOutput = [IO.File]::ReadAllText((Join-Path $fixture 'caller-output.txt'))
Check ($callerOutput.StartsWith((Join-Path $fixture 'artifacts/r046-caller') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
    (Test-Path -LiteralPath $callerOutput -PathType Container)) 'Caller 未采用原允许目录内的实际新输出。'

# -File transports scalar CLI values; use a separate script to exercise PowerShell's actual array
# binding, including a valid selection followed by an invalid one before any SDK call.
$arrayDriver = @'
param([string]$Name, [string]$Kind)
$root = $PSScriptRoot
$selected = if ($Kind -eq 'unknown') { @('Momoka.SettingsSearchNavigationTests', 'Momoka.DoesNotExist') }
    else { @('Momoka.HomeOrderProbeTests', 'Momoka.SettingsLifetimeTests') }
& (Join-Path $root 'tools/run-regressions.ps1') -Suite $selected -Dotnet (Join-Path $root 'fake-dotnet.ps1') -Packages (Join-Path $root 'packages') -OutputDirectory (Join-Path $root "artifacts/$Name")
exit $LASTEXITCODE
'@
Write-Fixture 'array-driver.ps1' $arrayDriver
foreach ($kind in @('unknown', 'shell')) {
    Write-Fixture 'calls.log' ''
    Write-Fixture 'mode.txt' 'ok'
    $name = "array-$kind"
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $hostExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'array-driver.ps1') -Name $name -Kind $kind *> (Join-Path $output "$name.log")
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    $calls = @(Get-Content -LiteralPath (Join-Path $fixture 'calls.log') -Encoding UTF8 | Where-Object { $_ })
    if ($kind -eq 'unknown') { Check ($exitCode -eq 1 -and $calls.Count -eq 0) '混合未知选择开始了部分执行。' }
    else {
        Check ($exitCode -eq 0) '多 Shell 专项调度失败。'
        $shellBuilds = @($calls | ForEach-Object { ,@((ConvertFrom-Json -InputObject $_)) } | Where-Object { $_[0] -eq 'build' -and $_[1] -like '*Momoka.Shell.csproj' })
        Check ($shellBuilds.Count -eq 1) '同次两个 Shell 专项没有共享当前成功构建。'
        $report = Get-Content -LiteralPath (Join-Path $fixture "artifacts/$name/summary.json") -Raw -Encoding UTF8 | ConvertFrom-Json
        Check (@($report.suites).Count -eq 2) '多选专项报告不完整。'
    }
    $cases.Add([pscustomobject]@{ name = $name; exit = $exitCode; passed = $true })
    Write-Output "通过 $name"
}

Write-Fixture 'tests/Momoka.NewUnregistered/Momoka.NewUnregistered.csproj' '<Project />'
Invoke-Case 'manifest-incomplete' 'ok' @('-Suite', $suite) 1 @()
# Unlike the anonymous tree above, this checks the real repository project inventory.
& $hostExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-regressions.ps1') -List *> (Join-Path $output 'actual-manifest.log')
Check ($LASTEXITCODE -eq 0) '实际仓库的顶层测试项目必须全部登记。'
$cases.Add([pscustomobject]@{ name = 'actual-manifest-complete'; exit = 0; passed = $true })
[IO.File]::WriteAllText((Join-Path $output 'results.json'), ($cases.ToArray() | ConvertTo-Json -Depth 5) + "`n", $utf8)
Write-Output "共 $($cases.Count) 项，通过 $($cases.Count)，失败 0，跳过 0。匿名脚本边界，不执行 .NET 或生产测试；证据 $output"
