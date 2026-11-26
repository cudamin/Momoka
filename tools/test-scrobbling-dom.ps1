[CmdletBinding()]
param(
    [string]$Dotnet = (Join-Path $HOME '.dotnet/dotnet.exe'),
    [string]$Node = 'node',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/scrobbling-dom/' + [Guid]::NewGuid().ToString('N')) }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$scripts = Join-Path $OutputDirectory 'scripts.json'
$project = Join-Path $root 'tests/Momoka.Tests/Momoka.Tests.csproj'
& $Dotnet build $project -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $Dotnet run --project $project -c Release --no-build --no-restore -- scrobbling-dom-scripts $scripts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $Node (Join-Path $PSScriptRoot 'scrobbling-dom/douban-dom-tests.mjs') $scripts
exit $LASTEXITCODE
