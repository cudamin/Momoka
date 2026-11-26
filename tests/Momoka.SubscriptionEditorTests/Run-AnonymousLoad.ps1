[CmdletBinding()]
param([switch]$BuildOnly, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$hostRoot = Join-Path $root 'artifacts/subscription-editor-load-host'
$target = Join-Path $PSScriptRoot 'AnonymousLoad.targets'
$project = Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/subscription-editor-load/run-' + [guid]::NewGuid().ToString('N')) }
[IO.Directory]::CreateDirectory($hostRoot) | Out-Null
Push-Location -LiteralPath $root
try {
    & $dotnet build $project -c Release -p:Platform=x64 --no-restore ('-p:CustomAfterMicrosoftCommonTargets=' + $target) ('-p:OutputPath=' + (Join-Path $hostRoot 'bin/')) *> (Join-Path $hostRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $hostRoot 'build.log') -Tail 16; exit $LASTEXITCODE }
    $log = [IO.File]::ReadAllText((Join-Path $hostRoot 'build.log'))
    if ($log -match ': warning ' -or $log -match ': error ') { throw 'Require zero-warning/error host build' }
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $hostRoot 'bin') -Filter Momoka.exe -File -Recurse)
    if ($candidates.Count -ne 1) { throw 'Expected one instrumented same-assembly apphost' }
    Write-Output ('Anonymous minimum-load apphost: ' + $candidates[0].FullName)
    if ($BuildOnly) { exit 0 }
    $probe = Start-Process -FilePath $candidates[0].FullName -ArgumentList @('--anonymous-editor-load', $OutputDirectory) -WorkingDirectory $root -PassThru
    Write-Output ('PID=' + $probe.Id)
    $probe.WaitForExit()
    exit $probe.ExitCode
} finally { Pop-Location }
