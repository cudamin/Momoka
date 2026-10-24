[CmdletBinding()]
param(
    [switch]$HistoricalRed,
    [switch]$ReuseLocalFeed,
    [switch]$RestoreOnly,
    [string]$Label = 'current'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$actual = (git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($actual -ne $root) { throw 'R042 root is not the current worktree' }
if ($Label -notmatch '^[a-zA-Z0-9-]+$') { throw 'Use a simple R042 evidence label' }
$evidence = Join-Path $root 'artifacts\r042'
foreach ($name in @('cli-home', 'temp', 'packages', 'logs', 'local-feed', 'http-cache', 'runs')) {
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
$sdk = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdk -ne '10.0.400') { throw "R042 requires SDK 10.0.400, got $sdk" }
$project = Join-Path $PSScriptRoot 'Momoka.AniBtSourceContractTests.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$run = Join-Path $evidence "runs\$Label-$stamp"
New-Item -ItemType Directory -Path $run | Out-Null
$prefix = Join-Path $evidence "logs\$Label-$stamp"
$mode = if ($HistoricalRed) { 'historical-red' } else { 'current' }
$source = $root
$historicalInputs = @('src/Momoka.Core/AniBt/AniBtService.cs', 'src/Momoka.Shell/ViewModels/AniBtScrapeViewModel.cs',
    'src/Momoka.Shell/Views/AniBtScrapeDialog.xaml', 'src/Momoka.Shell/Views/AniBtScrapeDialog.xaml.cs')
$common = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false',
    "-p:BaseIntermediateOutputPath=$run\obj\", "-p:OutputPath=$run\bin\", "-p:CompilerGeneratedFilesOutputPath=$run\generated")
if ($HistoricalRed) {
    $revision = '3f86b51f8202cac0328d9a10f9945ce9df15969a'
    $source = Join-Path $run 'historical-source'
    foreach ($relative in $historicalInputs) {
        $destination = [IO.Path]::GetFullPath((Join-Path $source $relative))
        if (-not $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Historical source destination escaped this run'
        }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
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
            if ($process.ExitCode -ne 0) { throw "Exact historical blob unavailable: $relative $errorText" }
        } finally {
            $stream.Dispose()
            $process.Dispose()
        }
    }
    $common += '-p:R042Legacy=true'
}
$inputs = @()
foreach ($relative in ($historicalInputs + @('src/Momoka.Core/Emby/EmbySession.cs', 'src/Momoka.Core/Emby/EmbySessionScope.cs',
    'src/Momoka.Core/Emby/EmbyResponses.cs', 'src/Momoka.Shell/ViewModels/PageViewModel.cs',
    'src/Momoka.Shell/Views/ShellPage.AniBt.cs', 'src/Momoka.Shell/Views/ItemCommands.cs', 'src/Momoka.Shell/Views/MoviePilotDialog.cs',
    'tests/Momoka.AniBtConfirmationTests/AnonymousFixture.cs', 'tests/Momoka.AniBtConfirmationTests/DialogBoundary.cs',
    'tests/Momoka.AniBtConfirmationTests/UiControls.cs', 'tests/Momoka.AniBtConfirmationTests/UiValues.cs',
    'tests/Momoka.AniBtConfirmationTests/ThemeBoundary.cs', 'tests/Momoka.SubtitleDialogTests/UiQueue.cs',
    'tests/Momoka.Tests/TestHarness.cs', 'Directory.Build.props', '.editorconfig', 'global.json'))) {
    $inputRoot = if ($HistoricalRed -and $relative -in $historicalInputs) { $source } else { $root }
    $inputs += [pscustomobject]@{ file = $relative; inputRoot = $inputRoot; sha256 =
        (Get-FileHash -LiteralPath (Join-Path $inputRoot $relative) -Algorithm SHA256).Hash }
}
foreach ($file in @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.ps1' })) {
    $inputs += [pscustomobject]@{ file = 'tests/Momoka.AniBtSourceContractTests/' + $file.Name; inputRoot = $root
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
[IO.File]::WriteAllText("$prefix-input-manifest.json", ($inputs | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
$packages = @()
foreach ($spec in @(@('communitytoolkit.mvvm', '8.4.2'), @('microsoft.visualstudio.threading.analyzers', '18.7.23'),
    @('microsoft.codeanalysis.bannedapianalyzers', '5.6.0'), @('roslynator.analyzers', '5.0.0'))) {
    $name, $version = $spec
    $cached = Join-Path $HOME ".nuget\packages\$name\$version\$name.$version.nupkg"
    if (-not (Test-Path -LiteralPath $cached -PathType Leaf)) { throw "本机缓存缺包，停止：$name $version" }
    $package = Join-Path $evidence "local-feed\$name.$version.nupkg"
    if (-not $ReuseLocalFeed) { Copy-Item -LiteralPath $cached -Destination $package }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "离线 feed 缺包，停止：$name $version" }
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash) { throw "离线包与固定本机缓存不一致：$name $version" }
    $packages += [pscustomobject]@{ name = $name; version = $version; sha256 = $hash }
}
[IO.File]::WriteAllText("$prefix-package-manifest.json", ($packages | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
# Every run has fresh assets/obj/bin. ReuseLocalFeed skips copies only, never restore or input checks.
$config = Join-Path $run 'offline.nuget.config'
$configXml = '<configuration><packageSources><clear /><add key="r042-local" value="' +
    [Security.SecurityElement]::Escape((Join-Path $evidence 'local-feed')) +
    '" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>'
[IO.File]::WriteAllText($config, $configXml + "`n", [Text.UTF8Encoding]::new($false))
& $dotnet restore $project --configfile $config --packages $env:NUGET_PACKAGES -p:NuGetAudit=false @common 2>&1 |
    Tee-Object -FilePath "$prefix-restore.log"
$restoreExit = $LASTEXITCODE
if ($restoreExit -ne 0) { Write-Output "RESTORE_EXIT=$restoreExit; 缺包或离线restore失败，停止"; exit $restoreExit }
if ($RestoreOnly) { Write-Output 'RESTORE_EXIT=0'; exit 0 }
& $dotnet build $project -c Release --no-restore @common "-p:R042SourceRoot=$source" 2>&1 |
    Tee-Object -FilePath "$prefix-build.log"
$buildExit = $LASTEXITCODE
Write-Output "BUILD_EXIT=$buildExit"
if ($buildExit -ne 0) { exit $buildExit }
$build = Get-Content -LiteralPath "$prefix-build.log" -Raw
if ($build -notmatch '(?m)^\s*0 (?:Warning\(s\)|个警告)' -or $build -notmatch '(?m)^\s*0 (?:Error\(s\)|个错误)') {
    throw 'Current Release did not prove zero warnings/errors; stale no-build output is forbidden'
}
$assembly = Join-Path $run 'bin\Momoka.AniBtSourceContractTests.dll'
& $dotnet $assembly 2>&1 | Tee-Object -FilePath "$prefix-test.log"
$testExit = $LASTEXITCODE
$result = [ordered]@{
    mode = $mode; head = (git -C $root rev-parse HEAD).Trim(); sdk = $sdk; buildExit = $buildExit; testExit = $testExit
    historicalRevision = if ($HistoricalRed) { $revision } else { $null }
    runRoot = $run; assemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    inputManifest = "$prefix-input-manifest.json"; inputManifestSha256 = (Get-FileHash -LiteralPath "$prefix-input-manifest.json" -Algorithm SHA256).Hash
    packageManifest = "$prefix-package-manifest.json"; packageManifestSha256 = (Get-FileHash -LiteralPath "$prefix-package-manifest.json" -Algorithm SHA256).Hash
    buildLog = "$prefix-build.log"; buildLogSha256 = (Get-FileHash -LiteralPath "$prefix-build.log" -Algorithm SHA256).Hash
    testLog = "$prefix-test.log"; testLogSha256 = (Get-FileHash -LiteralPath "$prefix-test.log" -Algorithm SHA256).Hash
    summary = @(Get-Content -LiteralPath "$prefix-test.log" | Where-Object { $_ -match '^共 \d+ 项' })
    limitations = '匿名 actual Core/VM 和惰性 HTTP/设置/控件端点；不是完整历史版本、真实模态 UI 或服务器/凭据可达性。'
}
[IO.File]::WriteAllText("$prefix-result.json", ($result | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "TEST_EXIT=$testExit; MODE=$mode; LOG_PREFIX=$prefix"
exit $testExit
