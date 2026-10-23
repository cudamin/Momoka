[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gitRoot = (& git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须在此 fixture 所属工作树运行。' }
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$area = Join-Path $root 'artifacts\r046-caller'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $area ('verify-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($area + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw '输出必须是本树 artifacts\r046-caller 下的新目录；不覆盖历史失败。'
}
$null = New-Item -ItemType Directory -Path $output
$utf8 = [Text.UTF8Encoding]::new($false)
$envNames = @('DOTNET_CLI_HOME', 'TEMP', 'TMP', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_GENERATE_ASPNET_CERTIFICATE',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE', 'MSBUILDDISABLENODEREUSE')
$saved = @{}
foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    foreach ($name in @('cli-home', 'temp', 'packages', 'feed', 'nuget-http')) {
        $path = Join-Path $area $name
        if (-not (Test-Path -LiteralPath $path)) { $null = New-Item -ItemType Directory -Path $path }
    }
    $env:DOTNET_CLI_HOME = Join-Path $area 'cli-home'
    $env:TEMP = Join-Path $area 'temp'
    $env:TMP = $env:TEMP
    $env:NUGET_PACKAGES = Join-Path $area 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $area 'nuget-http'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
    $env:MSBUILDDISABLENODEREUSE = '1'
    if ((& $dotnet --version).Trim() -ne '10.0.400') { throw '仅允许已核实的 10.0.400 SDK。' }
    $pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SourcePins.json') -Raw | ConvertFrom-Json
    foreach ($pin in $pins.sources) {
        $path = [IO.Path]::GetFullPath((Join-Path $root $pin.path))
        if (-not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '源超出工作树。' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
            throw "当前源 hash 漂移，停止而不是自动改 pins：$path"
        }
    }
    $feed = Join-Path $area 'feed'
    foreach ($package in @(@('microsoft.visualstudio.threading.analyzers', '18.7.23'),
        @('microsoft.codeanalysis.bannedapianalyzers', '5.6.0'), @('roslynator.analyzers', '5.0.0'))) {
        $source = Join-Path 'C:\Users\89400\.nuget\packages' "$($package[0])\$($package[1])\$($package[0]).$($package[1]).nupkg"
        if (-not (Test-Path -LiteralPath $source)) { throw "本机固定缓存缺包，停止：$source" }
        $destination = Join-Path $feed ([IO.Path]::GetFileName($source))
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw '隔离 feed 与固定本机缓存不一致。' }
        } else { Copy-Item -LiteralPath $source -Destination $destination }
    }
    $project = Join-Path $PSScriptRoot 'Momoka.NaturalContinuationCallerTests.csproj'
    $flags = @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-p:NuGetAudit=false')
    & $dotnet restore $project --source $feed --packages $env:NUGET_PACKAGES -p:RestoreDisableParallel=true @flags 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'restore.log')
    if ($LASTEXITCODE -ne 0) { throw '离线固定包 restore 失败；未运行旧测试。' }
    & $dotnet build $project -c Release --no-restore @flags 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $buildExit = $LASTEXITCODE
    $buildLog = Get-Content -LiteralPath (Join-Path $output 'build.log') -Raw
    if ($buildExit -ne 0 -or $buildLog -notmatch '(?m)^\s*0 (个警告|Warning\(s\))\s*$' -or $buildLog -notmatch '(?m)^\s*0 (个错误|Error\(s\))\s*$') {
        throw '当前 Release 构建不是零警告零错误；禁止 no-build。'
    }
    & $dotnet run --project $project -c Release --no-build --no-restore -- $root (Join-Path $output 'dynamic') 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'tests.log')
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { throw "实际 VM caller 回归失败，保留本次报告：$output" }
    $results = Get-Content -LiteralPath (Join-Path $output 'dynamic\results.json') -Raw | ConvertFrom-Json
    if ($results.failed -ne 0 -or $results.skipped -ne 0 -or $results.exit -ne 0) { throw '报告中仍有失败或跳过。' }
    [IO.File]::WriteAllText((Join-Path $output 'verification.json'), (([ordered]@{
        date = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'); root = $root; head = (& git -C $root rev-parse HEAD).Trim()
        sdk = '10.0.400'; buildExit = $buildExit; warning = 0; error = 0; dynamic = $results
        productSnapshotsUncommitted = $true; noAppWindowNativeOrRealServer = $true
    } | ConvertTo-Json -Depth 8).Replace("`r`n", "`n") + "`n"), $utf8)
    Write-Output "无窗口 caller 验证通过：$($results.passed) 项；证据 $output"
} finally {
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
