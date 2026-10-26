param(
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\')
$actual = (& git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $actual -ne $root) { throw 'Git 根目录不属于本夹具树' }
$evidence = Join-Path $root 'artifacts\r041-missing-search-availability'
$run = Join-Path $evidence ('extraction-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
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

function Hash-Text([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($encoding.GetBytes($Value))).Replace('-', '') }
    finally { $sha.Dispose() }
}

Push-Location -LiteralPath $root
try {
    $version = (& $Dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -ne '10.0.400') { throw '提取验证只允许 SDK 10.0.400' }
    $source = Join-Path $root 'src\Momoka.Shell\Views\ShellPage.xaml.cs'
    $text = [IO.File]::ReadAllText($source, [Text.UTF8Encoding]::new($false, $true))
    $guard = '    private bool _missingEpisodesShowing;'
    $signature = '    private async Task ShowMissingEpisodesAsync(EmbyItem item, IReadOnlyList<EmbyItem> missing)'
    $end = '    private bool _moviePilotReorganizeShowing;'
    if (-not $text.Contains($signature) -or -not $text.Contains($end)) { throw '当前源码边界已改变，拒绝构造反例' }
    $tests = @(
        @{ name = 'current-source'; text = $text; fail = $false; expected = '' },
        @{ name = 'missing-signature'; text = $text.Replace($signature, $signature.Replace('ShowMissingEpisodesAsync', 'OtherMissingEpisodesAsync')); fail = $true; expected = 'missing/ambiguous actual boundary' },
        @{ name = 'duplicate-start'; text = $text.Replace($guard, $guard + "
" + $guard); fail = $true; expected = 'missing/ambiguous actual boundary' },
        @{ name = 'duplicate-end'; text = $text.Replace($end, $end + "
" + $end); fail = $true; expected = 'missing/ambiguous actual boundary' },
        @{ name = 'expanded-member'; text = $text.Replace($end, '    private int FixtureMustNotCaptureThis;' + "
" + $end); fail = $true; expected = 'expanded single-member extraction boundary' },
        @{ name = 'unbalanced-body'; text = $text.Replace($signature + "
    {", $signature + "
    { {"); fail = $true; expected = 'single-member extraction boundary' },
        @{ name = 'unsupported-literal'; text = $text.Replace($signature + "
    {", $signature + "
    {
        var unsupported = @""fixture"";"); fail = $true; expected = 'unsupported verbatim/raw literal' },
        @{ name = 'override-no-hash'; text = $text; nohash = $true; fail = $true; expected = 'override requires an expected Git-blob SHA256' },
        @{ name = 'hash-mismatch'; text = $text; wronghash = $true; fail = $true; expected = 'input SHA256 mismatch' },
        @{ name = 'outside-tree'; text = $text; outside = $true; fail = $true; expected = 'input/output must remain in this worktree' }
    )
    $results = @()
    foreach ($test in $tests) {
        $input = Join-Path $run ($test.name + '.input.cs')
        [IO.File]::WriteAllText($input, $test.text, $encoding)
        $hash = Hash-Text $test.text
        $sourceArg = if ($test.outside) { [IO.Path]::GetFullPath((Join-Path $root '..\outside-tree-input.cs')) } else { $input }
        $arguments = @('msbuild', (Join-Path $PSScriptRoot 'Momoka.MissingEpisodesAvailabilityTests.csproj'),
            '-target:GenerateR041MissingEpisodesSources', '-m:1', '-p:BuildInParallel=false',
            '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false', '-p:Configuration=Release',
            "-p:MissingShellSource=$sourceArg", "-p:MissingSourceRevision=boundary-$($test.name)",
            "-p:IntermediateOutputPath=obj\extraction-$($test.name)\")
        if (-not $test.nohash) {
            $expectedHash = if ($test.wronghash) { '0' * 64 } else { $hash }
            $arguments += "-p:ExpectedMissingShellSha256=$expectedHash"
        }
        $output = & $Dotnet @arguments 2>&1
        $code = $LASTEXITCODE
        $log = ($output | ForEach-Object { $_.ToString() }) -join "
"
        $logFile = Join-Path $run ($test.name + '.log')
        [IO.File]::WriteAllText($logFile, $log + "
", $encoding)
        $passed = if ($test.fail) { $code -ne 0 -and $log.Contains('R041 extraction rejected:') -and $log.Contains($test.expected) } else { $code -eq 0 }
        $results += @{ name = $test.name; passed = $passed; expectedFailure = $test.fail; exitCode = $code;
            input = $input; inputSha256 = $hash; log = $logFile; arguments = $arguments }
        Write-Output ("{0} {1}，子命令退出 {2}" -f $(if ($passed) { '通过' } else { '失败' }), $test.name, $code)
    }
    $failed = @($results | Where-Object { -not $_.passed }).Count
    $summary = @{ record = 'R-041'; kind = 'build-extraction-boundary'; sdk = $version; total = $results.Count;
        passed = $results.Count - $failed; failed = $failed; skipped = 0; actualSourceUnchanged = ((Hash-Text ([IO.File]::ReadAllText($source))) -eq (Hash-Text $text));
        noRuntimeOrTestsLaunched = $true; results = $results }
    [IO.File]::WriteAllText((Join-Path $run 'results.json'), ($summary | ConvertTo-Json -Depth 8) + "
", $encoding)
    Write-Output "提取闸门共 $($results.Count) 项，通过 $($results.Count - $failed)，失败 $failed，跳过 0；$run"
    if ($failed -ne 0) { throw '构建提取边界验证失败；所有输入与日志保留' }
}
finally { Pop-Location }
