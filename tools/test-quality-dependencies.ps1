[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/quality-dependency-tests/' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'anonymous'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'tools')) | Out-Null
$bom = [Text.UTF8Encoding]::new($true)
function Save([string]$Name, [string]$Text) { [IO.File]::WriteAllText((Join-Path $fixture $Name), $Text.Replace("`r`n", "`n"), $bom) }
Save 'Momoka.sln' 'anonymous product marker'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'quality.ps1'))
$lookup = '$dotnet = Join-Path $HOME ''.dotnet/dotnet.exe'''
if ($source.Split([string[]]@($lookup), [StringSplitOptions]::None).Length -ne 2) { throw 'SDK discovery boundary changed.' }
Save 'tools/quality.ps1' $source.Replace($lookup, '$dotnet = Join-Path $root ''fake-dotnet.ps1''')
foreach ($name in @('check-scripts.ps1', 'check-diff.ps1', 'check-duplication.ps1')) { Save ("tools/$name") 'exit 0' }
Save 'fake-dotnet.ps1' @'
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'calls.log'), (($args | ConvertTo-Json -Compress) + "`n"))
$mode = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'mode.txt'))
if ($args[0] -eq 'build' -and $mode -eq 'failure') { Write-Output 'anonymous build error'; exit 17 }
if ($args[0] -eq 'build' -and $mode -eq 'warning') { Write-Output 'anonymous: warning TEST001: warning'; exit 0 }
if ($args[0] -eq 'run') { Write-Output '共 1 项，通过 1，失败 0，跳过 0' }
exit 0
'@
Save 'tools/run-regressions.ps1' @'
param([string[]]$Suite)
if ($Suite.Count -ne 1 -or $Suite[0] -ne 'Momoka.ArchitectureTests') { throw 'unexpected selection' }
[IO.File]::AppendAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'calls.log'), 'fresh-architecture-runner' + "`n")
exit 0
'@
$results = @()
foreach ($mode in @('failure', 'warning', 'success', 'skip-build')) {
    Save 'mode.txt' $mode
    Save 'calls.log' ''
    $flags = @()
    if ($mode -eq 'skip-build') { $flags += '-SkipBuild' }
    $log = Join-Path $run "$mode.log"
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'tools/quality.ps1') -Repo $fixture -SkipDuplication @flags *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    $calls = [IO.File]::ReadAllText((Join-Path $fixture 'calls.log'))
    $passed = if ($mode -in @('failure', 'warning')) { $code -ne 0 -and -not $calls.Contains('"run"') -and [IO.File]::ReadAllText($log).Contains('未执行') }
        elseif ($mode -eq 'skip-build') { $code -eq 0 -and $calls.Contains('fresh-architecture-runner') }
        else { $code -eq 0 -and $calls.Contains('"run"') }
    $results += [pscustomobject]@{ name=$mode; passed=$passed; exitCode=$code }
    Write-Output "$mode : $passed"
}
[IO.File]::WriteAllText((Join-Path $run 'results.json'), ($results | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$failed = @($results | Where-Object { -not $_.passed }).Count
Write-Output "共 $($results.Count) 项，通过 $($results.Count - $failed)，失败 $failed，跳过 0。证据：$run"
if ($failed) { exit 1 }
