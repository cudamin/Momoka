[CmdletBinding()]
param(
    [string]$Dotnet = (Join-Path $HOME '.dotnet\dotnet.exe'),
    [Parameter(Mandatory)][string]$Node,
    [Parameter(Mandatory)][string]$NodeModules,
    [Parameter(Mandatory)][string]$Browser,
    [string]$EmbyManager,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$tree = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'Momoka.ServerSettingsTabs.Tests.csproj'
if (-not $OutputRoot) { $OutputRoot = Join-Path $tree 'work\server-settings-tabs\final' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$prefix = $tree.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $OutputRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must be inside this fixture worktree.'
}
foreach ($file in @($Dotnet, $Node, $Browser)) {
    if (-not [IO.File]::Exists($file)) { throw "Missing executable: $file" }
}
if (-not [IO.Directory]::Exists($NodeModules)) { throw 'NodeModules directory is unavailable.' }
if ($EmbyManager -and -not [IO.File]::Exists($EmbyManager)) { throw 'Anonymous EmbyManager snapshot is unavailable.' }
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null

function Run-Captured([string]$Executable, [string[]]$Arguments, [string]$Name) {
    & $Executable @Arguments *> (Join-Path $OutputRoot "$Name.log")
    $result = $LASTEXITCODE
    Get-Content -LiteralPath (Join-Path $OutputRoot "$Name.log")
    if ($result -ne 0) { throw "$Name failed with exit code $result; no stale output is treated as passing." }
}

Push-Location -LiteralPath $tree
$oldModules = $env:MOMOKA_NODE_MODULES
$oldBrowser = $env:MOMOKA_BROWSER_EXE
try {
    Run-Captured $Dotnet @('restore', $project, '--disable-parallel', '-m:1', '-p:BuildInParallel=false') 'restore'
    Run-Captured $Dotnet @('build', $project, '-c', 'Release', '--no-restore', '-m:1',
        '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false') 'build'
    # This source-linked subset never registers SelfCheckRun or the full Core/mutex suite.
    Run-Captured $Dotnet @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore',
        '--', 'contracts') 'contracts'
    $payload = Join-Path $OutputRoot 'payload.json'
    Run-Captured $Dotnet @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore',
        '--', $tree, $payload) 'export'
    $env:MOMOKA_NODE_MODULES = [IO.Path]::GetFullPath($NodeModules)
    $env:MOMOKA_BROWSER_EXE = [IO.Path]::GetFullPath($Browser)
    $arguments = @((Join-Path $PSScriptRoot 'browser-tests.mjs'), $payload, $OutputRoot)
    if ($EmbyManager) { $arguments += [IO.Path]::GetFullPath($EmbyManager) }
    Run-Captured $Node $arguments 'browser'
    Write-Output "Server-settings contracts and anonymous DOM fixture passed; evidence: $OutputRoot"
}
finally {
    $env:MOMOKA_NODE_MODULES = $oldModules
    $env:MOMOKA_BROWSER_EXE = $oldBrowser
    Pop-Location
}
