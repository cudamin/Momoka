[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$PackagesPath,
    [Parameter(Mandatory)][string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$evidence = [IO.Path]::GetFullPath($EvidencePath)
$insidePrefix = (Join-Path $root 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $evidence.StartsWith($insidePrefix, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $evidence)) {
    throw '证据路径必须是本树 artifacts 下的新目录。'
}
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$project = Join-Path $PSScriptRoot 'Momoka.PerfListDiagnosticsTests.csproj'
$source = Join-Path $root 'src\Momoka.Shell\ViewModels\LibraryViewModel.cs'
$bytes = [IO.File]::ReadAllBytes($source)
$text = [Text.Encoding]::UTF8.GetString($bytes)
$records = [Collections.Generic.List[object]]::new()
$common = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false',
    '-p:DebugSymbols=false', '-p:DebugType=None')

function Invoke-Case([string]$Name, [string[]]$Arguments, [int]$ExpectedExit, [string]$ExpectedText = '') {
    $log = Join-Path $evidence ($Name + '.log')
    & $DotnetPath @Arguments *> $log
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    $records.Add([ordered]@{ name = $Name; exitCode = $code; expectedExit = $ExpectedExit; log = $log })
    if ($code -ne $ExpectedExit -or ($ExpectedText -and -not $output.Contains($ExpectedText))) {
        throw "$Name 未满足预期：退出 $code，预期 $ExpectedExit；日志 $log"
    }
    Write-Output "$Name：退出 $code，符合预期"
}

function Build-Case([string]$Name, [string]$Platform, [string[]]$Extra = @()) {
    Invoke-Case $Name (@('build', $project, '-c', 'Release', '--no-restore', "-p:Platform=$Platform") + $common + $Extra) 0
    $log = [IO.File]::ReadAllText((Join-Path $evidence ($Name + '.log')))
    if ($log -notmatch '(?m)^\s*0 (个警告|Warning\(s\))\s*$' -or $log -notmatch '(?m)^\s*0 (个错误|Error\(s\))\s*$') {
        throw "$Name 未确认零警告零错误。"
    }
}

function Generate-Rejection([string]$Name, [string]$InputPath, [string]$Message) {
    $generated = Join-Path $PSScriptRoot 'obj\x64\Release\net10.0\UpcomingSources.g.cs'
    $before = (Get-FileHash -LiteralPath $generated -Algorithm SHA256).Hash
    Invoke-Case $Name (@('msbuild', $project, '-t:GeneratePerfUpcomingSources', '-p:Configuration=Release',
        '-p:Platform=x64', "-p:PerfUpcomingSourcePath=$InputPath") + $common) 1 $Message
    if ((Get-FileHash -LiteralPath $generated -Algorithm SHA256).Hash -ne $before) {
        throw "$Name 覆盖了之前的合法生成源。"
    }
}

Invoke-Case 'restore-default' (@('restore', $project, '--source', $PackagesPath, '--packages', $PackagesPath,
    '--disable-parallel', '-p:NuGetAudit=false') + $common) 0
Build-Case 'build-default' 'AnyCPU'
Invoke-Case 'run-default' @((Join-Path $PSScriptRoot 'bin\Release\net10.0\Momoka.PageContractTests.dll')) 0 '失败 0，跳过 0'
Build-Case 'build-x64' 'x64'
$x64 = Join-Path $PSScriptRoot 'bin\x64\Release\net10.0\Momoka.PageContractTests.dll'
Invoke-Case 'run-x64' @($x64) 0 '失败 0，跳过 0'

# Use explicit copies only: the working tree's production source is never edited.
$input = Join-Path $evidence 'LibraryViewModel-copy.cs'
[IO.File]::WriteAllBytes($input, $bytes)
Build-Case 'build-local-copy' 'x64' @("-p:PerfUpcomingSourcePath=$input")
Invoke-Case 'run-local-copy' @($x64) 0 '失败 0，跳过 0'
[IO.File]::WriteAllText($input, $text + "`n// isolated stale-input counterexample`n", [Text.UTF8Encoding]::new($false))
Invoke-Case 'run-stale-input' @($x64) 1 '构建后源码变化必须重编译'

$missing = Join-Path $evidence 'LibraryViewModel-missing.cs'
[IO.File]::WriteAllText($missing, $text.Replace('    private void UpdateUpcomingDays()', '    private void RemovedUpcomingDays()'), [Text.UTF8Encoding]::new($false))
Generate-Rejection 'reject-missing-method' $missing 'Missing/ambiguous production method'
$guard = Join-Path $evidence 'LibraryViewModel-guard.cs'
[IO.File]::WriteAllText($guard, $text.Replace('_active && SourcesAvailable && _queries.IsCurrent(token);', '_active && _queries.IsCurrent(token);'), [Text.UTF8Encoding]::new($false))
Generate-Rejection 'reject-changed-guard' $guard 'Production query/source lifetime guard changed'
$outsideDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Momoka-perf-source-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outsideDirectory) | Out-Null
$outside = Join-Path $outsideDirectory 'LibraryViewModel.cs'
[IO.File]::WriteAllBytes($outside, $bytes)
$records.Add([ordered]@{ name = 'outside-input'; path = $outside; sha256 = (Get-FileHash -LiteralPath $outside -Algorithm SHA256).Hash })
Generate-Rejection 'reject-outside-identical-source' $outside 'PERF_SOURCE_OUTSIDE_WORKTREE'
$junction = Join-Path $evidence 'outside-junction'
New-Item -ItemType Junction -Path $junction -Value $outsideDirectory | Out-Null
Generate-Rejection 'reject-reparse-identical-source' (Join-Path $junction 'LibraryViewModel.cs') 'PERF_SOURCE_REPARSE_POINT'

Build-Case 'build-x64-final' 'x64'
Invoke-Case 'run-x64-final' @($x64) 0 '失败 0，跳过 0'
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding UTF8
Write-Output '默认与x64入口、当前哈希、过期哈希和错误树/重解析路径/缺方法/守卫负例均符合预期。'
