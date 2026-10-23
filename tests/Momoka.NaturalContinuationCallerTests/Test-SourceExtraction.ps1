[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$area = Join-Path $root 'artifacts\r046-caller'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $area ('extractor-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($area + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw '反例必须写到本树的新目录，不覆盖产品或历史失败。'
}
$null = New-Item -ItemType Directory -Path $output
$utf8 = [Text.UTF8Encoding]::new($false)
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $PSScriptRoot 'Momoka.NaturalContinuationCallerTests.csproj'
$pinsPath = Join-Path $PSScriptRoot 'SourcePins.json'
$pinsText = Get-Content -LiteralPath $pinsPath -Raw
$originalPins = $pinsText | ConvertFrom-Json
$transportPath = 'src/Momoka.Shell/ViewModels/PlayerViewModel.Transport.cs'
$transport = [IO.File]::ReadAllText((Join-Path $root $transportPath), $utf8)
$factsPath = Join-Path $PSScriptRoot 'obj\Release\net10.0\R046\SourceFacts.json'
if (-not (Test-Path -LiteralPath $factsPath)) { throw '先构建当前 Release fixture，以取得完整语法跨度。' }
$facts = Get-Content -LiteralPath $factsPath -Raw | ConvertFrom-Json
$start = @($facts.members | Where-Object name -EQ 'StartPlaybackAsync')
$next = @($facts.members | Where-Object name -EQ 'NextEpisodeToAutoPlay')
if ($start.Count -ne 1 -or $next.Count -ne 1) { throw '来源清单方法不唯一。' }
$startNode = $transport.Substring($start[0].offset, $start[0].length)
$nextNode = $transport.Substring($next[0].offset, $next[0].length)
$cases = @(
    @{ name = 'missing-start'; expected = 'MEMBER_MISSING_OR_AMBIGUOUS StartPlaybackAsync'; text = $transport.Replace('private async Task StartPlaybackAsync(', 'private async Task MissingCallerAsync(') },
    @{ name = 'duplicate-start'; expected = 'MEMBER_MISSING_OR_AMBIGUOUS StartPlaybackAsync count=2'; text = $transport.Insert($transport.LastIndexOf("`n}"), $startNode) },
    @{ name = 'duplicate-next'; expected = 'MEMBER_MISSING_OR_AMBIGUOUS NextEpisodeToAutoPlay count=2'; text = $transport.Insert($transport.LastIndexOf("`n}"), $nextNode) },
    @{ name = 'invalid-syntax'; expected = 'SOURCE_SYNTAX_INVALID'; text = $transport + "`nnamespace {`n" },
    @{ name = 'wrong-namespace'; expected = 'WRONG_NAMESPACE'; text = $transport.Replace('namespace Momoka.Shell.ViewModels;', 'namespace Momoka.R046Wrong;') },
    @{ name = 'missing-await-using'; expected = 'CONTINUATION_AWAIT_USING_MISSING_OR_AMBIGUOUS'; text = $transport.Replace('await using var continuation = result.Continuation;', 'var continuation = result.Continuation;') },
    @{ name = 'hash-drift'; expected = 'SOURCE_HASH_MISMATCH'; text = $transport + "`n// anonymous hash drift counterexample`n"; keepPin = $true }
)
$envNames = @('DOTNET_CLI_HOME', 'TEMP', 'TMP', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_GENERATE_ASPNET_CERTIFICATE',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE', 'MSBUILDDISABLENODEREUSE', 'Configuration')
$saved = @{}
foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$results = [Collections.Generic.List[object]]::new()
try {
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
    $env:Configuration = 'Release'
    if ((& $dotnet --version).Trim() -ne '10.0.400') { throw 'SDK 不匹配。' }
    foreach ($pin in $originalPins.sources) {
        if ((Get-FileHash -LiteralPath (Join-Path $root $pin.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
            throw "产品已改变，停止反例而非自动接受：$($pin.path)"
        }
    }
    $flags = @('-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-p:NuGetAudit=false')
    foreach ($case in $cases) {
        $caseRoot = Join-Path $output $case.name
        $inputs = Join-Path $caseRoot 'readonly-input'
        $null = New-Item -ItemType Directory -Path $inputs
        $pins = $pinsText | ConvertFrom-Json
        foreach ($pin in $pins.sources) {
            $destination = Join-Path $inputs $pin.path
            $parent = Split-Path -Parent $destination
            if (-not (Test-Path -LiteralPath $parent)) { $null = New-Item -ItemType Directory -Path $parent }
            if ($pin.path -eq $transportPath) { [IO.File]::WriteAllText($destination, $case.text, $utf8) }
            else { Copy-Item -LiteralPath (Join-Path $root $pin.path) -Destination $destination }
            if (-not $case.keepPin) { $pin.sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() }
            (Get-Item -LiteralPath $destination).IsReadOnly = $true
        }
        $casePins = Join-Path $caseRoot 'SourcePins.json'
        [IO.File]::WriteAllText($casePins, (($pins | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"), $utf8)
        (Get-Item -LiteralPath $casePins).IsReadOnly = $true
        $log = Join-Path $caseRoot 'build.log'
        & $dotnet build $project @flags "-p:R046SourceRoot=$inputs" "-p:R046SourcePins=$casePins" 2>&1 | Tee-Object -FilePath $log
        $code = $LASTEXITCODE
        $content = Get-Content -LiteralPath $log -Raw
        $matched = $content.Contains($case.expected)
        $results.Add([ordered]@{ case = $case.name; exit = $code; expected = $case.expected; matched = $matched; readonlyInput = $inputs })
        if ($code -ne 1 -or -not $matched) { throw "反例未按预期拒绝：$($case.name)，退出 $code" }
    }
    # Rejected before any source read. Do not inspect another worktree or user filesystem.
    $log = Join-Path $output 'foreign-root.log'
    $foreign = [IO.Path]::GetPathRoot($root)
    & $dotnet build $project @flags "-p:R046SourceRoot=$foreign" 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    $matched = (Get-Content -LiteralPath $log -Raw).Contains('FOREIGN_WORKTREE_INPUT_OR_OUTPUT')
    $results.Add([ordered]@{ case = 'foreign-root'; exit = $code; expected = 'FOREIGN_WORKTREE_INPUT_OR_OUTPUT'; matched = $matched; sourceRead = $false })
    if ($code -ne 1 -or -not $matched) { throw '外树入口未 fail closed。' }
    # The final failed build invalidates no-build eligibility. Rebuild the untouched current source,
    # then run the actual caller; this is never a run of stale output after failed extraction.
    & (Join-Path $PSScriptRoot 'Run-Caller.ps1') -OutputDirectory (Join-Path $output 'current-reverified')
    Write-Output "提取器反例通过：$($results.Count) 个均预期退出 1，之后当前源重新零警告错误构建并实际回归。"
} finally {
    [IO.File]::WriteAllText((Join-Path $output 'counterexamples.json'), (($results | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"), $utf8)
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
