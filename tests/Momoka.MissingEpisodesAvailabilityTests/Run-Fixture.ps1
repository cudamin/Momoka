param(
    [string]$Phase = 'green',
    [string]$ShellSource,
    [string]$ExpectedShellSha256,
    [string]$SourceRevision = 'current-tree',
    [switch]$Restore,
    [string]$FixedPackageCache = 'C:\Users\89400\.nuget\packages',
    [switch]$BuildShell,
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\')
$actualRoot = (& git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $actualRoot -ne $root) { throw '当前 Git 根目录与夹具工作树不符' }
if ($Phase -notmatch '^[a-z0-9-]+$') { throw 'Phase 只接受小写字母、数字和连字符' }
$evidence = Join-Path $root 'artifacts\r041-missing-search-availability'
$run = Join-Path $evidence ($Phase + '-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
$dirs = @($run, (Join-Path $evidence 'cli-home'), (Join-Path $evidence 'temp'),
    (Join-Path $root 'work\r041\n'), (Join-Path $evidence 'nuget-http'), (Join-Path $evidence 'fixed-feed'))
foreach ($dir in $dirs) {
    $resolved = [IO.Path]::GetFullPath($dir)
    if (-not $resolved.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '产物越出本树' }
    [IO.Directory]::CreateDirectory($resolved) | Out-Null
}

# Set before every SDK execution: no ASP.NET certificate, user CLI data, or network feeds.
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $root 'work\r041\n'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $evidence 'nuget-plugins'
$env:TEMP = Join-Path $evidence 'temp'
$env:TMP = $env:TEMP
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$encoding = [Text.UTF8Encoding]::new($false)

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $run ($Name + '.log')
    $lines = & $Dotnet @Arguments 2>&1
    $code = $LASTEXITCODE
    $text = ($lines | ForEach-Object { $_.ToString() }) -join "`n"
    [IO.File]::WriteAllText($log, $text + "`n", $encoding)
    $lines | ForEach-Object { Write-Output $_.ToString() }
    $entry = @{ name = $Name; arguments = $Arguments; exitCode = $code; log = $log } | ConvertTo-Json -Compress
    [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), $entry + "`n", $encoding)
    if ($code -ne 0) { throw "$Name 退出 $code；证据保留于 $log；执行停止，不继续其它构建或运行" }
}

Push-Location -LiteralPath $root
try {
    Invoke-LoggedDotnet 'sdk-version' @('--version')
    $version = [IO.File]::ReadAllText((Join-Path $run 'sdk-version.log')).Trim()
    if ($version -ne '10.0.400') { throw "要求 SDK 10.0.400，实际 $version" }
    $project = Join-Path $PSScriptRoot 'Momoka.MissingEpisodesAvailabilityTests.csproj'
    $shell = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'
    $config = Join-Path $evidence 'offline.nuget.config'
    if ($Restore) {
        # Freeze local archives in-tree, preserving hashes. Missing packages fail restore; no fallback.
        if (-not (Test-Path -LiteralPath $FixedPackageCache -PathType Container)) { throw '本机 fixed 缓存不存在，停止' }
        $feed = Join-Path $evidence 'fixed-feed'
        $manifest = @()
        foreach ($archive in (Get-ChildItem -LiteralPath $FixedPackageCache -File -Filter '*.nupkg' -Recurse | Sort-Object FullName)) {
            $target = Join-Path $feed $archive.Name
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Copy-Item -LiteralPath $archive.FullName -Destination $target }
            $sourceHash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
            $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($sourceHash -ne $targetHash) { throw 'fixed 包归档与本树快照哈希不同，停止' }
            $manifest += @{ archive = $archive.Name; sha256 = $sourceHash; source = $archive.FullName }
        }
        if ($manifest.Count -eq 0) { throw 'fixed 缓存没有包归档，停止' }
        [IO.File]::WriteAllText((Join-Path $run 'fixed-cache-manifest.json'), ($manifest | ConvertTo-Json -Depth 4) + "`n", $encoding)
        $escaped = [Security.SecurityElement]::Escape($feed)
        [IO.File]::WriteAllText($config,
            "<?xml version=""1.0"" encoding=""utf-8""?>
<configuration><packageSources><clear/><add key=""fixed-local"" value=""$escaped""/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>
", $encoding)
        $restoreArgs = @('restore', $project, '--configfile', $config, '--packages', $env:NUGET_PACKAGES,
            '--disable-parallel', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
        Invoke-LoggedDotnet 'restore-fixture' $restoreArgs
        if ($BuildShell) {
            $restoreArgs[1] = $shell
            $restoreArgs += '-p:Platform=x64'
            Invoke-LoggedDotnet 'restore-shell' $restoreArgs
        }
    }
    $buildArgs = @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', "-p:MissingSourceRevision=$SourceRevision")
    if ($ShellSource) {
        if (-not $ExpectedShellSha256) { throw '基点源码输入必须指定精确 SHA256' }
        $buildArgs += "-p:MissingShellSource=$ShellSource"
        $buildArgs += "-p:ExpectedMissingShellSha256=$ExpectedShellSha256"
    }
    Invoke-LoggedDotnet 'build-fixture' $buildArgs
    # Only the just-successful build can authorize no-build. A red assertion still returns nonzero.
    Invoke-LoggedDotnet 'fixture' @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--',
        '--report', (Join-Path $run 'results.json'))
    if ($BuildShell) {
        Invoke-LoggedDotnet 'build-shell' @('build', $shell, '-c', 'Release', '--no-restore', '-m:1',
            '-p:Platform=x64', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    }
    Write-Output "R-041 证据目录：$run"
}
finally { Pop-Location }
