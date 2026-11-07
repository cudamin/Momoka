[CmdletBinding()]
param([switch]$BuildOnly, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$hostRoot = Join-Path $root 'artifacts/anonymous-same-assembly'
$target = Join-Path $PSScriptRoot 'AnonymousEntry.targets'
$project = Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/anonymous-selfcheck/run-' + [guid]::NewGuid().ToString('N')) }
$sourcePaths = @('src/Momoka.Shell/Program.cs','src/Momoka.Shell/App.xaml.cs','src/Momoka.Shell/ShellSelfCheck.cs',
    'src/Momoka.Shell/ShellSelfCheck.Run.cs','src/Momoka.Shell/ShellSelfCheck.Finalization.cs','src/Momoka.Shell/Composition/ShellServices.cs')
$before = @($sourcePaths | ForEach-Object { [pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })
[IO.Directory]::CreateDirectory($hostRoot) | Out-Null
Push-Location -LiteralPath $root
try {
    & $dotnet build $project -c Release -p:Platform=x64 -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false `
        -p:DebugSymbols=false -p:DebugType=None ('-p:CustomAfterMicrosoftCommonTargets=' + $target) `
        ('-p:OutputPath=' + (Join-Path $hostRoot 'bin/')) *> (Join-Path $hostRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $hostRoot 'build.log') -Tail 12; exit $LASTEXITCODE }
    $log = [IO.File]::ReadAllText((Join-Path $hostRoot 'build.log'))
    if ($log -notmatch '(?m)^\s*0 个警告\s*$' -or $log -notmatch '(?m)^\s*0 个错误\s*$') { throw 'Require zero-warning/error host build' }
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $hostRoot 'bin') -Filter Momoka.exe -File -Recurse)
    if ($candidates.Count -ne 1) { throw 'Expected one instrumented Momoka apphost' }
    $exe = $candidates[0]
    foreach ($item in $before) {
        if ((Get-FileHash -LiteralPath (Join-Path $root $item.path)).Hash -ne $item.sha256) { throw 'Original Shell sources changed during host build' }
    }
    [IO.File]::WriteAllText((Join-Path $hostRoot 'source-receipt.json'),($before | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    Write-Output ('Instrumented anonymous apphost: ' + $exe.FullName)
    if ($BuildOnly) { exit 0 }
    $probe = Start-Process -FilePath $exe.FullName -ArgumentList @('--anonymous-selfcheck', $OutputDirectory) -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Write-Output ('PID=' + $probe.Id)
    $probe.WaitForExit()
    exit $probe.ExitCode
} finally { Pop-Location }
