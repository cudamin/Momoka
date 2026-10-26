[CmdletBinding()]
param(
    [string]$Phase = 'green'
)

$ErrorActionPreference = 'Stop'
if ($Phase -notmatch '^[a-z0-9-]+$') { throw 'Phase 只接受小写字母、数字和连字符。' }
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
Assert-PlainPath $repo
$run = Get-ChildPath $repo ('artifacts/r067-approved-payload/' + $Phase + '-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
[IO.Directory]::CreateDirectory($run) | Out-Null
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
$fixture = New-MpvPayloadFixture $repo (Join-Path $run 'complete-synthetic')
$check = Join-Path $PSScriptRoot 'verify-publish.ps1'
$results = [Collections.Generic.List[object]]::new()
$encoding = [Text.UTF8Encoding]::new($false)

function Assert-FixtureTarget([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "夹具目标越界：$resolved" }
    return $resolved
}

function Save-Result([string]$Name, [bool]$Passed, [int]$ExpectedExit, $Result) {
    $index = $results.Count + 1
    $log = Join-Path $run ('case-' + $index.ToString('D3') + '.log')
    [IO.File]::WriteAllText($log, $Result.output.Replace("`r`n", "`n"), $encoding)
    $results.Add([pscustomobject]@{ name=$Name; passed=$Passed; expectedExit=$ExpectedExit;
        exitCode=$Result.exitCode; executable=$Result.executable; arguments=$Result.arguments; log=$log;
        repository=$fixture.repository; publish=$fixture.publish; nativeLoaded=$false; realPublish=$false; output=$Result.output })
    [IO.File]::WriteAllText((Join-Path $run 'case-results.json'), ($results.ToArray() | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n", $encoding)
    Write-Output ("{0}: {1}; expected={2}, actual={3}" -f $Name, $Passed, $ExpectedExit, $Result.exitCode)
}

function Invoke-Check([string]$Name, [bool]$Success, [string]$Needle = '', [switch]$SelfContained, [string]$Repository = $fixture.repository) {
    $result = Invoke-MpvPublishFixture $check $Repository $fixture.publish -SelfContained:$SelfContained
    $expected = if ($Success) { 0 } else { 1 }
    $passed = $result.exitCode -eq $expected -and ($Needle.Length -eq 0 -or $result.output.Contains($Needle))
    Save-Result $Name $passed $expected $result
}

function Invoke-FileMutation([string]$Name, [string]$Path, [scriptblock]$Mutation, [string]$Needle) {
    $target = Assert-FixtureTarget $Path
    $bytes = if (Test-Path -LiteralPath $target -PathType Leaf) { [IO.File]::ReadAllBytes($target) } else { $null }
    try {
        & $Mutation $target
        Invoke-Check $Name $false $Needle
    } finally {
        if ($null -ne $bytes) { [IO.File]::WriteAllBytes($target, $bytes) }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath (Assert-FixtureTarget $target) -Force }
    }
}

function Invoke-ManifestMutation([string]$Name, [scriptblock]$Mutation, [string]$Needle) {
    $target = Assert-FixtureTarget $fixture.manifest
    $bytes = [IO.File]::ReadAllBytes($target)
    $manifest = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($target))
    try {
        & $Mutation $manifest
        [IO.File]::WriteAllText($target, ($manifest | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n", $encoding)
        Invoke-Check $Name $false $Needle
    } finally { [IO.File]::WriteAllBytes($target, $bytes) }
}

# 独立断言真实批准数据，并实际执行生产来源/声明一致性路径；这里没有真实 libmpv，所以必须拒绝。
$approvedPath = Join-Path $repo 'assets\mpv-runtime\approved-payload.json'
$approved = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($approvedPath))
$lib = @($approved.native | Where-Object { $_.path -ceq 'libmpv-2.dll' })
$vulkan = @($approved.native | Where-Object { $_.path -ceq 'vulkan-1.dll' })
$identityMatches = $approved.schemaVersion -eq 1 -and $approved.approvalId -ceq 'mpv-lgpl-2026-10-14-270367e8af' -and
    $lib.Count -eq 1 -and $vulkan.Count -eq 1 -and $lib[0].size -eq 101242880 -and
    $lib[0].sha256 -ceq 'fe583af31036bf3f3adacf6659e1bd448b568091b0334675573a3e303ec89d17' -and
    $lib[0].version -ceq 'mpv v0.41.0-1163-g270367e8a' -and $lib[0].release -ceq '2026-10-14-270367e8af' -and
    $lib[0].asset -ceq 'mpv-dev-lgpl-x86_64-20261014-git-270367e8af.7z' -and
    $lib[0].archiveSha256 -ceq '1488b4458279561a4a0134ddf837989dcc8045308ac521657a98a284bcd8529d' -and
    $vulkan[0].size -eq 581120 -and $vulkan[0].version -ceq '1.4.359.0' -and
    $vulkan[0].sha256 -ceq '13dd448a97495763b5cce84b8ade84cd2fa13c71f7e871f3c7d509eca845ad0f'
Save-Result 'real approved identity constants (not native execution)' $identityMatches 0 ([pscustomobject]@{
    exitCode=0; output="approved=$approvedPath; libmpv=101242880/fe583af...; vulkan=581120/13dd448a...; version/release/archive asserted; no native execution.`n";
    executable='in-process assertion'; arguments=@($approvedPath) })
Invoke-Check 'real manifest source/NOTICE consistency and synthetic native rejection' $false '发布载荷 libmpv-2.dll 字节大小与批准清单不一致' -Repository $repo

Invoke-Check 'complete synthetic approved payload' $true
Invoke-Check 'complete synthetic self-contained payload' $true -SelfContained
foreach ($entry in $approved.files | Where-Object { $_.path.StartsWith('mpv-ui/') }) {
    Invoke-FileMutation ('missing UI ' + $entry.path) (Join-Path $fixture.publish $entry.path.Replace('/', '\')) {
        param($target); Remove-Item -LiteralPath $target
    } $entry.path.Split('/')[-1]
}
foreach ($name in @('libmpv-2.dll', 'vulkan-1.dll', 'LICENSE', 'LICENSE.Apache-2.0.txt', 'NOTICE.txt', 'THIRD-PARTY-NOTICES.md')) {
    Invoke-FileMutation ('missing runtime ' + $name) (Join-Path $fixture.publish $name) {
        param($target); Remove-Item -LiteralPath $target
    } $name
}
foreach ($relative in @('mpv-ui/scripts/uosc/lib/std.lua', 'mpv-ui/scripts/uosc/lib/utils.lua',
    'mpv-ui/fonts/MaterialIconsRound-Regular.otf', 'mpv-ui/scripts/uosc/intl/zh-hans.json',
    'mpv-ui/scripts/stats.lua', 'mpv-ui/LICENSE-MaterialIcons.txt', 'libmpv-2.dll', 'vulkan-1.dll',
    'LICENSE.Apache-2.0.txt', 'NOTICE.txt', 'THIRD-PARTY-NOTICES.md')) {
    Invoke-FileMutation ('same-size corrupt ' + $relative) (Join-Path $fixture.publish $relative.Replace('/', '\')) {
        param($target); $bytes = [IO.File]::ReadAllBytes($target); $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($target, $bytes)
    } 'SHA256'
}
Invoke-FileMutation 'non-PE supplied native placeholder' (Join-Path $fixture.publish 'libmpv-2.dll') {
    param($target); [IO.File]::WriteAllText($target, 'not the approved DLL')
} 'libmpv-2.dll'
Invoke-FileMutation 'old/other native bytes with approved-looking MZ signature' (Join-Path $fixture.publish 'libmpv-2.dll') {
    param($target); $bytes = [IO.File]::ReadAllBytes($target); $bytes[0]=0x4D; $bytes[1]=0x5A; [IO.File]::WriteAllBytes($target, $bytes)
} 'SHA256'
Invoke-FileMutation 'unapproved supply in repository root cannot define approval' (Join-Path $fixture.repository 'libmpv-2.dll') {
    param($target); $bytes = [IO.File]::ReadAllBytes($target); $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($target, $bytes)
} '供应 libmpv-2.dll'
Invoke-FileMutation 'damaged repository module cannot refresh expected bytes' (Join-Path $fixture.repository 'assets\mpv-ui\scripts\uosc\lib\std.lua') {
    param($target); $bytes = [IO.File]::ReadAllBytes($target); $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($target, $bytes)
} 'std.lua'
Invoke-FileMutation 'missing repository module cannot shrink shipping list' (Join-Path $fixture.repository 'assets\mpv-ui\scripts\uosc\lib\std.lua') {
    param($target); Remove-Item -LiteralPath $target
} 'std.lua'

foreach ($relative in @('mpv-ui/extra.lua', 'mpv-ui/README.md', 'mpv-ui/scripts/uosc/lib/hidden.lua',
    'extra.dll', 'mpv.exe', 'libmpv-old.dll', 'lua51.dll', 'LICENSE.extra.txt', 'NOTICE.extra.txt')) {
    Invoke-FileMutation ('extra payload ' + $relative) (Join-Path $fixture.publish $relative.Replace('/', '\')) {
        param($target); [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        [IO.File]::WriteAllText($target, 'unapproved extra payload')
        if ($target.EndsWith('hidden.lua')) { [IO.File]::SetAttributes($target, [IO.FileAttributes]::Hidden) }
    } $relative.Split('/')[-1]
}
Invoke-FileMutation 'extra runtime source DLL' (Join-Path $fixture.repository 'assets\mpv-runtime\extra.dll') {
    param($target); [IO.File]::WriteAllText($target, 'unapproved source DLL')
} 'extra.dll'
Invoke-FileMutation 'extra UI source file' (Join-Path $fixture.repository 'assets\mpv-ui\extra.lua') {
    param($target); [IO.File]::WriteAllText($target, 'unapproved source UI')
} 'extra.lua'
Invoke-FileMutation 'dependencies do not grant approval to old Lua DLL' (Join-Path $fixture.publish 'Momoka.deps.json') {
    param($target); $deps=Get-Content -LiteralPath $target -Raw | ConvertFrom-Json
    $deps.targets.($deps.runtimeTarget.name).'R067.Offline.Native/1.0.0'.native | Add-Member -NotePropertyName 'lua51.dll' -NotePropertyValue ([pscustomobject]@{})
    [IO.File]::WriteAllText($target, ($deps | ConvertTo-Json -Depth 14))
    [IO.File]::WriteAllText((Join-Path $fixture.publish 'lua51.dll'), 'unapproved Lua')
} 'lua51.dll'
Remove-Item -LiteralPath (Assert-FixtureTarget (Join-Path $fixture.publish 'lua51.dll'))
$framework = Assert-FixtureTarget (Join-Path $fixture.publish 'Momoka.Core.dll')
try { [IO.File]::WriteAllText($framework, 'registered dependency fixture'); Invoke-Check 'registered framework dependency remains compatible' $true }
finally { Remove-Item -LiteralPath $framework }

Invoke-ManifestMutation 'empty UI manifest array' { param($m); $m.files=@() } '非空数组'
Invoke-ManifestMutation 'empty native manifest array' { param($m); $m.native=@() } '非空数组'
Invoke-ManifestMutation 'empty evidence manifest array' { param($m); $m.evidence=@() } '非空数组'
Invoke-ManifestMutation 'manifest omission cannot hide required std' { param($m); $m.files=@($m.files | Where-Object { $_.path -ne 'mpv-ui/scripts/uosc/lib/std.lua' }) } 'std.lua'
Invoke-ManifestMutation 'manifest omission cannot hide native' { param($m); $m.native=@($m.native | Where-Object { $_.path -ne 'vulkan-1.dll' }) } '完整包含'
Invoke-ManifestMutation 'duplicate path' { param($m); $m.files += $m.files[0] } '重复载荷路径'
Invoke-ManifestMutation 'case-insensitive duplicate path' { param($m); $copy=$m.files[0] | ConvertTo-Json | ConvertFrom-Json; $copy.path=$copy.path.ToUpperInvariant(); $m.files += $copy } '重复载荷路径'
Invoke-ManifestMutation 'duplicate source' { param($m); $m.files[0].source=$m.files[1].source } '重复来源路径'
foreach ($invalid in @('', '../outside.lua', 'mpv-ui/../outside.lua', 'C:/outside.lua', '/outside.lua',
    '//server/share.lua', 'mpv-ui\scripts\escape.lua', 'mpv-ui//double.lua', 'mpv-ui/end.', 'mpv-ui/NUL.lua', 'mpv-ui/std.lua:ads')) {
    $bad = $invalid
    Invoke-ManifestMutation ('invalid manifest path ' + $invalid) { param($m); $m.files[0].path=$bad } '非法清单路径'
}
Invoke-ManifestMutation 'source traversal' { param($m); $m.files[0].source='../outside' } '非法清单路径'
Invoke-ManifestMutation 'zero approved size' { param($m); $m.files[0].size=0 } 'size'
Invoke-ManifestMutation 'fractional approved size' { param($m); $m.files[0].size=1.5 } 'size'
Invoke-ManifestMutation 'string approved size' { param($m); $m.files[0].size='70' } 'size'
Invoke-ManifestMutation 'empty SHA256' { param($m); $m.files[0].sha256='' } 'sha256'
Invoke-ManifestMutation 'unknown schema version' { param($m); $m.schemaVersion=2 } 'schemaVersion'
Invoke-ManifestMutation 'unknown bypass field' { param($m); $m | Add-Member -NotePropertyName SkipNativeCheck -NotePropertyValue $true } '未知字段'
Invoke-ManifestMutation 'wrong version approval cannot ignore NOTICE' { param($m); $m.native[0].version='mpv v99.0.0-unapproved' } '声明不一致'
Invoke-ManifestMutation 'wrong native size approval cannot ignore NOTICE' { param($m); $m.native[0].size=71 } '字节大小'
Invoke-ManifestMutation 'wrong native hash approval cannot ignore NOTICE' { param($m); $m.native[0].sha256=('0' * 64) } 'SHA256'
Invoke-ManifestMutation 'wrong vulkan hash approval cannot ignore NOTICE' { param($m); $m.native[1].sha256=('0' * 64) } 'vulkan-1.dll'
Invoke-ManifestMutation 'wrong vulkan size approval' { param($m); $m.native[1].size=71 } 'vulkan-1.dll'
Invoke-ManifestMutation 'wrong license binding' { param($m); $m.native[0].licenses=@('LICENSE.Apache-2.0.txt') } '许可索引'
Invoke-ManifestMutation 'wrong release approval index' { param($m); $m.native[0].release='unapproved-release' } '批准来源'
Invoke-ManifestMutation 'wrong native source path' { param($m); $m.native[0].source='arbitrary.dll' } 'shipping inclusion'
Invoke-ManifestMutation 'evidence omission' { param($m); $m.evidence[0].source='README.md' } '批准证据'
foreach ($raw in @('', '{}', '{"schemaVersion":1,"schemaVersion":1}', '{"path":"one","p\u0061th":"two"}', '{"files":[],}')) {
    $value=$raw
    Invoke-FileMutation ('raw malformed/duplicate JSON ' + $raw) $fixture.manifest {
        param($target); [IO.File]::WriteAllText($target, $value)
    } '清单'
}
Invoke-FileMutation 'NOTICE evidence drift' (Join-Path $fixture.repository 'assets\mpv-runtime\NOTICE.txt') {
    param($target); [IO.File]::AppendAllText($target, 'drift')
} 'NOTICE.txt'
Invoke-FileMutation 'README evidence drift' (Join-Path $fixture.repository 'assets\mpv-runtime\README.md') {
    param($target); [IO.File]::AppendAllText($target, 'drift')
} 'README.md'
$noticePath = Assert-FixtureTarget (Join-Path $fixture.repository 'assets\mpv-runtime\NOTICE.txt')
$noticeBytes = [IO.File]::ReadAllBytes($noticePath)
try {
    [IO.File]::WriteAllText($noticePath, [IO.File]::ReadAllText($noticePath).Replace('mpv v0.0.0-r067-fixture', 'mpv v99.0.0-unapproved'), $encoding)
    Invoke-ManifestMutation 'updated NOTICE byte hash still rejects conflicting version evidence' {
        param($m); $entry=$m.files | Where-Object { $_.path -eq 'NOTICE.txt' }; $entry.size=(Get-Item -LiteralPath $noticePath).Length;
        $entry.sha256=(Get-FileHash -LiteralPath $noticePath -Algorithm SHA256).Hash.ToLowerInvariant()
    } '声明不一致'
} finally { [IO.File]::WriteAllBytes($noticePath, $noticeBytes) }
Invoke-FileMutation 'Shell shipping exclusion drift' (Join-Path $fixture.repository 'src\Momoka.Shell\Momoka.Shell.csproj') {
    param($target); $text=[IO.File]::ReadAllText($target); [IO.File]::WriteAllText($target, $text.Replace('Exclude="..\..\assets\mpv-ui\**\*.md"', 'Exclude=""'))
} 'shipping inclusion'
# 重解析目标完全留在夹具内。只删单个 junction/link 本身，不递归、不跟随、不清理整轮证据。
$link = Assert-FixtureTarget (Join-Path $fixture.publish 'mpv-ui\fixture-link')
try {
    New-Item -ItemType Junction -Path $link -Target (Join-Path $fixture.repository 'assets\mpv-ui') | Out-Null
    Invoke-Check 'reparse UI directory cannot escape payload inventory' $false '重解析'
} finally {
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete((Assert-FixtureTarget $link)) }
}
foreach ($name in @('empty', 'duplicate-case', 'duplicate-escaped')) {
    $value = switch ($name) {
        'empty' { '{"schemaVersion":1,"approvalId":"","native":[],"files":[],"evidence":[]}' }
        'duplicate-case' { '{"schemaVersion":1,"SchemaVersion":1}' }
        'duplicate-escaped' { '{"path":"one","p\u0061th":"two"}' }
    }
    Invoke-FileMutation ('strict manifest JSON ' + $name) $fixture.manifest {
        param($target); [IO.File]::WriteAllText($target, $value)
    } '清单'
}
Invoke-FileMutation 'invalid UTF8 manifest' $fixture.manifest {
    param($target); [IO.File]::WriteAllBytes($target, [byte[]]@(0xFF, 0xFE, 0xFF))
} 'JSON 无效'
Invoke-ManifestMutation 'boolean approved size' { param($m); $m.files[0].size=$true } 'size'
Invoke-ManifestMutation 'null approved array element' { param($m); $m.files += $null } '非法清单路径'
Invoke-ManifestMutation 'empty approved version' { param($m); $m.native[0].version='' } '空版本'
Invoke-FileMutation 'unlisted framework DLL rejects in framework-dependent mode' (Join-Path $fixture.publish 'Microsoft.UI.Xaml.Controls.dll') {
    param($target); [IO.File]::WriteAllText($target, 'framework dependent mode should not carry self-contained-only DLL')
} 'Microsoft.UI.Xaml.Controls.dll'
$framework = Assert-FixtureTarget (Join-Path $fixture.publish 'Microsoft.UI.Xaml.Controls.dll')
try {
    [IO.File]::WriteAllText($framework, 'fixed SDK target copied DLL fixture')
    Invoke-Check 'fixed SDK self-contained framework DLL remains compatible' $true -SelfContained
} finally { Remove-Item -LiteralPath $framework }
$webview = Assert-FixtureTarget (Join-Path $fixture.publish 'Microsoft.Web.WebView2.Core.dll')
$depsPath = Assert-FixtureTarget (Join-Path $fixture.publish 'Momoka.deps.json')
$depsBytes = [IO.File]::ReadAllBytes($depsPath)
try {
    [IO.File]::WriteAllText($webview, 'fixed WebView2 target copied DLL fixture')
    # WebView2 许可只来自隔离固定包/实际项目引用；这里不篡改待验 deps 建立授权。
    Invoke-Check 'selected WebView2 native content remains compatible' $true
} finally {
    Remove-Item -LiteralPath $webview
    [IO.File]::WriteAllBytes($depsPath, $depsBytes)
}
Invoke-ManifestMutation 'abbreviated libmpv version cannot match declaration prefix' { param($m); $m.native[0].version='mpv' } '声明不一致'
Invoke-ManifestMutation 'abbreviated vulkan version cannot match declaration prefix' { param($m); $m.native[1].version='0.0' } '声明不一致'
Invoke-ManifestMutation 'abbreviated release cannot match declaration substring' { param($m); $m.native[0].release='r067'; $m.approvalId='mpv-lgpl-r067' } '声明不一致'
Invoke-ManifestMutation 'abbreviated release asset cannot match declaration substring' { param($m); $m.native[0].asset='r067' } '声明不一致'
$noticeBytes = [IO.File]::ReadAllBytes($noticePath)
try {
    $notice = [IO.File]::ReadAllText($noticePath).Replace('LICENSE.Apache-2.0.txt   vulkan-1.dll', 'LICENSE.Apache-2.0.txt   libmpv-2.dll')
    [IO.File]::WriteAllText($noticePath, $notice, $encoding)
    Invoke-ManifestMutation 'updated NOTICE byte hash cannot move Apache ownership to libmpv' {
        param($m); $entry=$m.files | Where-Object { $_.path -eq 'NOTICE.txt' }; $entry.size=(Get-Item -LiteralPath $noticePath).Length
        $entry.sha256=(Get-FileHash -LiteralPath $noticePath -Algorithm SHA256).Hash.ToLowerInvariant()
    } '归属索引'
} finally { [IO.File]::WriteAllBytes($noticePath, $noticeBytes) }
Invoke-Check 'complete fixture restored after all mutations' $true

$failed = @($results | Where-Object { -not $_.passed }).Count
$summary = [ordered]@{ phase=$Phase; localDate=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K'); timezone='Asia/Shanghai';
    total=$results.Count; passed=($results.Count-$failed); failed=$failed; skipped=0; warnings=0;
    syntheticManifest=$fixture.manifest; realApprovedManifest=$approvedPath; realNativeGatePassed=$false;
    boundaries='Production verify-publish/verify-mpv-payload executed. Complete synthetic approval only; no native load, media, config, account, publish, shortcut or network.';
    cases=$results.ToArray() }
[IO.File]::WriteAllText((Join-Path $run 'results.json'), ($summary | ConvertTo-Json -Depth 12).Replace("`r`n", "`n") + "`n", $encoding)
Write-Output ("mpv payload checks: {0} total, {1} failed, 0 skipped; {2}" -f $results.Count, $failed, $run)
if ($failed -gt 0) { exit 1 }
exit 0
