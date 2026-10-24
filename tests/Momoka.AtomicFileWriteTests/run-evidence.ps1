[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,63}$')]
    [string]$Label,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSourceSha256,
    [switch]$ControlsOnly,
    [switch]$BackupFailuresOnly
)

$ErrorActionPreference = 'Stop'
if ($ControlsOnly -and $BackupFailuresOnly) { throw '对照限定与备份失败限定不能混用。' }
$scope = if ($ControlsOnly) { 'controls-only' } elseif ($BackupFailuresOnly) { 'backup-failures-only' } else { 'all' }
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$actualRepo = @(& git -C $repo rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or $actualRepo.Count -ne 1 -or $actualRepo[0].Replace('/', '\') -ne $repo) {
    throw '当前命令不是本项目的独立工作树根目录。'
}
if ((Get-Location).Path -ne $repo) { throw '请在当前工作树根目录运行此证据脚本。' }
$source = Join-Path $repo 'src\Momoka.Core\Infrastructure\AtomicFile.cs'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ($sourceHash -ne $ExpectedSourceSha256) { throw "源码 SHA 不符，拒绝运行：$sourceHash" }
$dotnet = Join-Path $HOME '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw '缺少用户级 dotnet.exe；不回退 PATH SDK。' }
$output = Join-Path $PSScriptRoot ('outputs\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Label + '-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $output) { throw '拒绝覆盖已有证据。' }
$null = & git -C $repo check-ignore -- (Join-Path $output 'evidence.json')
if ($LASTEXITCODE -ne 0) { throw '证据输出目录不在 ignored 范围。' }
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
function Save-Json([string]$name, [object]$value) {
    [System.IO.File]::WriteAllText((Join-Path $output $name), ($value | ConvertTo-Json -Depth 20) + "`n", $utf8)
}
$project = Join-Path $PSScriptRoot 'Momoka.AtomicFileWriteTests.csproj'
$head = @(& git -C $repo rev-parse HEAD)
$status = @(& git -C $repo -c core.quotepath=false status --short)
$diff = @(& git -C $repo diff -- src/Momoka.Core/Infrastructure/AtomicFile.cs)
$sdk = @(& $dotnet --version)
if ($LASTEXITCODE -ne 0) { throw 'SDK 查询失败。' }
$testHashes = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
Save-Json 'preconditions.json' ([ordered]@{
    date = (Get-Date).ToString('o'); label = $Label; worktree = $repo; head = $head;
    sourceSha256 = $sourceHash; source = $source; sdk = $sdk; status = $status;
    coreDependencyDiff = $diff; testFiles = $testHashes; controlsOnly = [bool]$ControlsOnly;
    backupFailuresOnly = [bool]$BackupFailuresOnly; scope = $scope;
    noNetwork = $true; retryBudget = 0; releaseSingleNode = $true
})
# Snapshot evidence only; the project always compile-links the CURRENT repository source.
[System.IO.File]::WriteAllBytes((Join-Path $output 'AtomicFile.dependency-snapshot.txt'), [System.IO.File]::ReadAllBytes($source))
function Invoke-Step([string]$name, [string[]]$arguments) {
    $log = Join-Path $output ($name + '.log')
    & $dotnet @arguments *> $log
    $code = $LASTEXITCODE
    [System.IO.File]::WriteAllText((Join-Path $output ($name + '.exit-code.txt')), "$code`n", $utf8)
    Write-Output "$name exit=$code；日志=$log"
    return [int]$code
}
$restore = Invoke-Step 'restore' @('restore', $project, '--configfile', (Join-Path $PSScriptRoot 'NuGet.Config'),
    '--disable-parallel', '-m:1', '-p:BuildInParallel=false', '-p:NuGetAudit=false', '-p:MSBuildNodeReuse=false')
$restoreCode = [int]($restore | Select-Object -Last 1)
$restore | Select-Object -SkipLast 1 | Write-Output
if ($restoreCode -ne 0) { Save-Json 'batch-result.json' @{ complete = $false; stage = 'restore'; exitCode = $restoreCode }; exit $restoreCode }
$build = Invoke-Step 'build' @('build', $project, '-c', 'Release', '--no-restore', '-m:1',
    '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-warnaserror')
$buildCode = [int]($build | Select-Object -Last 1)
$build | Select-Object -SkipLast 1 | Write-Output
if ($buildCode -ne 0) { Save-Json 'batch-result.json' @{ complete = $false; stage = 'build'; exitCode = $buildCode }; exit $buildCode }
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $ExpectedSourceSha256) {
    throw '编译期间生产依赖变化；不跑旧输出。'
}
$arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--',
    '--evidence-directory', (Join-Path $output 'run'))
if ($ControlsOnly) { $arguments += '--controls-only' }
if ($BackupFailuresOnly) { $arguments += '--backup-failures-only' }
$run = Invoke-Step 'run' $arguments
$runCode = [int]($run | Select-Object -Last 1)
$run | Select-Object -SkipLast 1 | Write-Output
$afterHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$reportPath = Join-Path $output 'run\result.json'
$reportExists = Test-Path -LiteralPath $reportPath -PathType Leaf
$complete = $false
if ($reportExists) {
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    $complete = $report.summary.complete -and ($afterHash -eq $ExpectedSourceSha256)
}
if (-not $complete -and $runCode -eq 0) { $runCode = 2 }
Save-Json 'batch-result.json' ([ordered]@{
    complete = [bool]$complete; contractsExecuted = -not [bool]$ControlsOnly; exitCode = $runCode;
    allRequiredContractsExecuted = (-not [bool]$ControlsOnly -and -not [bool]$BackupFailuresOnly); scope = $scope;
    sourceBeforeSha256 = $sourceHash; sourceAfterSha256 = $afterHash; reportPath = $reportPath;
    restoreExitCode = $restoreCode; buildExitCode = $buildCode; runExitCode = $runCode
})
Write-Output "证据批次=$output；最终 exit=$runCode；complete=$complete"
exit $runCode
