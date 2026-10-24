[CmdletBinding()]
param(
    [ValidateSet('Current', 'R010', 'All')][string]$Mode = 'All',
    [string]$RunName = 'current',
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe',
    [switch]$Restore,
    [switch]$Format
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw 'Only this fixture worktree is allowed' }
if ($RunName -notmatch '^[a-z0-9-]+$') { throw 'RunName must use lowercase letters, digits or hyphens' }
$evidence = Join-Path $root 'artifacts\r011-current-permission'
$run = Join-Path $evidence ('account-' + $RunName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($directory in @($run, (Join-Path $evidence 'cli-home'), (Join-Path $evidence 'temp'),
    (Join-Path $evidence 'nuget-http'), (Join-Path $evidence 'nuget-plugins'))) { [void][IO.Directory]::CreateDirectory($directory) }
$env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:Configuration = 'Release'
$env:NUGET_PACKAGES = Join-Path $evidence 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $evidence 'nuget-plugins'
$env:TEMP = Join-Path $evidence 'temp'
$env:TMP = $env:TEMP

function Invoke-Dotnet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $run ($Name + '.log')
    & $Dotnet @Arguments > $log 2>&1
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log -Encoding UTF8 | Write-Output
    [IO.File]::WriteAllText((Join-Path $run ($Name + '.exit.txt')), "$code`n", $encoding)
    $entry = @{ name = $Name; arguments = $Arguments; exitCode = $code; log = $log } | ConvertTo-Json -Compress
    [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), $entry + "`n", $encoding)
    if ($code -ne 0) { throw "${Name} failed ($code). Stop; preserve evidence, never execute old output or use a network fallback." }
}

Push-Location -LiteralPath $root
try {
    Invoke-Dotnet 'sdk-version' @('--version')
    if ([IO.File]::ReadAllText((Join-Path $run 'sdk-version.log')).Trim() -ne '10.0.400') { throw 'SDK 10.0.400 required' }
    $project = Join-Path $PSScriptRoot 'AccountCompatibility\AccountCompatibility.csproj'
    if ($Restore) {
        $configuration = Join-Path $evidence 'offline.nuget.config'
        if (-not (Test-Path -LiteralPath $configuration)) { throw 'First prepare the fixed archive feed with Run-Fixture.ps1 -Restore. Do not download missing packages.' }
        Invoke-Dotnet 'restore-account' @('restore', $project, '--configfile', $configuration, '--packages', $env:NUGET_PACKAGES,
            '--disable-parallel', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-p:NuGetAudit=false')
    }
    $flags = @('-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    $sources = @()
    foreach ($name in @('FnNasSessionViewModel.cs', 'FnNasPreferencesViewModel.cs', 'FnNasPasswordViewModel.cs')) {
        $path = Join-Path $root ('src\Momoka.Shell\ViewModels\' + $name)
        $sources += @{ source = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; scope = 'current-worktree' }
    }
    if ($Mode -in @('Current', 'All')) {
        $env:AccountPasswordSource = $null
        $env:AccountPersistenceSource = $null
        Invoke-Dotnet 'build-current' (@('build', $project) + $flags)
        if ($Format) {
            Invoke-Dotnet 'format-current' @('format', 'whitespace', $project, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic')
            Invoke-Dotnet 'rebuild-current-after-format' (@('build', $project) + $flags)
        }
        Invoke-Dotnet 'tests-current' @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--', $root, (Join-Path $run 'current-results.json'))
    }
    if ($Mode -in @('R010', 'All')) {
        # Static committed source evidence only: full R010 password VM + its contract, NEVER
        # production Session/Store/Service/authentication/vault. Snapshots are confined to artifacts.
        $commit = 'e229a2d75289704d78644a4b41552fad8f061e7e'
        $snapshots = @{}
        foreach ($relative in @('src/Momoka.Shell/ViewModels/FnNasPasswordViewModel.cs', 'src/Momoka.Core/Services/ISettingsPersistence.cs')) {
            $spec = $commit + ':' + $relative
            $blob = (& git -C $root rev-parse $spec).Trim()
            if ($LASTEXITCODE -ne 0) { throw 'The explicitly authorized R010 commit is unavailable; do not fetch or substitute a source.' }
            $lines = @(& git -C $root show $spec)
            if ($LASTEXITCODE -ne 0) { throw 'Cannot read the authorized committed source' }
            $path = Join-Path $run ([IO.Path]::GetFileName($relative))
            if (Test-Path -LiteralPath $path) { throw 'Never overwrite source evidence' }
            [IO.File]::WriteAllText($path, ($lines -join "`n") + "`n", $encoding)
            $actual = (& git -C $root hash-object --no-filters $path).Trim()
            if ($LASTEXITCODE -ne 0 -or $actual -ne $blob) { throw 'Snapshot must be byte-identical to its committed Git blob' }
            $sources += @{ source = $relative; commit = $commit; gitBlob = $blob; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; output = $path }
            $snapshots[[IO.Path]::GetFileName($relative)] = $path
        }
        $properties = @(('-p:AccountPasswordSource=' + $snapshots['FnNasPasswordViewModel.cs']), ('-p:AccountPersistenceSource=' + $snapshots['ISettingsPersistence.cs']))
        $env:AccountPasswordSource = $snapshots['FnNasPasswordViewModel.cs']
        $env:AccountPersistenceSource = $snapshots['ISettingsPersistence.cs']
        Invoke-Dotnet 'build-r010' (@('build', $project) + $flags + $properties)
        if ($Format) {
            # Limit format to this fixture's own sources. The linked committed snapshot is evidence,
            # not a request to reformat R010 or any forbidden production/shared file.
            $ownFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'AccountCompatibility') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
            Invoke-Dotnet 'format-r010' (@('format', 'whitespace', $project, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic', '--include') + $ownFiles)
            Invoke-Dotnet 'rebuild-r010-after-format' (@('build', $project) + $flags + $properties)
        }
        Invoke-Dotnet 'tests-r010' (@('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore') + $properties + @('--', $root, (Join-Path $run 'r010-results.json')))
    }
    [IO.File]::WriteAllText((Join-Path $run 'source-manifest.json'), ($sources | ConvertTo-Json -Depth 5) + "`n", $encoding)
    Write-Output "Full account compatibility evidence: $run. Counts for Current/R010 overlap and are reported separately."
}
finally { $env:AccountPasswordSource = $null; $env:AccountPersistenceSource = $null; Pop-Location }
