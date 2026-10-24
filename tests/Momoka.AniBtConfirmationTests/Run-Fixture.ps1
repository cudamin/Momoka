[CmdletBinding()]
param(
    [switch]$Baseline,
    [switch]$RestoreOnly,
    [switch]$SkipRestore,
    [switch]$LegacyRegression,
    [string]$Label = "fixture"
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$actual = (git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($actual -ne $root) { throw 'Fixture root is not the current worktree' }
$evidence = Join-Path $root 'artifacts\r037-r039'
foreach ($name in @('cli-home', 'temp', 'packages', 'logs', 'local-feed', 'http-cache')) {
    New-Item -ItemType Directory -Path (Join-Path $evidence $name) -Force | Out-Null
}
$env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
$env:TEMP = Join-Path $evidence 'temp'
$env:TMP = $env:TEMP
$env:NUGET_PACKAGES = Join-Path $evidence 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'http-cache'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NUGET_CERT_REVOCATION_MODE = 'offline'
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = if ($LegacyRegression) { Join-Path $PSScriptRoot 'Legacy\Momoka.Tests.csproj' } else { Join-Path $PSScriptRoot 'Momoka.AniBtConfirmationTests.csproj' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$prefix = Join-Path $evidence "logs\$Label-$stamp"
$common = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
$source = if ($Baseline) { Join-Path $evidence 'baseline' } else { $root }
if ($Baseline -and $LegacyRegression) { throw 'Legacy regression sources use the fixed two-phase contract, not baseline input' }
Set-Location -LiteralPath $root
if ($Baseline) {
    $revision = '3f86b51f8202cac0328d9a10f9945ce9df15969a'
    $inputs = @(
        'src/Momoka.Core/AniBt/AniBtService.cs',
        'src/Momoka.Shell/ViewModels/AniBtScrapeViewModel.cs',
        'src/Momoka.Shell/Views/AniBtScrapeDialog.xaml',
        'src/Momoka.Shell/Views/AniBtScrapeDialog.xaml.cs'
    )
    $manifest = @()
    foreach ($relative in $inputs) {
        $destination = [IO.Path]::GetFullPath((Join-Path $source $relative))
        if (-not $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Baseline destination escaped the isolated snapshot directory'
        }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        # Copy exact Git blob bytes. Never checkout/restore/reset or overwrite working sources.
        $start = [Diagnostics.ProcessStartInfo]::new('git')
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.ArgumentList.Add('show')
        $start.ArgumentList.Add("${revision}:$relative")
        $process = [Diagnostics.Process]::Start($start)
        $stream = [IO.File]::Create($destination)
        try {
            $process.StandardOutput.BaseStream.CopyTo($stream)
            $errorText = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw "Exact baseline blob unavailable: $relative $errorText" }
        } finally {
            $stream.Dispose()
            $process.Dispose()
        }
        $manifest += [pscustomobject]@{ file = $relative; commit = $revision; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
    }
    $json = ($manifest | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText((Join-Path $evidence 'baseline-manifest.json'), $json, [Text.UTF8Encoding]::new($false))
}
if (-not $SkipRestore) {
    foreach ($spec in @(
        @('communitytoolkit.mvvm', '8.4.2'),
        @('microsoft.visualstudio.threading.analyzers', '18.7.23'),
        @('microsoft.codeanalysis.bannedapianalyzers', '5.6.0'),
        @('roslynator.analyzers', '5.0.0')
    )) {
        $name, $version = $spec
        $package = Join-Path $HOME ".nuget\packages\$name\$version\$name.$version.nupkg"
        if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "本机缓存缺包，停止：$name $version" }
        Copy-Item -LiteralPath $package -Destination (Join-Path $evidence 'local-feed')
    }
    # Sources are FIXED to copied, pre-existing machine-cache packages. No public feed/fallback/audit.
    & $dotnet restore $project --source (Join-Path $evidence 'local-feed') --packages $env:NUGET_PACKAGES -p:NuGetAudit=false @common 2>&1 |
        Tee-Object -FilePath "$prefix-restore.log"
    $restoreExit = $LASTEXITCODE
    if ($restoreExit -ne 0) { Write-Output "RESTORE_EXIT=$restoreExit; 本机缓存缺包或restore失败，停止"; exit $restoreExit }
}
if ($RestoreOnly) { Write-Output 'RESTORE_EXIT=0'; exit 0 }
& $dotnet build $project -c Release --no-restore @common "-p:AniBtSourceRoot=$source" 2>&1 |
    Tee-Object -FilePath "$prefix-build.log"
$buildExit = $LASTEXITCODE
Write-Output "BUILD_EXIT=$buildExit"
if ($buildExit -ne 0) { exit $buildExit }
$build = Get-Content -LiteralPath "$prefix-build.log" -Raw
if ($build -notmatch '(?m)^\s*0 (?:Warning\(s\)|个警告)' -or $build -notmatch '(?m)^\s*0 (?:Error\(s\)|个错误)') {
    throw 'Current Release build did not prove zero warnings and zero errors; no-build is forbidden'
}
& $dotnet run --project $project -c Release --no-build --no-restore 2>&1 |
    Tee-Object -FilePath "$prefix-test.log"
$testExit = $LASTEXITCODE
Write-Output "TEST_EXIT=$testExit; LOG_PREFIX=$prefix"
exit $testExit
