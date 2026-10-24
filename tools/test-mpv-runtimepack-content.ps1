[CmdletBinding()]
param([string]$Phase='runtimepack-content-green')
$ErrorActionPreference='Stop'
if ($Phase -cnotmatch '^[a-z0-9-]+$') { throw '非法 Phase。' }
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
. (Join-Path $PSScriptRoot 'mpv-nuget-content-hash.ps1')
$repo=(Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
Assert-PlainPath $repo
$run=Get-ChildPath $repo ('artifacts/r067-approved-payload/'+$Phase+'-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
[IO.Directory]::CreateDirectory($run)|Out-Null
$fixture=New-MpvPayloadFixture $repo (Get-ChildPath $run 'isolated-synthetic')
$encoding=[Text.UTF8Encoding]::new($false)
$results=[Collections.Generic.List[object]]::new()
function Assert-Scratch([string]$Path) {
    $absolute=[IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($fixture.fixture+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'runtimepack 变异/写入目标越界。' }
    Assert-PlainPath $absolute
    return $absolute
}
function Save([string]$Name,[int]$Expected,$Result,$Details=$null) {
    $log=Get-ChildPath $run ('case-'+($results.Count+1).ToString('D3')+'.log')
    [IO.File]::WriteAllText($log,$Result.output.Replace([string][char]13,'')+[char]10,$encoding)
    $passed=$Result.exitCode -eq $Expected
    if ($null -ne $Details -and $Details.PSObject.Properties['trustInputsUnchanged']) { $passed=$passed -and $Details.trustInputsUnchanged }
    if ($null -ne $Details -and $Details.PSObject.Properties['expectedFailure']) { $passed=$passed -and $Result.output.Contains($Details.expectedFailure) }
    $results.Add([pscustomobject]@{name=$Name;passed=$passed;expectedExit=$Expected;exitCode=$Result.exitCode;log=$log;
        details=$Details;output=$Result.output;arguments=$Result.arguments;nativeLoaded=$false;networkUsed=$false})
    [IO.File]::WriteAllText((Get-ChildPath $run 'case-results.json'),($results.ToArray()|ConvertTo-Json -Depth 16)+[char]10,$encoding)
    Write-Output "$Name : $passed expected=$Expected actual=$($Result.exitCode)"
}
$assetsPath=Get-ChildPath $fixture.repository 'src/Momoka.Shell/obj/project.assets.json'
$assets=Get-Content -LiteralPath $assetsPath -Raw|ConvertFrom-Json
$packages=@($assets.packageFolders.PSObject.Properties.Name)[0].TrimEnd('\')
$external=Get-ChildPath $fixture.fixture 'default-user-cache/packages'
# 全部 target/source 先解析；外部只是相对 synthetic repository 的布局，不是真实 global cache。
$copyPlan=@(Get-PlainFiles $packages -Recurse|ForEach-Object{
    [pscustomobject]@{source=(Assert-Scratch $_.FullName);target=(Assert-Scratch (Get-ChildPath $external $_.FullName.Substring($packages.Length+1).Replace('\','/')))}
})
foreach ($copy in $copyPlan) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $copy.target))|Out-Null
    Copy-Item -LiteralPath $copy.source -Destination $copy.target
}
foreach ($file in @(Get-PlainFiles $external -Recurse|Where-Object{$_.Name -ceq '.nupkg.metadata'})) {
    $metadata=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
    $metadata.source='https://api.nuget.org/v3/index.json'
    [IO.File]::WriteAllText((Assert-Scratch $file.FullName),($metadata|ConvertTo-Json),$encoding)
}
$assets.packageFolders=[pscustomobject]@{($external+'\')=[pscustomobject]@{}}
[IO.File]::WriteAllText((Assert-Scratch $assetsPath),($assets|ConvertTo-Json -Depth 18),$encoding)
$tools=Get-ChildPath $fixture.repository 'tools'
$toolPlan=@('verify-publish.ps1','verify-mpv-payload.ps1','mpv-payload-common.ps1','mpv-payload-dependencies.ps1','mpv-nuget-content-hash.ps1')|ForEach-Object{
    [pscustomobject]@{source=(Get-ChildPath $repo ('tools/'+$_));target=(Assert-Scratch (Get-ChildPath $tools $_))}
}
foreach ($copy in $toolPlan) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $copy.target))|Out-Null
    Copy-Item -LiteralPath $copy.source -Destination $copy.target
}
$check=Get-ChildPath $tools 'verify-publish.ps1'
function Invoke-DefaultCheck {
    $saved=$ErrorActionPreference;$ErrorActionPreference='Continue'
    try {
        $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$check,'-PublishRoot',$fixture.publish)
        $lines=@(& powershell @arguments 2>&1);$code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    return [pscustomobject]@{exitCode=$code;output=($lines|Out-String);arguments=$arguments}
}
$pinPath=Get-ChildPath $fixture.repository 'assets/mpv-runtime/approved-runtimepack.json'
$packDirectory=Get-ChildPath $external 'microsoft.windows.sdk.net.ref/10.0.19041.57'
$archive=Get-ChildPath $packDirectory 'microsoft.windows.sdk.net.ref.10.0.19041.57.nupkg'
$side=$archive+'.sha512'
$asset='lib/net8.0/Microsoft.Windows.SDK.NET.dll'
$extract=Get-ChildPath $packDirectory $asset
$published=Get-ChildPath $fixture.publish 'Microsoft.Windows.SDK.NET.dll'
$metadataPath=Get-ChildPath $external 'microsoft.windows.sdk.net.ref/10.0.19041.57/.nupkg.metadata'
$trustPaths=@($assetsPath,(Get-ChildPath $fixture.repository 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.deps.json'),
    (Get-ChildPath $fixture.publish 'Momoka.deps.json'),$fixture.manifest,(Get-ChildPath $fixture.repository 'src/Momoka.Shell/Momoka.Shell.csproj'),
    (Get-ChildPath $fixture.repository 'global.json'),$pinPath)
$trustBefore=@($trustPaths|ForEach-Object{[pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash;bytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_))}})
Save 'complete synthetic runtimepack default invocation external cache and NuGet URL' 0 (Invoke-DefaultCheck)
$savedBytes=@{}
foreach ($target in @($archive,$side,$metadataPath,$extract,$published)) { $null=Assert-Scratch $target;$savedBytes[$target]=[IO.File]::ReadAllBytes($target) }
$initialHash=Get-MpvNuGetPackageContentHash $archive
try {
    $bytes=[byte[]]$savedBytes[$extract].Clone();$bytes[0]=$bytes[0] -bxor 1
    $memory=[IO.MemoryStream]::new();$memory.Write($savedBytes[$archive],0,$savedBytes[$archive].Length);$memory.Position=0
    Add-Type -AssemblyName System.IO.Compression
    $zip=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Update,$true)
    try {
        $entry=$zip.GetEntry($asset);$entry.Delete();$entry=$zip.CreateEntry($asset)
        $entry.LastWriteTime=[DateTimeOffset]::new(2026,10,24,0,0,0,[TimeSpan]::Zero)
        $stream=$entry.Open();try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    } finally {$zip.Dispose()}
    try{$archiveBytes=$memory.ToArray()}finally{$memory.Dispose()}
    $algorithm=[Security.Cryptography.SHA512]::Create()
    try{$raw=[Convert]::ToBase64String($algorithm.ComputeHash($archiveBytes))}finally{$algorithm.Dispose()}
    [IO.File]::WriteAllBytes((Assert-Scratch $archive),$archiveBytes)
    [IO.File]::WriteAllText((Assert-Scratch $side),$raw,$encoding)
    $actualHash=Get-MpvNuGetPackageContentHash $archive
    $metadata=Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json;$metadata.contentHash=$actualHash
    [IO.File]::WriteAllText((Assert-Scratch $metadataPath),($metadata|ConvertTo-Json),$encoding)
    [IO.File]::WriteAllBytes((Assert-Scratch $extract),$bytes)
    [IO.File]::WriteAllBytes((Assert-Scratch $published),$bytes)
    $trustAfter=@($trustBefore|ForEach-Object{[pscustomobject]@{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash;
        unchanged=($_.bytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($_.path)))}})
    $unchanged=@($trustAfter|Where-Object{-not $_.unchanged}).Count -eq 0
    $attack=Get-ChildPath $fixture.fixture 'joint-mutation-bytes'
    $attackPlan=@($savedBytes.Keys|ForEach-Object{[pscustomobject]@{source=(Assert-Scratch $_);target=(Assert-Scratch (Get-ChildPath $attack $_.Substring($fixture.fixture.Length+1).Replace('\','/')))}})
    foreach ($copy in $attackPlan) {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $copy.target))|Out-Null
        Copy-Item -LiteralPath $copy.source -Destination $copy.target
    }
    Save 'joint runtimepack archive sideHash metadata extract publish replacement rejects with seven fixed trust inputs' 1 (Invoke-DefaultCheck) ([pscustomobject]@{
        trustInputsUnchanged=$unchanged;expectedFailure='可信包内容/归档与当前 restore 不符';unmodifiedTrustInputs=$trustAfter;fixedTrustCount=7;runtimepack='runtimepack.Microsoft.Windows.SDK.NET.Ref/10.0.19041.57';asset=$asset;
        initialContentHash=$initialHash;replacementContentHash=$actualHash;metadataEqualsActualNewContentHash=($metadata.contentHash -ceq $actualHash);
        sideMatchesNewArchive=($raw -ceq $actualHash);pinNeverRecalculated=$true;attackBytes=$attack;allAttackTargetsInsideExtraScratch=$true;realCacheWritten=$false})
} finally {foreach ($target in $savedBytes.Keys) {[IO.File]::WriteAllBytes((Assert-Scratch $target),$savedBytes[$target])}}
Save 'same synthetic runtimepack and independent pin restored stays accepted' 0 (Invoke-DefaultCheck)
# 只验证这个独立 pin 的 selected 身份/资产边界；每次只写 scratch，生产批准清单始终只读。
function Pin-Case([string]$Name,[scriptblock]$Action,[string]$Needle='') {
    $original=[IO.File]::ReadAllBytes($pinPath)
    try {
        $pin=Get-Content -LiteralPath $pinPath -Raw|ConvertFrom-Json
        & $Action $pin
        [IO.File]::WriteAllText((Assert-Scratch $pinPath),($pin|ConvertTo-Json -Depth 10),$encoding)
        $details=if($Needle){[pscustomobject]@{expectedFailure=$Needle}}else{$null}
        Save $Name 1 (Invoke-DefaultCheck) $details
    } finally {[IO.File]::WriteAllBytes((Assert-Scratch $pinPath),$original)}
}
$realPinPath=Get-ChildPath $repo 'assets/mpv-runtime/approved-runtimepack.json'
$realPin=Get-Content -LiteralPath $realPinPath -Raw|ConvertFrom-Json
$expected='5I8PT7WbBvGU7kq8caXQwxm8V4/+5DD6XKpg5TfRK8QyFoxr1xvpa7Q5o+rmrdJweLAh7Dm9TqKkLTym3klvDA=='
$constants=$realPin.packageId -ceq 'Microsoft.Windows.SDK.NET.Ref' -and $realPin.packageVersion -ceq '10.0.19041.57' -and
    $realPin.sdkVersion -ceq '10.0.400' -and $realPin.targetFramework -ceq 'net10.0-windows10.0.19041.0' -and $realPin.runtimeIdentifier -ceq 'win-x64' -and
    $realPin.contentHash -ceq $expected -and $realPin.archive.size -eq 12719136 -and
    $realPin.archive.sha256 -ceq '90b18f2cf7d7a23f5b0bfe2b86d8dda45881481f677ce36eccaf6e9ab50b6b4b' -and $realPin.assets.Count -eq 2 -and
    $realPin.assets[0].source -ceq 'lib/net8.0/Microsoft.Windows.SDK.NET.dll' -and $realPin.assets[1].source -ceq 'lib/net8.0/WinRT.Runtime.dll'
Save 'real approved runtimepack identity and two asset constants no native execution' 0 ([pscustomobject]@{exitCode=$(if($constants){0}else{1});output=($realPin|ConvertTo-Json -Depth 8);arguments=@('read-only-approved-runtimepack-constants',$realPinPath)})
$original=[IO.File]::ReadAllBytes($pinPath)
try {Remove-Item -LiteralPath (Assert-Scratch $pinPath);Save 'missing independent runtimepack pin fails closed no cache fallback' 1 (Invoke-DefaultCheck) ([pscustomobject]@{expectedFailure='缺少独立 runtimepack'})}
finally {[IO.File]::WriteAllBytes((Assert-Scratch $pinPath),$original)}
Pin-Case 'malformed pin contentHash rejected' {param($p);$p.contentHash='invalid'} '内容摘要'
Pin-Case 'well-formed wrong pin contentHash cannot trust cache metadata' {param($p);$p.contentHash=[Convert]::ToBase64String([byte[]]::new(64))} 'restore'
foreach ($field in @('sdkVersion','targetFramework','runtimeIdentifier','packageId','packageVersion')) {
    $identityField=$field
    Pin-Case ('pin selected identity mismatch '+$identityField) {param($p);$p.$identityField=if($identityField -eq 'packageVersion'){'10.0.19041.58'}else{'R067.Foreign'}} 'runtimepack'
}
Pin-Case 'empty approved runtimepack assets reject' {param($p);$p.assets=@()} '非空数组'
Pin-Case 'trusted build second runtimepack asset cannot be omitted from pin' {param($p);$p.assets=@($p.assets[0])} '独立批准映射'
Pin-Case 'duplicate approved runtimepack asset reject' {param($p);$p.assets+=$p.assets[0]} '重复'
Pin-Case 'case alias approved runtimepack asset reject' {param($p);$a=$p.assets[0]|ConvertTo-Json|ConvertFrom-Json;$a.path=$a.path.ToUpperInvariant();$a.source=$a.source.ToUpperInvariant();$p.assets+=$a} 'runtimepack'
Pin-Case 'runtimepack approved target traversal rejected' {param($p);$p.assets[-1].path='../../../../../LICENSE'} '非法清单路径'
Pin-Case 'runtimepack approved source traversal rejected' {param($p);$p.assets[-1].source='../../../../../LICENSE'} '非法清单路径'
Pin-Case 'runtimepack Windows reserved source rejected' {param($p);$p.assets[-1].source='lib/net8.0/NUL.dll'} '非法清单路径'
Pin-Case 'runtimepack array contentHash rejected not collection comparison' {param($p);$p.contentHash=@()} '标量字符串'
try {
    $text=[Text.Encoding]::UTF8.GetString($original)
    $text=$text.Replace('"contentHash":',('"contentHash":"'+$initialHash+'","content\u0048ash":'))
    [IO.File]::WriteAllText((Assert-Scratch $pinPath),$text,$encoding)
    Save 'escaped duplicate pin contentHash JSON rejects' 1 (Invoke-DefaultCheck) ([pscustomobject]@{expectedFailure='重复 JSON 字段'})
} finally {[IO.File]::WriteAllBytes((Assert-Scratch $pinPath),$original)}
Pin-Case 'runtimepack approved archive size cannot drift' {param($p);$p.archive.size++} '独立批准字节索引'
Pin-Case 'runtimepack approved archive SHA256 cannot drift' {param($p);$p.archive.sha256=('0'*64)} '独立批准字节索引'
# 同步篡改 trusted/publish deps 仍不能把新的 basename/资产加进固定 runtimepack pin。
$trustedDeps=$trustPaths[1];$publishDeps=$trustPaths[2]
$both=@{};foreach($target in @($trustedDeps,$publishDeps)){$both[$target]=[IO.File]::ReadAllBytes($target)}
$extra=Get-ChildPath $fixture.publish 'R067.Foreign.Runtimepack.dll'
$pinHash=(Get-FileHash -LiteralPath $pinPath -Algorithm SHA256).Hash
try {
    [IO.File]::WriteAllText((Assert-Scratch $extra),'unapproved synthetic extra runtimepack DLL -- never load',$encoding)
    foreach ($target in $both.Keys) {
        $deps=Get-Content -LiteralPath $target -Raw|ConvertFrom-Json
        $deps.targets.($deps.runtimeTarget.name).'runtimepack.Microsoft.Windows.SDK.NET.Ref/10.0.19041.57'.runtime|
            Add-Member -NotePropertyName 'R067.Foreign.Runtimepack.dll' -NotePropertyValue ([pscustomobject]@{})
        [IO.File]::WriteAllText((Assert-Scratch $target),($deps|ConvertTo-Json -Depth 16),$encoding)
    }
    Save 'changed trusted and publish deps cannot self-authorize foreign runtimepack asset with fixed pin' 1 (Invoke-DefaultCheck) ([pscustomobject]@{
        expectedFailure='独立批准映射';trustInputsUnchanged=($pinHash -ceq (Get-FileHash -LiteralPath $pinPath -Algorithm SHA256).Hash);fixedIndependentPin=$pinPath})
} finally {
    foreach($target in $both.Keys){[IO.File]::WriteAllBytes((Assert-Scratch $target),$both[$target])}
    Remove-Item -LiteralPath (Assert-Scratch $extra)
}
Save 'final complete synthetic default runtimepack with unchanged independent pin accepted' 0 (Invoke-DefaultCheck)
$failed=@($results|Where-Object{-not $_.passed}).Count
$summary=[ordered]@{phase=$Phase;date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K');timezone='Asia/Shanghai';total=$results.Count;passed=($results.Count-$failed);failed=$failed;skipped=0;cases=$results.ToArray();realNativeGatePassed=$false;
    scope='Production default verifier, complete synthetic approved payload and independent fixed runtimepack pin. All joint attacks in scratch, seven fixed trust inputs unchanged; no real cache/repository/native/network writes.'}
[IO.File]::WriteAllText((Get-ChildPath $run 'results.json'),($summary|ConvertTo-Json -Depth 18)+[char]10,$encoding)
Write-Output "Runtimepack content checks: $($results.Count) total, $failed failed, 0 skipped; $run"
if ($failed -gt 0) {exit 1}
exit 0
