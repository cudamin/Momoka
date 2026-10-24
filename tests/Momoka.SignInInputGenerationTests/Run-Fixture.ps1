[CmdletBinding()]
param(
    [string]$RunName = 'current',
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe',
    [string]$FixedPackageCache = 'C:\Users\89400\.nuget\packages',
    [switch]$Baseline,
    [ValidateSet('1b37ca14bbbdf5699ac686aac891a7b2e14c9bdf', 'cdfb83212694953b8a39d1c2ad9d7cab57f81197', 'afd448469fdbd7db947668254739ee57b693bb0a')]
    [string]$BaselineCommit = '1b37ca14bbbdf5699ac686aac891a7b2e14c9bdf',
    [switch]$Restore,
    [switch]$BuildShell,
    [switch]$Format
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw '本夹具使用PowerShell 7，禁止更换认证或恢复链' }
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须在夹具所属的独立工作树执行' }
if ($RunName -notmatch '^[a-z0-9-]+$') { throw 'RunName只允许小写字母、数字和连字符' }
$baseCommit = $BaselineCommit
$evidence = Join-Path $root 'artifacts\r008-signin-input-generation'
$run = Join-Path $evidence ('run-' + $RunName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
$variant = if ($Baseline) { 'baseline' } else { 'current' }
$buildRoot = Join-Path $evidence ('build-' + $variant)
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($directory in @($run, $buildRoot, (Join-Path $evidence 'cli-home'), (Join-Path $evidence 'temp'),
    (Join-Path $evidence 'nuget-http'), (Join-Path $evidence 'nuget-plugins'),
    (Join-Path $root 'work\r008\f'), (Join-Path $root 'work\r008\n'))) {
    [void][IO.Directory]::CreateDirectory($directory)
}
$env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:Configuration = 'Release'
$env:NUGET_PACKAGES = Join-Path $root 'work\r008\n'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $evidence 'nuget-plugins'
$env:TEMP = Join-Path $evidence 'temp'
$env:TMP = $env:TEMP
$env:MSBUILDDISABLENODEREUSE = '1'

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $run ($Name + '.log')
    & $Dotnet @Arguments > $log 2>&1
    $code = $LASTEXITCODE
    $lines = @(Get-Content -LiteralPath $log -Encoding UTF8)
    $lines | Select-Object -Last 75 | Write-Output
    $entry = @{ name = $Name; arguments = $Arguments; exitCode = $code; log = $log } | ConvertTo-Json -Compress
    [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), $entry + "`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $run ($Name + '.exit.txt')), "$code`n", $encoding)
    if ($code -ne 0) { throw "$Name 退出 $code；所有失败留在本树，立即停止，不跑旧输出、不改源或换外网" }
    if ($Name -like 'build-*' -and ($lines -match '\b(?:warning|error)\s+[A-Z]+\d+\b')) {
        throw "$Name 非零警告/错误；保留日志并停止"
    }
}

function Write-ExactGitBlob([string]$Spec, [string]$Destination) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = 'git'
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.ArgumentList.Add('show')
    $info.ArgumentList.Add($Spec)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $memory = [IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($memory)
            $errorText = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw "基点Gitblob读取失败：$errorText" }
            [IO.File]::WriteAllBytes($Destination, $memory.ToArray())
        }
        finally { $memory.Dispose() }
    }
    finally { $process.Dispose() }
    $expected = (& git -C $root rev-parse $Spec).Trim()
    if ($LASTEXITCODE -ne 0) { throw '基点blob不存在' }
    $actual = (& git -C $root hash-object --no-filters $Destination).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) { throw '基点源码blob字节不符；停止' }
    [IO.File]::WriteAllText((Join-Path $run ([IO.Path]::GetFileNameWithoutExtension($Destination) + '-gitblob.txt')), "$Spec`n$expected`n", $encoding)
}

Push-Location -LiteralPath $root
try {
    Write-Output "R-008 $variant 证据目录：$run"
    Invoke-LoggedDotnet 'sdk-version' @('--version')
    if ([IO.File]::ReadAllText((Join-Path $run 'sdk-version.log')).Trim() -ne '10.0.400') { throw 'SDK必须为10.0.400' }
    [IO.File]::WriteAllText((Join-Path $run 'environment.json'), (@{
        date = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'); timezone = 'Asia/Shanghai'; baseCommit = $baseCommit
        workingTree = $root; variant = $variant; cliHome = $env:DOTNET_CLI_HOME; temp = $env:TEMP
        packages = $env:NUGET_PACKAGES; certificateGeneration = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
    } | ConvertTo-Json) + "`n", $encoding)
    $project = Join-Path $PSScriptRoot 'Momoka.SignInInputGenerationTests.csproj'
    $shell = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'
    $feed = Join-Path $root 'work\r008\f'
    $config = Join-Path $evidence 'offline.nuget.config'
    $snapshot = Join-Path $evidence 'fixed-cache-manifest.json'
    $serial = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    $fixturePaths = @("-p:BaseIntermediateOutputPath=$buildRoot\obj\", "-p:BaseOutputPath=$buildRoot\bin\")
    if ($Restore) {
        if (-not (Test-Path -LiteralPath $snapshot -PathType Leaf)) {
            if (-not (Test-Path -LiteralPath $FixedPackageCache -PathType Container)) { throw 'fixed缓存不存在；禁止外网补包' }
            $manifest = @()
            foreach ($archive in (Get-ChildItem -LiteralPath $FixedPackageCache -File -Filter '*.nupkg' -Recurse | Sort-Object FullName)) {
                $target = Join-Path $feed $archive.Name
                if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Copy-Item -LiteralPath $archive.FullName -Destination $target }
                $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
                if ($hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw 'fixed快照哈希不同；停止' }
                $manifest += @{ archive = $archive.Name; sha256 = $hash; source = $archive.FullName }
            }
            if ($manifest.Count -eq 0) { throw 'fixed缓存无归档；停止' }
            [IO.File]::WriteAllText($snapshot, ($manifest | ConvertTo-Json -Depth 4) + "`n", $encoding)
            $escaped = [Security.SecurityElement]::Escape($feed)
            $xml = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<configuration><packageSources><clear/><add key=`"fixed-local`" value=`"$escaped`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>`n"
            [IO.File]::WriteAllText($config, $xml, $encoding)
        }
        foreach ($item in (Get-Content -LiteralPath $snapshot -Raw -Encoding UTF8 | ConvertFrom-Json)) {
            $archive = Join-Path $feed $item.archive
            if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or (Get-FileHash -LiteralPath $archive).Hash -ne $item.sha256) {
                throw '已固定快照缺包/变更；首次失败即停止，禁止外网或刷新缓存'
            }
        }
        Copy-Item -LiteralPath $snapshot -Destination (Join-Path $run 'fixed-cache-manifest.json')
        # NuGetAudit=false belongs only to these offline restore invocations, never project/config/env.
        $restoreArgs = @('restore', $project, '--configfile', $config, '--packages', $env:NUGET_PACKAGES,
            '--disable-parallel', '-p:NuGetAudit=false') + $serial + $fixturePaths
        Invoke-LoggedDotnet 'restore-fixture' $restoreArgs
        if ($BuildShell) {
            Invoke-LoggedDotnet 'restore-shell' (@('restore', $shell, '--configfile', $config, '--packages', $env:NUGET_PACKAGES,
                '--disable-parallel', '-p:NuGetAudit=false', '-p:Platform=x64') + $serial)
        }
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $buildRoot 'obj\project.assets.json') -PathType Leaf)) {
        throw '无当前变体restore；停止，不使用另一夹具或主树assets'
    }
    $source = Join-Path $root 'src\Momoka.Shell\ViewModels\SignInViewModel.cs'
    $pageSource = Join-Path $root 'src\Momoka.Shell\ViewModels\PageViewModel.cs'
    if ($Baseline) {
        $source = Join-Path $run 'SignInViewModel.cs'
        $pageSource = Join-Path $run 'PageViewModel.cs'
        Write-ExactGitBlob ($baseCommit + ':src/Momoka.Shell/ViewModels/SignInViewModel.cs') $source
        Write-ExactGitBlob ($baseCommit + ':src/Momoka.Shell/ViewModels/PageViewModel.cs') $pageSource
    }
    $inputs = @($source, $pageSource, $project, $PSCommandPath) + @(
        'src\Momoka.Core\Infrastructure\LoadGeneration.cs',
        'src\Momoka.Core\Emby\EmbySignInIdentity.cs', 'src\Momoka.Core\Emby\EmbyServerAddress.cs',
        'src\Momoka.Core\FnNas\FnNasEndpoint.cs', 'src\Momoka.Core\Plex\PlexEndpoint.cs',
        'src\Momoka.Shell\Views\SignInPage.xaml', 'src\Momoka.Shell\Views\SignInPage.xaml.cs',
        'tests\Momoka.Tests\TestHarness.cs', 'tests\Momoka.SubtitleDialogTests\UiQueue.cs',
        'tests\Momoka.SignInInputGenerationTests\Boundaries.cs', 'tests\Momoka.SignInInputGenerationTests\FakeSignInAttempt.cs',
        'tests\Momoka.SignInInputGenerationTests\Program.cs', 'tests\Momoka.SignInInputGenerationTests\NullSelectionTests.cs',
        'tests\Momoka.SignInInputGenerationTests\SynchronousEntryTests.cs',
        'tests\Momoka.SignInInputGenerationTests\SynchronousTailTests.cs',
        'tests\Momoka.SignInInputGenerationTests\Test-GeneratedBindings.ps1'
    ) | ForEach-Object { if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $root $_ } }
    $hashes = @($inputs | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash } })
    [IO.File]::WriteAllText((Join-Path $run 'inputs.json'), ($hashes | ConvertTo-Json -Depth 4) + "`n", $encoding)
    Invoke-LoggedDotnet 'build-fixture' (@('build', $project, '-c', 'Release', '--no-restore', "-p:SignInSourcePath=$source", "-p:SignInPageSourcePath=$pageSource") + $serial + $fixturePaths)
    # No --no-build run is reachable without the successful current Release build immediately above.
    Invoke-LoggedDotnet 'fixture' (@('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore') + $fixturePaths)
    if ($Format) {
        $previousIntermediate = $env:BaseIntermediateOutputPath
        $previousOutput = $env:BaseOutputPath
        $env:BaseIntermediateOutputPath = "$buildRoot\obj\"
        $env:BaseOutputPath = "$buildRoot\bin\"
        try {
            Invoke-LoggedDotnet 'format-fixture' @('format', 'whitespace', $project, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic',
                '--report', (Join-Path $run 'format-fixture-report.json'), '--include',
                (Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'NullSelectionTests.cs'),
                (Join-Path $PSScriptRoot 'Boundaries.cs'), (Join-Path $PSScriptRoot 'FakeSignInAttempt.cs'))
        }
        finally {
            $env:BaseIntermediateOutputPath = $previousIntermediate
            $env:BaseOutputPath = $previousOutput
        }
        $previousPlatform = $env:Platform
        $env:Platform = 'x64'
        try {
            Invoke-LoggedDotnet 'format-vm' @('format', 'whitespace', $shell, '--verify-no-changes', '--no-restore', '--verbosity', 'diagnostic',
                '--report', (Join-Path $run 'format-vm-report.json'), '--include', (Join-Path $root 'src\Momoka.Shell\ViewModels\SignInViewModel.cs'),
                (Join-Path $root 'src\Momoka.Shell\ViewModels\PageViewModel.cs'))
        }
        finally { $env:Platform = $previousPlatform }
    }
    if ($BuildShell) {
        # Format may touch generated files, so the actual binding receipt follows a successful Shell
        # build after formatting, never backup/g.i files or another tree's DLL.
        Invoke-LoggedDotnet 'build-shell' (@('build', $shell, '-c', 'Release', '--no-restore', '-p:Platform=x64') + $serial)
        $generated = Join-Path $root 'src\Momoka.Shell\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\Views\SignInPage.g.cs'
        $assembly = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
        $buildReceipt = Join-Path $run 'current-shell-build.json'
        $shellInputs = @(
            'src\Momoka.Shell\ViewModels\SignInViewModel.cs', 'src\Momoka.Shell\ViewModels\PageViewModel.cs',
            'src\Momoka.Core\Infrastructure\LoadGeneration.cs', 'src\Momoka.Shell\Views\SignInPage.xaml',
            'src\Momoka.Shell\Views\SignInPage.xaml.cs', 'src\Momoka.Shell\Momoka.Shell.csproj',
            'Directory.Build.props', 'tools\analyzers\Microsoft.WindowsAppSDK.Analyzers.dll',
            'tools\analyzers\Microsoft.WindowsAppSDK.Analyzers.targets'
        ) | ForEach-Object { $p = Join-Path $root $_; @{ path = $p; sha256 = (Get-FileHash -LiteralPath $p).Hash } }
        [IO.File]::WriteAllText($buildReceipt, (@{
            workingTree = $root; exitCode = 0; buildLog = (Join-Path $run 'build-shell.log'); inputs = @($shellInputs)
            generated = @{ path = $generated; sha256 = (Get-FileHash -LiteralPath $generated).Hash }
            assembly = @{ path = $assembly; sha256 = (Get-FileHash -LiteralPath $assembly).Hash }
        } | ConvertTo-Json -Depth 6) + "`n", $encoding)
        foreach ($counterexample in @($false, $true)) {
            $name = if ($counterexample) { 'generator-counterexamples' } else { 'generated-bindings' }
            $log = Join-Path $run ($name + '.log')
            $code = 0
            try {
                & (Join-Path $PSScriptRoot 'Test-GeneratedBindings.ps1') -BuildReceipt $buildReceipt -Counterexamples:$counterexample > $log 2>&1
            }
            catch {
                $code = 1
                [IO.File]::AppendAllText($log, $_.Exception.Message + "`n", $encoding)
            }
            Get-Content -LiteralPath $log -Encoding UTF8 | Write-Output
            [IO.File]::WriteAllText((Join-Path $run ($name + '.exit.txt')), "$code`n", $encoding)
            [IO.File]::AppendAllText((Join-Path $run 'commands.jsonl'), (@{
                name = $name; script = (Join-Path $PSScriptRoot 'Test-GeneratedBindings.ps1')
                buildReceipt = $buildReceipt; counterexamples = $counterexample; exitCode = $code; log = $log
            } | ConvertTo-Json -Compress) + "`n", $encoding)
            if ($code -ne 0) { throw "$name 验证失败，保留日志并停止，不使用旧绑定证据" }
        }
    }
}
finally { Pop-Location }
