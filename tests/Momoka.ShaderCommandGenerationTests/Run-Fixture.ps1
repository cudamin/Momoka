param(
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe',
    [string]$PackageSource = 'C:\Users\89400\.nuget\packages'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $root 'work\r051'
$run = Join-Path $work ('runner-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
[void](New-Item -ItemType Directory -Path $run)
$env:DOTNET_CLI_HOME = Join-Path $work 'cli-home'
$env:TEMP = Join-Path $work 'temp'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
$env:NUGET_PACKAGES = Join-Path $work 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $work 'http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $work 'plugin-cache'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBUILDDISABLENODEREUSE = '1'
foreach ($directory in @($env:DOTNET_CLI_HOME, $env:TEMP, $env:NUGET_PACKAGES,
        $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_PLUGINS_CACHE_PATH)) {
    [void](New-Item -ItemType Directory -Path $directory -Force)
}
if (!(Test-Path -LiteralPath $Dotnet -PathType Leaf)) { throw "缺少指定 SDK：$Dotnet" }
if (!(Test-Path -LiteralPath $PackageSource -PathType Container)) { throw "缺少固定本机包缓存：$PackageSource" }
$version = & $Dotnet --version
if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne '10.0.400') { throw "SDK 必须是 10.0.400，实际：$version" }
$config = Join-Path $run 'nuget-local.config'
$source = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($PackageSource))
$xml = '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /><add key="fixed-local-cache" value="{0}" /></packageSources></configuration>' -f $source
[IO.File]::WriteAllText($config, $xml + [char]10, [Text.UTF8Encoding]::new($false))

function Invoke-Step {
    param([string]$Name, [string[]]$Arguments, [switch]$Build)
    $log = Join-Path $run ($Name + '.log')
    & $Dotnet @Arguments 2>&1 | Out-File -LiteralPath $log -Encoding UTF8
    $code = $LASTEXITCODE
    Add-Content -LiteralPath $log -Value ('R051_EXIT_CODE=' + $code) -Encoding UTF8
    Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Last 12
    if ($code -ne 0) { throw "$Name 退出 $code；保留日志：$log" }
    $text = Get-Content -LiteralPath $log -Raw -Encoding UTF8
    if ($text -match 'warning [A-Z]+[0-9]+:' -or $text -match 'error [A-Z]+[0-9]+:') {
        throw "$Name 存在诊断；不运行旧产物：$log"
    }
    if ($text -match '跳过项目|Skipping project|MSB3202|NU1101|NU1102') { throw "$Name 未完整加载/缺包：$log" }
    if ($Build -and $text -notmatch '(0 个警告|0 Warning\(s\))') { throw "$Name 没有零警告构建证据：$log" }
}

Push-Location -LiteralPath $root
try {
    $project = Join-Path $PSScriptRoot 'Momoka.ShaderCommandGenerationTests.csproj'
    $flags = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
    Invoke-Step -Name 'restore' -Arguments (@('restore', $project, '--configfile', $config,
        '--packages', $env:NUGET_PACKAGES, '--disable-parallel', '-p:NuGetAudit=false') + $flags)
    Invoke-Step -Name 'build' -Build -Arguments (@('build', $project, '-c', 'Release', '--no-restore') + $flags)
    Invoke-Step -Name 'tests' -Arguments @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore')
    $text = Get-Content -LiteralPath (Join-Path $run 'tests.log') -Raw -Encoding UTF8
    if ($text -notmatch '共 37 项，通过 37，失败 0，跳过 0') { throw "测试不完整：$run" }
    Write-Output ('R051_RUN_DIRECTORY=' + $run)
}
finally { Pop-Location }
