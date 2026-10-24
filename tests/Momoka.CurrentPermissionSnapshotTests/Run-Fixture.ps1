[CmdletBinding()]
param(
    [string]$RunName = 'current',
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe',
    [string]$FixedPackageCache = 'C:\Users\89400\.nuget\packages',
    [switch]$Restore,
    [switch]$BuildShell,
    [switch]$Related,
    [switch]$Format,
    [switch]$ExpectRed
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '仅允许本专项所属工作树' }
if ($RunName -notmatch '^[a-z0-9-]+$') { throw 'RunName仅允许小写字母/数字/连字符' }
$evidence = Join-Path $root 'artifacts\r011-current-permission'
$run = Join-Path $evidence ('run-' + $RunName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($directory in @($run, (Join-Path $evidence 'cli-home'), (Join-Path $evidence 'temp'),
    (Join-Path $evidence 'nuget-http'), (Join-Path $evidence 'nuget-plugins'), (Join-Path $evidence 'fixed-feed'),
    (Join-Path $evidence 'packages'))) { [void][IO.Directory]::CreateDirectory($directory) }
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
$env:R011_RESULT_PATH = Join-Path $run 'results.json'

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments, [switch]$AllowFailure) {
    $log = Join-Path $run ($Name + '.log')
    & $Dotnet @Arguments > $log 2>&1
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log -Encoding UTF8 | Write-Output
    $entry = @{ name = $Name; arguments = $Arguments; exitCode = $code; log = $log } | ConvertTo-Json -Compress
    [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), $entry + "`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $run ($Name + '.exit.txt')), "$code`n", $encoding)
    if ($code -ne 0 -and -not $AllowFailure) { throw "${Name}退出${code}；保留证据，停止，不跑旧输出、不换工具重试停止链" }
    $script:LastDotnetCode = $code
}

Push-Location -LiteralPath $root
try {
    Write-Output "R011证据：$run"
    Invoke-LoggedDotnet 'sdk-version' @('--version')
    if ([IO.File]::ReadAllText((Join-Path $run 'sdk-version.log')).Trim() -ne '10.0.400') { throw '必须SDK10.0.400' }
    $project = Join-Path $PSScriptRoot 'Momoka.CurrentPermissionSnapshotTests.csproj'
    $shell = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'
    $core = Join-Path $root 'src\Momoka.Core\Momoka.Core.csproj'
    $relatedProjects = @(
        @{ name = 'fnnas-default-permission'; path = Join-Path $root 'tests\Momoka.FnNasDefaultPermissionTests\Momoka.FnNasDefaultPermissionTests.csproj'; needsShell = $false },
        @{ name = 'settings-lifetime'; path = Join-Path $root 'tests\Momoka.SettingsLifetimeTests\Momoka.SettingsLifetimeTests.csproj'; needsShell = $true }
    )
    if ($Related -and -not $BuildShell) { throw 'Related Settings lifetime needs an explicitly fresh -BuildShell in this run' }
    $configuration = Join-Path $evidence 'offline.nuget.config'
    if ($Restore) {
        $feed = Join-Path $evidence 'fixed-feed'
        $manifest = @()
        if (-not (Test-Path -LiteralPath $FixedPackageCache -PathType Container)) { throw '固定缓存缺失；不联网补包' }
        foreach ($archive in (Get-ChildItem -LiteralPath $FixedPackageCache -Filter '*.nupkg' -File -Recurse | Sort-Object FullName)) {
            $target = Join-Path $feed $archive.Name
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Copy-Item -LiteralPath $archive.FullName -Destination $target }
            $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
            if ($hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw '固定缓存包哈希不一致；停止' }
            $manifest += @{ archive = $archive.Name; sha256 = $hash }
        }
        if ($manifest.Count -eq 0) { throw '缓存无归档；停止，不联网' }
        [IO.File]::WriteAllText((Join-Path $run 'fixed-cache-manifest.json'), ($manifest | ConvertTo-Json -Depth 3) + "`n", $encoding)
        $escaped = [Security.SecurityElement]::Escape($feed)
        $xml = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<configuration><packageSources><clear/><add key=`"fixed-local`" value=`"$escaped`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>`n"
        [IO.File]::WriteAllText($configuration, $xml, $encoding)
        $restoreArgs = @('restore', $project, '--configfile', $configuration, '--packages', $env:NUGET_PACKAGES,
            '--disable-parallel', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-p:NuGetAudit=false')
        Invoke-LoggedDotnet 'restore-fixture' $restoreArgs
        if ($BuildShell) { $restoreArgs[1] = $shell; Invoke-LoggedDotnet 'restore-shell' ($restoreArgs + '-p:Platform=x64') }
        if ($Related) {
            foreach ($relatedProject in $relatedProjects) {
                $restoreArgs[1] = $relatedProject.path
                Invoke-LoggedDotnet ('restore-' + $relatedProject.name) $restoreArgs
            }
        }
    }
    $flags = @('-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    Invoke-LoggedDotnet 'build-fixture' (@('build', $project) + $flags)
    if ($BuildShell) { Invoke-LoggedDotnet 'build-shell' (@('build', $shell) + $flags + '-p:Platform=x64') }
    if ($Format) {
        Invoke-LoggedDotnet 'format-fixture' @('format', 'whitespace', $project, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic')
        $coreFiles = @('src/Momoka.Core/Emby/EmbySession.cs', 'src/Momoka.Core/Emby/EmbySessionScope.cs',
            'src/Momoka.Core/Services/ServerCapabilities.cs') | ForEach-Object { Join-Path $root $_ }
        Invoke-LoggedDotnet 'format-core' (@('format', 'whitespace', $core, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic', '--include') + $coreFiles)
        if ($BuildShell) {
            $changed = @('src/Momoka.Core/Emby/EmbySession.cs', 'src/Momoka.Core/Emby/EmbySessionScope.cs',
                'src/Momoka.Core/Services/ServerCapabilities.cs', 'src/Momoka.Shell/ViewModels/FnNasSessionViewModel.cs',
                'src/Momoka.Shell/ViewModels/FnNasAdminViewModel.cs', 'src/Momoka.Shell/ViewModels/FnNasPasswordViewModel.cs',
                'src/Momoka.Shell/ViewModels/SettingsViewModel.cs',
                'src/Momoka.Shell/Views/SettingsPage.xaml.cs') | ForEach-Object { Join-Path $root $_ }
            Invoke-LoggedDotnet 'format-shell' (@('format', 'whitespace', $shell, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic', '--include') + $changed)
        }
    }
    if ($Format) {
        # MSBuildWorkspace may empty generated .g.cs during format. Rebuild current inputs before
        # no-build execution or inspecting XAML/generator evidence; never read backup output.
        Invoke-LoggedDotnet 'build-fixture-after-format' (@('build', $project) + $flags)
        if ($BuildShell) { Invoke-LoggedDotnet 'build-shell-after-format' (@('build', $shell) + $flags + '-p:Platform=x64') }
    }
    $runArgs = @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--', $root)
    if ($BuildShell) { $runArgs += Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll' }
    Invoke-LoggedDotnet 'tests' $runArgs -AllowFailure:$ExpectRed
    if ($ExpectRed -and $script:LastDotnetCode -ne 1) { throw '要求有效行为红灯退出1，不接受编译/入口失败或零失败' }
    $results = Get-Content -LiteralPath $env:R011_RESULT_PATH -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($results.executed -le 0 -or $results.skipped -ne 0 -or $ExpectRed -and $results.failed -le 0) { throw '结果不构成有效行为证据' }
    if ($Related) {
        foreach ($relatedProject in $relatedProjects) {
            # These are the existing anonymous, no-window projects only. Never run main Core or
            # selfcheck, whose user-level mutex and production routes belong to the integrator.
            Invoke-LoggedDotnet ('build-' + $relatedProject.name) (@('build', $relatedProject.path) + $flags)
            $arguments = @('run', '--project', $relatedProject.path, '-c', 'Release', '--no-build', '--no-restore', '--')
            if ($relatedProject.needsShell) { $arguments += Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll' }
            $arguments += $root
            Invoke-LoggedDotnet ('tests-' + $relatedProject.name) $arguments
        }
    }
    $hashes = @()
    foreach ($relative in @('src\Momoka.Core\Emby\EmbySession.cs', 'src\Momoka.Core\Emby\EmbySessionScope.cs',
        'src\Momoka.Core\Services\ServerCapabilities.cs', 'src\Momoka.Shell\ViewModels\FnNasSessionViewModel.cs',
        'src\Momoka.Shell\ViewModels\FnNasAdminViewModel.cs', 'src\Momoka.Shell\ViewModels\SettingsViewModel.cs',
        'src\Momoka.Shell\Views\SettingsPage.xaml.cs')) {
        $hashes += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash }
    }
    [IO.File]::WriteAllText((Join-Path $run 'production-source-hashes.json'), ($hashes | ConvertTo-Json -Depth 3) + "`n", $encoding)
    & git -C $root diff -- src/Momoka.Core/Emby/EmbySession.cs src/Momoka.Core/Emby/EmbySessionScope.cs src/Momoka.Core/Services/ServerCapabilities.cs src/Momoka.Shell/ViewModels/FnNasSessionViewModel.cs src/Momoka.Shell/ViewModels/FnNasAdminViewModel.cs src/Momoka.Shell/ViewModels/SettingsViewModel.cs src/Momoka.Shell/Views/SettingsPage.xaml.cs > (Join-Path $run 'production.diff')
    Write-Output "R011完成本次专项：$run"
}
finally { Pop-Location }
