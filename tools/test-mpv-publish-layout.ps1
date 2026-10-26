[CmdletBinding()]
param([string]$Phase='release-layout-green')
$ErrorActionPreference='Stop'
if ($Phase -cnotmatch '^[a-z0-9-]+$') { throw '非法 Phase。' }
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
$repo=(Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
Assert-PlainPath $repo
$run=Get-ChildPath $repo ('artifacts/r067-approved-payload/'+$Phase+'-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
[IO.Directory]::CreateDirectory($run)|Out-Null
$fixture=New-MpvPayloadFixture $repo (Get-ChildPath $run 'isolated-synthetic')
$check=Join-Path $PSScriptRoot 'verify-publish.ps1'
$encoding=[Text.UTF8Encoding]::new($false)
$results=[Collections.Generic.List[object]]::new()
function Save([string]$Name,[bool]$Passed,[int]$Expected,$Result,$Details=$null) {
    $log=Get-ChildPath $run ('case-'+($results.Count+1).ToString('D3')+'.log')
    [IO.File]::WriteAllText($log,$Result.output.Replace("`r`n","`n")+"`n",$encoding)
    $results.Add([pscustomobject]@{name=$Name;passed=$Passed;expectedExit=$Expected;exitCode=$Result.exitCode;
        arguments=$Result.arguments;details=$Details;log=$log;output=$Result.output;nativeLoaded=$false;networkUsed=$false})
    [IO.File]::WriteAllText((Get-ChildPath $run 'case-results.json'),($results.ToArray()|ConvertTo-Json -Depth 14)+"`n",$encoding)
    Write-Output "$Name : $Passed expected=$Expected actual=$($Result.exitCode)"
}
function Check([string]$Name,[bool]$Success,[string]$Needle='', [switch]$SelfContained) {
    $result=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish -SelfContained:$SelfContained
    $expected=if($Success){0}else{1}
    Save $Name ($result.exitCode-eq $expected -and ($Needle-eq '' -or $result.output.Contains($Needle))) $expected $result
}
function Mutate([string]$Name,[string]$Path,[scriptblock]$Action,[string]$Needle='') {
    Assert-PlainPath $Path
    if (-not $Path.StartsWith($fixture.fixture+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '变异目标越界。' }
    $bytes=if(Test-Path -LiteralPath $Path -PathType Leaf){[IO.File]::ReadAllBytes($Path)}else{$null}
    try { & $Action $Path; Check $Name $false $Needle }
    finally {
        if($null-ne $bytes){[IO.File]::WriteAllBytes($Path,$bytes)}
        elseif(Test-Path -LiteralPath $Path){Remove-Item -LiteralPath $Path -Force}
    }
}
function Snapshot([string]$Root) {
    return (@(Get-ChildItem -LiteralPath $Root -Recurse -Force|Sort-Object FullName|ForEach-Object{
        if(($_.Attributes-band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'sentinel目录不应有重解析路径'}
        if($_.PSIsContainer){'DIR '+$_.FullName}else{'FILE '+$_.FullName+' '+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })-join "`n")
}
Check 'complete fixed-source synthetic payload' $true
$assetsPath=Get-ChildPath $fixture.repository 'src/Momoka.Shell/obj/project.assets.json'
$assetsBytes=[IO.File]::ReadAllBytes($assetsPath)
$assets=Get-Content -LiteralPath $assetsPath -Raw|ConvertFrom-Json
$packages=@($assets.packageFolders.PSObject.Properties.Name)[0].TrimEnd('\')
$metadata=@(Get-PlainFiles $packages -Recurse|Where-Object{$_.Name-ceq '.nupkg.metadata'})
$metadataBytes=@{}
foreach($file in $metadata){$metadataBytes[$file.FullName]=[IO.File]::ReadAllBytes($file.FullName)}
# 模拟正常默认 restore 的 global-user cache + nuget.org URL，全部真实写入仍留在额外 scratch。
try {
    foreach($file in $metadata){$m=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json;$m.source='https://api.nuget.org/v3/index.json';[IO.File]::WriteAllText($file.FullName,($m|ConvertTo-Json),$encoding)}
    Check 'normal NuGet URL source is metadata not local path' $true
    $external=Get-ChildPath $fixture.fixture 'default-user-cache/packages'
    foreach($file in @(Get-PlainFiles $packages -Recurse)){
        $target=Get-ChildPath $external $file.FullName.Substring($packages.Length+1).Replace('\','/')
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $assets.packageFolders=[pscustomobject]@{($external+'\')=[pscustomobject]@{}}
    [IO.File]::WriteAllText($assetsPath,($assets|ConvertTo-Json -Depth 18),$encoding)
    [IO.File]::WriteAllText((Get-ChildPath $run 'default-style-restore-input.json'),($assets|ConvertTo-Json -Depth 18),$encoding)
    Check 'normal external cache plus NuGet URL with explicit repository' $true
    Check 'normal external cache plus NuGet URL self-contained mode' $true -SelfContained
    $tools=Get-ChildPath $fixture.repository 'tools'
    [IO.Directory]::CreateDirectory($tools)|Out-Null
    foreach($name in @('verify-publish.ps1','verify-mpv-payload.ps1','mpv-payload-common.ps1','mpv-payload-dependencies.ps1',
        'mpv-payload-fixture.ps1','mpv-nuget-content-hash.ps1','test-mpv-payload.ps1','test-mpv-payload-boundaries.ps1')){
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Get-ChildPath $tools $name)
    }
    # 普通 -File 调用省略 RepositoryRoot，实际执行生产 verifier 的默认根定位，不发布/不启动产品。
    $saved=$ErrorActionPreference;$ErrorActionPreference='Continue'
    try{$lines=@(& powershell -NoProfile -ExecutionPolicy Bypass -File (Get-ChildPath $tools 'verify-publish.ps1') -PublishRoot $fixture.publish -SelfContained 2>&1);$code=$LASTEXITCODE}
    finally{$ErrorActionPreference=$saved}
    Save 'normal default verifier invocation omitted RepositoryRoot' ($code-eq 0) 0 ([pscustomobject]@{exitCode=$code;output=($lines|Out-String);arguments=@('-File',(Get-ChildPath $tools 'verify-publish.ps1'),'-PublishRoot',$fixture.publish,'-SelfContained')}) @{externalSyntheticCache=$external;urlNeverContacted=$true;noRealDefaultRestoreRun=$true}
    Mutate 'normal source still binds trusted native DLL bytes' (Get-ChildPath $fixture.publish 'Microsoft.WindowsAppRuntime.Bootstrap.dll') {
        param($target);$b=[IO.File]::ReadAllBytes($target);$b[0]=$b[0]-bxor 1;[IO.File]::WriteAllBytes($target,$b)
    } '字节'
    $restored=Get-ChildPath $external 'microsoft.windowsappsdk.foundation/2.3.12/runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll'
    Mutate 'normal source still compares extraction to pinned ZIP entry' $restored {
        param($target);$b=[IO.File]::ReadAllBytes($target);$b[0]=$b[0]-bxor 1;[IO.File]::WriteAllBytes($target,$b)
    } '固定归档'
    $archive=Get-ChildPath $external 'microsoft.windowsappsdk.foundation/2.3.12/microsoft.windowsappsdk.foundation.2.3.12.nupkg'
    Mutate 'normal source missing local archive fails offline without fallback' $archive {param($target);Remove-Item -LiteralPath $target} '归档缺失'
    Mutate 'normal source corrupt archive rejected by SHA512' $archive {
        param($target);$b=[IO.File]::ReadAllBytes($target);$b[0]=$b[0]-bxor 1;[IO.File]::WriteAllBytes($target,$b)
    } '归档'
    $nativeMetadata=Get-ChildPath $external 'microsoft.windowsappsdk.foundation/2.3.12/.nupkg.metadata'
    Mutate 'normal source changed contentHash rejected against pinned restore' $nativeMetadata {
        param($target);$m=Get-Content -LiteralPath $target -Raw|ConvertFrom-Json;$m.contentHash='invalid';[IO.File]::WriteAllText($target,($m|ConvertTo-Json))
    } 'restore'
    $extra=Get-ChildPath $fixture.publish 'normal-deps-injected.dll'
    try {
        [IO.File]::WriteAllText($extra,'unapproved normal-mode extra',$encoding)
        Mutate 'normal source publish deps cannot grant DLL trust' (Get-ChildPath $fixture.publish 'Momoka.deps.json') {
            param($target);$d=Get-Content -LiteralPath $target -Raw|ConvertFrom-Json
            $d.targets.($d.runtimeTarget.name).'R067.Offline.Native/1.0.0'.native|Add-Member -NotePropertyName 'normal-deps-injected.dll' -NotePropertyValue ([pscustomobject]@{})
            [IO.File]::WriteAllText($target,($d|ConvertTo-Json -Depth 16))
        } '未批准'
    } finally {Remove-Item -LiteralPath $extra}
} finally {
    [IO.File]::WriteAllBytes($assetsPath,$assetsBytes)
    foreach($file in $metadata){[IO.File]::WriteAllBytes($file.FullName,$metadataBytes[$file.FullName])}
}
foreach($relative in @('shaders/libmpv-old.dll','shaders/extra.dll','shaders/libmpv-2.dll','shaders/vulkan-1.dll',
    'shaders/Microsoft.WindowsAppRuntime.Bootstrap.dll','deep/inner/extra.DLL','shaders/hidden.dll')){
    $path=Get-ChildPath $fixture.publish $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null
    $r=$relative
    Mutate ('nested DLL relative path rejects '+$relative) $path {
        param($target)
        if($r.EndsWith('Microsoft.WindowsAppRuntime.Bootstrap.dll')){Copy-Item -LiteralPath (Get-ChildPath $fixture.publish 'Microsoft.WindowsAppRuntime.Bootstrap.dll') -Destination $target}
        elseif($r.EndsWith('libmpv-2.dll')){Copy-Item -LiteralPath (Get-ChildPath $fixture.publish 'libmpv-2.dll') -Destination $target}
        else{[IO.File]::WriteAllText($target,'unapproved nested DLL')}
        if($r.EndsWith('hidden.dll')){[IO.File]::SetAttributes($target,[IO.FileAttributes]::Hidden)}
    } $relative
}
# 两个正式测试入口只在隔离repo设置junction；比较第一次mkdir之前/之后整个外sentinel目录与字节。
$tools=Get-ChildPath $fixture.repository 'tools'
foreach($entry in @('test-mpv-payload.ps1','test-mpv-payload-boundaries.ps1')){
    foreach($ancestor in @('artifacts/r067-approved-payload','redirect-parent')){
        $outside=Get-ChildPath $fixture.fixture ('outside-entry-sentinel-'+$results.Count)
        [IO.Directory]::CreateDirectory($outside)|Out-Null
        $sentinel=Get-ChildPath $outside 'sentinel.txt'
        [IO.File]::WriteAllText($sentinel,'isolated outside sentinel',$encoding)
        $bytes=[IO.File]::ReadAllBytes($sentinel);$before=Snapshot $outside
        if($ancestor-eq 'redirect-parent'){
            # 已有artifacts含合成包，不替换它；在完全独立入口repo里测试更高一级祖先。
            $entryRepo=Get-ChildPath $fixture.fixture ('entry-repository-'+$results.Count)
            $entryTools=Get-ChildPath $entryRepo 'tools';[IO.Directory]::CreateDirectory($entryTools)|Out-Null
            foreach($name in @('mpv-payload-common.ps1',$entry)){Copy-Item -LiteralPath (Get-ChildPath $tools $name) -Destination (Get-ChildPath $entryTools $name)}
            $junction=Get-ChildPath $entryRepo 'artifacts';$script=Get-ChildPath $entryTools $entry
        }else{$junction=Get-ChildPath $fixture.repository $ancestor;$script=Get-ChildPath $tools $entry}
        New-Item -ItemType Junction -Path $junction -Target $outside|Out-Null
        try{
            $saved=$ErrorActionPreference;$ErrorActionPreference='Continue'
            try{$lines=@(& powershell -NoProfile -ExecutionPolicy Bypass -File $script -Phase entry-green 2>&1);$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
            $unchanged=$before-ceq(Snapshot $outside)
            $bytesUnchanged=[Convert]::ToBase64String($bytes)-ceq[Convert]::ToBase64String([IO.File]::ReadAllBytes($sentinel))
            $result=[pscustomobject]@{exitCode=$code;output=($lines|Out-String);arguments=@('-File',$script,'-Phase','entry-green')}
            Save ('script entry first mkdir rejects '+$entry+' '+$ancestor) ($code-eq 1 -and $unchanged -and $bytesUnchanged -and $result.output.Contains('重解析')) 1 $result @{outsideSentinel=$outside;sentinelBytesUnchanged=$bytesUnchanged;outsideDirectorySnapshotUnchanged=$unchanged;zeroOutsideWrites=$unchanged;realRepositoryJunctionNeverCreated=$true}
        }finally{[IO.Directory]::Delete($junction)}
    }
}
Check 'restored complete synthetic payload' $true
$failed=@($results|Where-Object{-not $_.passed}).Count
$summary=[ordered]@{phase=$Phase;date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K');timezone='Asia/Shanghai';total=$results.Count;passed=($results.Count-$failed);failed=$failed;skipped=0;cases=$results.ToArray();realNativeGatePassed=$false;
    scope='Actual production verifier and entry scripts, complete synthetic pinned approval, default-style URL/external cache fully in scratch; no network/native/default real restore/publish.'}
[IO.File]::WriteAllText((Get-ChildPath $run 'results.json'),($summary|ConvertTo-Json -Depth 16)+"`n",$encoding)
Write-Output "Release layout checks: $($results.Count) total, $failed failed, 0 skipped; $run"
if($failed-gt 0){exit 1}
exit 0
