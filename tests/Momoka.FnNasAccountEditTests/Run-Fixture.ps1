[CmdletBinding()]
param(
    [string]$RunName = 'current',
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe',
    [string]$FixedPackageCache = 'C:\Users\89400\.nuget\packages',
    [string]$SourceRoot,
    [string]$ExpectedPasswordSha256,
    [string]$ExpectedPreferencesSha256,
    [switch]$Restore,
    [switch]$BuildShell,
    [switch]$Format
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须在本项目所属工作树执行' }
if ($RunName -notmatch '^[a-z0-9-]+$') { throw 'RunName只允许小写字母、数字、连字符' }
$evidence = Join-Path $root 'artifacts\r027-account-edit-generation'
$run = Join-Path $evidence ('run-' + $RunName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
foreach ($directory in @($run, (Join-Path $evidence 'cli-home'), (Join-Path $evidence 'temp'),
    (Join-Path $evidence 'nuget-http'), (Join-Path $evidence 'nuget-plugins'), (Join-Path $evidence 'fixed-feed'),
    (Join-Path $root 'work\r027\n'))) { [void][IO.Directory]::CreateDirectory($directory) }
$encoding = [Text.UTF8Encoding]::new($false)
$env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:Configuration = 'Release'
$env:NUGET_PACKAGES = Join-Path $root 'work\r027\n'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $evidence 'nuget-plugins'
$env:TEMP = Join-Path $evidence 'temp'
$env:TMP = $env:TEMP
$env:MSBUILDDISABLENODEREUSE = '1'

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $run ($Name + '.log')
    & $Dotnet @Arguments > $log 2>&1
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log | Write-Output
    $entry = @{ name = $Name; arguments = $Arguments; exitCode = $code; log = $log } | ConvertTo-Json -Compress
    [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), $entry + "`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $run ($Name + '.exit.txt')), "$code`n", $encoding)
    if ($code -ne 0) { throw "$Name 退出 $code，保留所有日志；停止，不运行旧输出或重试其它链" }
}

Push-Location -LiteralPath $root
try {
    Write-Output "R-027 证据目录：$run"
    Invoke-LoggedDotnet 'sdk-version' @('--version')
    if ([IO.File]::ReadAllText((Join-Path $run 'sdk-version.log')).Trim() -ne '10.0.400') { throw 'SDK必须为10.0.400' }
    $project = Join-Path $PSScriptRoot 'Momoka.FnNasAccountEditTests.csproj'
    $shell = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'
    $config = Join-Path $evidence 'offline.nuget.config'
    if ($Restore) {
        if (-not (Test-Path -LiteralPath $FixedPackageCache -PathType Container)) { throw 'fixed缓存不存在，停止' }
        $feed = Join-Path $evidence 'fixed-feed'
        $manifest = @()
        foreach ($archive in (Get-ChildItem -LiteralPath $FixedPackageCache -File -Filter '*.nupkg' -Recurse | Sort-Object FullName)) {
            $target = Join-Path $feed $archive.Name
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Copy-Item -LiteralPath $archive.FullName -Destination $target }
            $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
            if ($hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw 'fixed快照与包缓存哈希不同，停止' }
            $manifest += @{ archive = $archive.Name; sha256 = $hash; source = $archive.FullName }
        }
        if ($manifest.Count -eq 0) { throw 'fixed缓存没有归档，停止' }
        [IO.File]::WriteAllText((Join-Path $run 'fixed-cache-manifest.json'), ($manifest | ConvertTo-Json -Depth 4) + "`n", $encoding)
        $escaped = [Security.SecurityElement]::Escape($feed)
        $xml = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<configuration><packageSources><clear/><add key=`"fixed-local`" value=`"$escaped`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>`n"
        [IO.File]::WriteAllText($config, $xml, $encoding)
        $restoreArgs = @('restore', $project, '--configfile', $config, '--packages', $env:NUGET_PACKAGES,
            '--disable-parallel', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
        Invoke-LoggedDotnet 'restore-fixture' $restoreArgs
        if ($BuildShell) {
            $restoreArgs[1] = $shell
            Invoke-LoggedDotnet 'restore-shell' ($restoreArgs + '-p:Platform=x64')
        }
    }
    if ($SourceRoot) {
        $SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
        if (-not $SourceRoot.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '基点输入必须保留在本工作树' }
        if (-not $ExpectedPasswordSha256 -or -not $ExpectedPreferencesSha256) { throw '基点输入必须提供两个精确SHA256' }
        foreach ($pair in @(@('FnNasPasswordViewModel.cs', $ExpectedPasswordSha256), @('FnNasPreferencesViewModel.cs', $ExpectedPreferencesSha256))) {
            if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot $pair[0])).Hash -ne $pair[1]) { throw '基点源码哈希不符，停止' }
        }
    }
    else { $SourceRoot = Join-Path $root 'src\Momoka.Shell\ViewModels' }
    $files = @((Join-Path $SourceRoot 'FnNasPasswordViewModel.cs'), (Join-Path $SourceRoot 'FnNasPreferencesViewModel.cs'),
        (Join-Path $root 'src\Momoka.Shell\ViewModels\FnNasSessionViewModel.cs'), (Join-Path $root 'src\Momoka.Shell\ViewModels\PageViewModel.cs'),
        (Join-Path $root 'src\Momoka.Shell\ViewModels\FnNasAdminRows.cs'),
        (Join-Path $root 'src\Momoka.Core\Infrastructure\LoadGeneration.cs'),
        (Join-Path $root 'src\Momoka.Core\Diagnostics\Failure.cs'), (Join-Path $root 'src\Momoka.Core\Emby\EmbyApiException.cs'),
        (Join-Path $root 'tests\Momoka.Tests\TestHarness.cs'), (Join-Path $root 'tests\Momoka.SubtitleDialogTests\UiQueue.cs'),
        (Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'Boundaries.cs'), $project, $PSCommandPath,
        (Join-Path $root 'src\Momoka.Shell\Views\FnNasPasswordPage.xaml'), (Join-Path $root 'src\Momoka.Shell\Views\FnNasPreferencesPage.xaml'),
        (Join-Path $root 'src\Momoka.Shell\Views\FnNasPasswordPage.xaml.cs'), (Join-Path $root 'src\Momoka.Shell\Views\FnNasPreferencesPage.xaml.cs'))
    $inputs = @($files | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash } })
    [IO.File]::WriteAllText((Join-Path $run 'inputs.json'), ($inputs | ConvertTo-Json -Depth 4) + "`n", $encoding)
    $buildArgs = @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', "-p:AccountEditSourceRoot=$SourceRoot")
    Invoke-LoggedDotnet 'build-fixture' $buildArgs
    # Only the just-built production VM/source-linked executable may run. A red stays exit 1.
    Invoke-LoggedDotnet 'fixture' @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore')
    if ($BuildShell) {
        Invoke-LoggedDotnet 'build-shell' @('build', $shell, '-c', 'Release', '--no-restore', '-m:1', '-p:Platform=x64',
            '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    }
    if ($Format) {
        Invoke-LoggedDotnet 'format-fixture' @('format', 'whitespace', $project, '--verify-no-changes', '--no-restore')
        # Design-time MSBuild must use the same x64 Core reference path as the actual Shell build.
        # Without Platform the formatter reports missing bin/Release/Core despite a valid x64 build.
        $previousPlatform = $env:Platform
        $env:Platform = 'x64'
        try {
            Invoke-LoggedDotnet 'format-shell' @('format', 'whitespace', $shell, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic',
                '--report', (Join-Path $run 'format-shell-report.json'), '--include',
                (Join-Path $root 'src\Momoka.Shell\ViewModels\FnNasPasswordViewModel.cs'),
                (Join-Path $root 'src\Momoka.Shell\ViewModels\FnNasPreferencesViewModel.cs'))
        }
        finally { $env:Platform = $previousPlatform }
    }
}
finally { Pop-Location }
