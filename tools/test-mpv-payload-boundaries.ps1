[CmdletBinding()]
param([string]$Phase='followup-green')
$ErrorActionPreference='Stop'
if ($Phase -cnotmatch '^[a-z0-9-]+$') { throw '非法 Phase。' }
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
$repo=(Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
Assert-PlainPath $repo
$run=Get-ChildPath $repo ('artifacts/r067-approved-payload/'+$Phase+'-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
[IO.Directory]::CreateDirectory($run) | Out-Null
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
$fixture=New-MpvPayloadFixture $repo (Join-Path $run 'safe-seed')
$check=Join-Path $PSScriptRoot 'verify-publish.ps1'
$results=[Collections.Generic.List[object]]::new()
$encoding=[Text.UTF8Encoding]::new($false)
$sentinel=Join-Path $fixture.fixture 'outside-fixture-sentinel'
[IO.File]::WriteAllText($sentinel,'R067 isolated immutable sentinel',$encoding)
$license=Join-Path $fixture.repository 'LICENSE'

function Record([string]$Name,[bool]$Passed,[int]$Expected,$Result,$Details=$null) {
    $log=Join-Path $run ('case-'+($results.Count+1).ToString('D3')+'.log')
    [IO.File]::WriteAllText($log,$Result.output.Replace("`r`n","`n")+"`n",$encoding)
    $results.Add([pscustomobject]@{name=$Name;passed=$Passed;expectedExit=$Expected;exitCode=$Result.exitCode;log=$log;
        arguments=$Result.arguments;details=$Details;nativeLoaded=$false;realPublish=$false;output=$Result.output})
    [IO.File]::WriteAllText((Join-Path $run 'case-results.json'),($results.ToArray()|ConvertTo-Json -Depth 12)+"`n",$encoding)
    Write-Output ("{0}: {1}; expected={2}, actual={3}" -f $Name,$Passed,$Expected,$Result.exitCode)
}
function Snapshot([string]$Root) {
    $rows=[Collections.Generic.List[string]]::new()
    function Visit([string]$Directory) {
        foreach ($entry in @(Get-ChildItem -LiteralPath $Directory -Force)) {
            $relative=$entry.FullName.Substring($Root.Length+1)
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $rows.Add('LINK '+$relative); continue }
            if ($entry.PSIsContainer) { $rows.Add('DIR '+$relative); Visit $entry.FullName }
            else { $rows.Add('FILE '+$relative+' '+$entry.Length+' '+(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash) }
        }
    }
    Visit $Root
    return ($rows|Sort-Object) -join "`n"
}
function Check([string]$Name,[bool]$Success,[string]$Needle='') {
    $result=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish
    $expected=if($Success){0}else{1}
    Record $Name ($result.exitCode-eq $expected -and ($Needle-eq '' -or $result.output.Contains($Needle))) $expected $result
}
function Manifest-Case([string]$Name,[scriptblock]$Mutation,[switch]$FixturePreflight,[string]$Needle='') {
    $saved=[IO.File]::ReadAllBytes($fixture.manifest)
    $manifest=Get-Content -LiteralPath $fixture.manifest -Raw|ConvertFrom-Json
    try {
        & $Mutation $manifest
        [IO.File]::WriteAllText($fixture.manifest,($manifest|ConvertTo-Json -Depth 16)+"`n",$encoding)
        [IO.File]::WriteAllText((Join-Path $run ('manifest-input-'+($results.Count+1).ToString('D3')+'.json')),($manifest|ConvertTo-Json -Depth 16)+"`n",$encoding)
        if (-not $FixturePreflight) { Check $Name $false $Needle; return }
        $destination=Join-Path $fixture.repository ('artifacts\preflight\'+($results.Count+1))
        $before=Snapshot $fixture.fixture
        $sourceBytes=[IO.File]::ReadAllBytes($license)
        $sentinelBytes=[IO.File]::ReadAllBytes($sentinel)
        $result=[pscustomobject]@{exitCode=0;output='';arguments=@('New-MpvPayloadFixture',$fixture.repository,$destination)}
        try { $null=New-MpvPayloadFixture $fixture.repository $destination }
        catch { $result.exitCode=1; $result.output=$_.Exception.ToString() }
        $unchanged=($before-ceq(Snapshot $fixture.fixture))
        $details=@{scratch=$fixture.fixture;fixtureRoot=$destination;allScratchBytesAndDirectoriesUnchanged=$unchanged;
            sourceLicenseUnchanged=([Convert]::ToBase64String($sourceBytes)-ceq[Convert]::ToBase64String([IO.File]::ReadAllBytes($license)));
            outsideSentinelUnchanged=([Convert]::ToBase64String($sentinelBytes)-ceq[Convert]::ToBase64String([IO.File]::ReadAllBytes($sentinel)));
            fixtureCreated=(Test-Path -LiteralPath $destination);zeroOutsideWrites=$unchanged}
        Record $Name ($result.exitCode-eq 1 -and $unchanged -and -not $details.fixtureCreated -and
            ($Needle-eq '' -or $result.output.Contains($Needle))) 1 $result $details
    } finally { [IO.File]::WriteAllBytes($fixture.manifest,$saved) }
}
Check 'complete synthetic trusted payload baseline' $true
$selfContainedResult=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish -SelfContained
Record 'complete synthetic trusted self-contained baseline' ($selfContainedResult.exitCode-eq 0) 0 $selfContainedResult
# P1: source repo 自身也完全在额外 scratch 内。固定5层布局只读解析曾逃逸到这个隔离LICENSE。
$hypothetical=Join-Path $fixture.repository 'artifacts\level1\level2\fixture\publish'
$escape=[IO.Path]::GetFullPath((Join-Path $hypothetical '../../../../../LICENSE'))
Record 'P1 old traversal pure calculation stays only isolated scratch' ($escape-ceq $license) 0 ([pscustomobject]@{exitCode=0;output=$escape;arguments=@('pure-path-calculation')}) @{escapedToIsolatedLicense=$true;noWrite=$true}
Manifest-Case 'P1 THIRD-PARTY target traversal early rejects before any write' {
    param($m); ($m.files|Where-Object{$_.path-eq 'THIRD-PARTY-NOTICES.md'}).path='../../../../../LICENSE'
} -FixturePreflight -Needle '非法清单路径'
Manifest-Case 'P1 last record source traversal early rejects before any write' {
    param($m); $m.files[-1].source='../../../../../LICENSE'
} -FixturePreflight -Needle '非法清单路径'
foreach ($invalid in @('', '/outside.dll', 'C:/outside.dll', '//host/share.dll', 'mpv-ui/../LICENSE',
    'mpv-ui\alias.lua','mpv-ui//alias.lua','mpv-ui/CON.txt','mpv-ui/end.','mpv-ui/std.lua:ads')) {
    $bad=$invalid
    Manifest-Case ('P1 target illegal alias '+$invalid) {param($m);$m.files[-1].path=$bad} -FixturePreflight -Needle '非法清单路径'
}
Manifest-Case 'P1 duplicate target early rejects' {param($m);$m.files+=$m.files[-1]} -FixturePreflight -Needle '重复载荷路径'
Manifest-Case 'P1 case alias target early rejects' {param($m);$copy=$m.files[-1]|ConvertTo-Json|ConvertFrom-Json;$copy.path=$copy.path.ToUpperInvariant();$m.files+=$copy} -FixturePreflight -Needle '重复载荷路径'
Manifest-Case 'P1 duplicate source early rejects' {param($m);$m.files[-1].source=$m.files[0].source} -FixturePreflight -Needle '重复来源路径'
Manifest-Case 'P1 Windows-reserved source early rejects' {param($m);$m.files[-1].source='assets/mpv-ui/NUL.txt'} -FixturePreflight -Needle '非法清单路径'
Manifest-Case 'P1 missing late source early rejects' {param($m);$m.files[-1].source='assets/mpv-ui/missing.lua'} -FixturePreflight
$link=Join-Path $fixture.repository 'assets\mpv-ui\fixture-link'
try {
    New-Item -ItemType Junction -Path $link -Target $fixture.fixture|Out-Null
    Manifest-Case 'P1 source reparse rejects before writes' {param($m)} -FixturePreflight -Needle '重解析'
} finally { if(Test-Path -LiteralPath $link){[IO.Directory]::Delete($link)} }
$targetLink=Join-Path $fixture.repository 'artifacts\preflight'
$redirect=Join-Path $fixture.fixture 'target-junction-sentinel'
[IO.Directory]::CreateDirectory($redirect)|Out-Null
try {
    New-Item -ItemType Junction -Path $targetLink -Target $redirect|Out-Null
    Manifest-Case 'P1 output parent reparse early rejects with sentinel unchanged' {param($m)} -FixturePreflight -Needle '重解析'
} finally {if(Test-Path -LiteralPath $targetLink){[IO.Directory]::Delete($targetLink)}}
# P3: 全类型矩阵；同一有效字符串另实际正例。集合绝不能靠 -cne 空结果躲过索引检查。
foreach($variant in @('empty-array','valid-array','multi-array','null','bool','object','number','empty','whitespace')){
    $type=$variant
    $mutation={param($m);$m.approvalId=switch($type){
        'empty-array' {,@()}; 'valid-array' {,@($m.approvalId)}; 'multi-array' {,@($m.approvalId,$m.approvalId)};
        'null' {$null}; 'bool' {$true}; 'object' {[pscustomobject]@{value=$m.approvalId}}; 'number' {67};
        'empty' {''}; 'whitespace' {'  '}
    }}
    Manifest-Case ('P3 approvalId '+$variant) $mutation -Needle 'approvalId'
}
Check 'P3 valid scalar approvalId stays accepted' $true
# P2: 篡改待验deps自授权不得扩大可信restore/编译清单，不限于一个extra文件名。
$depsPath=Join-Path $fixture.publish 'Momoka.deps.json'
$depsBytes=[IO.File]::ReadAllBytes($depsPath)
foreach($variant in @('active-native','active-runtime','active-runtimeTargets','foreign-target','foreign-rid','unknown-library','fake-webview')){
    $extra=Join-Path $fixture.publish 'renamed-old-native.dll'
    try {
        [IO.File]::WriteAllText($extra,'unapproved renamed old native fixture',$encoding)
        $deps=[IO.File]::ReadAllText($depsPath)|ConvertFrom-Json
        $active=$deps.targets.($deps.runtimeTarget.name)
        switch($variant){
            'active-native' {$active.'R067.Offline.Native/1.0.0'.native|Add-Member -NotePropertyName 'renamed-old-native.dll' -NotePropertyValue ([pscustomobject]@{})}
            'active-runtime' {$active.'Momoka/0.1.7'.runtime|Add-Member -NotePropertyName 'renamed-old-native.dll' -NotePropertyValue ([pscustomobject]@{})}
            'active-runtimeTargets' {$active.'R067.Offline.Native/1.0.0'|Add-Member -NotePropertyName runtimeTargets -NotePropertyValue ([pscustomobject]@{'runtimes/win-x64/native/renamed-old-native.dll'=[pscustomobject]@{rid='win-x64';assetType='native'}})}
            'foreign-target' {$deps.targets|Add-Member -NotePropertyName 'net0.0/win-arm64' -NotePropertyValue ([pscustomobject]@{'forged/1'=[pscustomobject]@{native=[pscustomobject]@{'renamed-old-native.dll'=[pscustomobject]@{}}}})}
            'foreign-rid' {$deps.runtimeTarget.name='.NETCoreApp,Version=v10.0/win-arm64'}
            'unknown-library' {$active|Add-Member -NotePropertyName 'forged/1.0' -NotePropertyValue ([pscustomobject]@{native=[pscustomobject]@{'renamed-old-native.dll'=[pscustomobject]@{}}})}
            'fake-webview' {$deps.libraries|Add-Member -NotePropertyName 'Microsoft.Web.WebView2/99.0' -NotePropertyValue ([pscustomobject]@{})}
        }
        [IO.File]::WriteAllText($depsPath,($deps|ConvertTo-Json -Depth 16),$encoding)
        Check ('P2 deps cannot self-authorize '+$variant) $false
    } finally {Remove-Item -LiteralPath $extra;[IO.File]::WriteAllBytes($depsPath,$depsBytes)}
}
foreach($variant in @('foreign-rid-runtimeTargets','unselected-empty-target','unselected-trusted-asset','missing-target-object','unknown-target-same-name')){
    try{
        $deps=Get-Content -LiteralPath $depsPath -Raw|ConvertFrom-Json
        $active=$deps.targets.($deps.runtimeTarget.name)
        switch($variant){
            'foreign-rid-runtimeTargets'{$active.'R067.Offline.Native/1.0.0'|Add-Member -NotePropertyName runtimeTargets -NotePropertyValue ([pscustomobject]@{'runtimes/win-x64/native/hostfxr.dll'=[pscustomobject]@{rid='win-arm64';assetType='native'}})}
            'unselected-empty-target'{$deps.targets|Add-Member -NotePropertyName '.NETCoreApp,Version=v99.0/win-x64' -NotePropertyValue ([pscustomobject]@{})}
            'unselected-trusted-asset'{$deps.targets|Add-Member -NotePropertyName '.NETCoreApp,Version=v99.0/win-x64' -NotePropertyValue ([pscustomobject]@{'R067.Offline.Native/1.0.0'=[pscustomobject]@{native=[pscustomobject]@{'runtimes/win-x64/native/hostfxr.dll'=[pscustomobject]@{}}}})}
            'missing-target-object'{$deps.targets=$null}
            'unknown-target-same-name'{$deps.runtimeTarget.name='.NETCoreApp,Version=v99.0/win-x64'}
        }
        [IO.File]::WriteAllText($depsPath,($deps|ConvertTo-Json -Depth 16),$encoding)
        [IO.File]::WriteAllText((Join-Path $run ('deps-input-'+($results.Count+1).ToString('D3')+'.json')),($deps|ConvertTo-Json -Depth 16),$encoding)
        Check ('P2 target/RID constraint '+$variant) $false
    } finally{[IO.File]::WriteAllBytes($depsPath,$depsBytes)}
}
$extra=Join-Path $fixture.publish 'extra.dll'
try {
    [IO.File]::WriteAllText($extra,'unapproved extra',$encoding)
    $deps=Get-Content -LiteralPath $depsPath -Raw|ConvertFrom-Json
    $deps.targets.($deps.runtimeTarget.name).'R067.Offline.Native/1.0.0'.native|Add-Member -NotePropertyName extra.dll -NotePropertyValue ([pscustomobject]@{})
    [IO.File]::WriteAllText($depsPath,($deps|ConvertTo-Json -Depth 16),$encoding)
    Check 'P2 extra.dll plus active deps injection still rejects' $false 'extra.dll'
} finally {Remove-Item -LiteralPath $extra;[IO.File]::WriteAllBytes($depsPath,$depsBytes)}
# 相同可信名称也绑定包/当前编译字节，旧native改名成可信名仍拒绝。
$bootstrap=Join-Path $fixture.publish 'Microsoft.WindowsAppRuntime.Bootstrap.dll'
$bytes=[IO.File]::ReadAllBytes($bootstrap)
try {[IO.File]::WriteAllText($bootstrap,'unapproved native renamed to trusted name',$encoding);Check 'P2 trusted native basename with wrong bytes rejects' $false '字节'}
finally {[IO.File]::WriteAllBytes($bootstrap,$bytes)}
$package=Join-Path $fixture.repository 'artifacts\r067-fixed-dependency-packages\microsoft.windowsappsdk.foundation\2.3.12\runtimes\win-x64\native\Microsoft.WindowsAppRuntime.Bootstrap.dll'
$bytes=[IO.File]::ReadAllBytes($package)
try {[IO.File]::WriteAllText($package,'corrupted restored package asset',$encoding);Check 'P2 changed trusted package extraction rejects against fixed archive' $false '固定归档'}
finally {[IO.File]::WriteAllBytes($package,$bytes)}
$missing=Join-Path $fixture.repository 'src\Momoka.Shell\obj\project.assets.json'
$bytes=[IO.File]::ReadAllBytes($missing)
try {Remove-Item -LiteralPath $missing;Check 'P2 missing trusted restore cannot fall back to publish deps' $false '可信 restore'}
finally {[IO.File]::WriteAllBytes($missing,$bytes)}
$core=Join-Path $fixture.publish 'Momoka.Core.dll'
try {Copy-Item -LiteralPath (Join-Path $fixture.repository 'src\Momoka.Core\bin\Release\net10.0\Momoka.Core.dll') -Destination $core;Check 'P2 normal current trusted project output accepts' $true}
finally {Remove-Item -LiteralPath $core}
Check 'complete synthetic trusted payload restored' $true
$failed=@($results|Where-Object{-not $_.passed}).Count
$summary=@{phase=$Phase;date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K');timezone='Asia/Shanghai';total=$results.Count;
    passed=($results.Count-$failed);failed=$failed;skipped=0;cases=$results.ToArray();syntheticTrust='Fixed isolated packages/restore/project output, not production NuGet or native approval';realNativeGatePassed=$false}
[IO.File]::WriteAllText((Join-Path $run 'results.json'),($summary|ConvertTo-Json -Depth 14)+"`n",$encoding)
Write-Output "R067 boundary checks: $($results.Count) total, $failed failed, 0 skipped; $run"
if($failed-gt 0){exit 1}
exit 0
