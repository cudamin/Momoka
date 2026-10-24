[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$run = Join-Path $repo ('artifacts\shader-publish-tests\' + [guid]::NewGuid().ToString('N'))
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
# 使用同一生产校验路径的完整 approved 合成载荷；不再用六份 uosc / 任意 native 占位绕过新门禁。
$fixture = New-MpvPayloadFixture $repo (Join-Path $run 'fixture')
$root = $fixture.publish
$fixtureRepository = $fixture.repository
$source = Join-Path $fixtureRepository 'assets\shaders'
$results = [System.Collections.Generic.List[object]]::new()
$check = Join-Path $PSScriptRoot 'verify-publish.ps1'

function Invoke-Check([string]$Name, [bool]$Success, [string]$Needle = '') {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $check -PublishRoot $root -RepositoryRoot $fixtureRepository 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    $text = $lines | Out-String
    $ok = (($code -eq 0) -eq $Success) -and ($Needle.Length -eq 0 -or $text.Contains($Needle))
    $results.Add([pscustomobject]@{ name=$Name; passed=$ok; exitCode=$code; output=$text; repository=$fixtureRepository; publish=$root; nativeIsSynthetic=$true })
    Write-Output ("{0}: {1}" -f $Name, $ok)
}

Invoke-Check 'complete payload' $true
$payload = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Extension -in @('.glsl', '.hook', '.txt') })
foreach ($file in $payload) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $target = Join-Path (Join-Path $root 'shaders') $relative
    $bytes = [System.IO.File]::ReadAllBytes($target)
    Remove-Item -LiteralPath $target
    Invoke-Check ('missing ' + $relative) $false $file.Name
    [System.IO.File]::WriteAllBytes($target, $bytes)
}
$first = $payload | Where-Object { $_.Extension -eq '.hook' } | Select-Object -First 1
$relative = $first.FullName.Substring($source.Length + 1)
$target = Join-Path (Join-Path $root 'shaders') $relative
$bytes = [System.IO.File]::ReadAllBytes($target)
[System.IO.File]::WriteAllText($target, 'damaged shader')
Invoke-Check 'corrupt hook' $false $first.Name
[System.IO.File]::WriteAllBytes($target, $bytes)
$extra = Join-Path $root 'shaders\unexpected.hook'
[System.IO.File]::WriteAllText($extra, 'unexpected')
Invoke-Check 'unexpected shader' $false 'unexpected.hook'
Remove-Item -LiteralPath $extra

# 许可正文在两个位置各有一份（exe 根给 libmpv-2.dll、shaders\ 给着色器），发布根那一份必须与仓库源头
# 一致。改掉它必须报出来，判据是文件名 —— 只报「不一致」而不说是哪一份，等于让人逐个文件去猜。
$sharedLicense = Join-Path $root 'LICENSE.LGPL-2.1.txt'
$savedLicense = [System.IO.File]::ReadAllBytes($sharedLicense)
[System.IO.File]::WriteAllText($sharedLicense, 'damaged license')
Invoke-Check 'corrupt published license' $false 'LICENSE.LGPL-2.1.txt'
[System.IO.File]::WriteAllBytes($sharedLicense, $savedLicense)
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding UTF8
$failed = @($results | Where-Object { -not $_.passed }).Count
Write-Output ("shader publish checks: {0} total, {1} failed; {2}" -f $results.Count, $failed, $run)
if ($failed -gt 0) { exit 1 }
exit 0
