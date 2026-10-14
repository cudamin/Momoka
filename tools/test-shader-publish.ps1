[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$run = Join-Path $repo ('artifacts\shader-publish-tests\' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $run 'publish'
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$required = @(
    'Momoka.exe', 'Momoka.deps.json', 'Momoka.runtimeconfig.json', 'libmpv-2.dll', 'vulkan-1.dll',
    'Momoka.pri', 'mpv-ui\scripts\uosc\main.lua', 'mpv-ui\scripts\uosc\lib\utils.lua',
    'mpv-ui\scripts\uosc\elements\Timeline.lua', 'mpv-ui\fonts\uosc_textures.ttf',
    'mpv-ui\fonts\MaterialIconsRound-Regular.otf', 'mpv-ui\LICENSE.LGPL',
    'LICENSE', 'LICENSE.LGPL-2.1.txt', 'LICENSE.LGPL-3.0.txt', 'LICENSE.GPL-3.0.txt',
    'LICENSE.Apache-2.0.txt', 'THIRD-PARTY-NOTICES.md'
)
foreach ($relative in $required) {
    $target = Join-Path $root $relative
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    [System.IO.File]::WriteAllText($target, 'fixture themeresources')
}
# 那三份 LGPL/GPL 正文不行：verify-publish 拿它们与仓库逐字节比对，占位文本会被当场判为「与仓库不一致」，
# 于是每条用例都变成同一种失败（假载荷自己不合格），真正的判据全被掩盖。所以从仓库真拷。
foreach ($name in @('LICENSE.LGPL-2.1.txt', 'LICENSE.LGPL-3.0.txt', 'LICENSE.GPL-3.0.txt')) {
    Copy-Item -LiteralPath (Join-Path $repo "assets\mpv-runtime\$name") -Destination (Join-Path $root $name) -Force
}
$source = Join-Path $repo 'assets\shaders'
Copy-Item -LiteralPath $source -Destination (Join-Path $root 'shaders') -Recurse
$results = [System.Collections.Generic.List[object]]::new()
$check = Join-Path $PSScriptRoot 'verify-publish.ps1'

function Invoke-Check([string]$Name, [bool]$Success, [string]$Needle = '') {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $check -PublishRoot $root -RepositoryRoot $repo 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    $text = $lines | Out-String
    $ok = (($code -eq 0) -eq $Success) -and ($Needle.Length -eq 0 -or $text.Contains($Needle))
    $results.Add([pscustomobject]@{ name=$Name; passed=$ok; exitCode=$code; output=$text })
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
