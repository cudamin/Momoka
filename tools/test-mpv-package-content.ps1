[CmdletBinding()]
param([string]$Phase='package-content-green')
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
function Save([string]$Name,[int]$Expected,$Result,$Details=$null) {
    $log=Get-ChildPath $run ('case-'+($results.Count+1).ToString('D3')+'.log')
    [IO.File]::WriteAllText($log,$Result.output.Replace("`r`n","`n")+"`n",$encoding)
    $passed=$Result.exitCode-eq $Expected
    if ($null-ne $Details -and $Details.PSObject.Properties['trustInputsUnchanged']) { $passed=$passed -and $Details.trustInputsUnchanged }
    if ($null-ne $Details -and $Details.PSObject.Properties['expectedFailure']) { $passed=$passed -and $Result.output.Contains($Details.expectedFailure) }
    $results.Add([pscustomobject]@{name=$Name;passed=$passed;expectedExit=$Expected;exitCode=$Result.exitCode;log=$log;
        details=$Details;output=$Result.output;arguments=$Result.arguments;nativeLoaded=$false;networkUsed=$false})
    [IO.File]::WriteAllText((Get-ChildPath $run 'case-results.json'),($results.ToArray()|ConvertTo-Json -Depth 14)+"`n",$encoding)
    Write-Output "$Name : $passed expected=$Expected actual=$($Result.exitCode)"
}
# 全部攻击仅指本树 artifacts 内额外 scratch，绝不修改实际 NuGet cache 或项目来源。
$assetsPath=Get-ChildPath $fixture.repository 'src/Momoka.Shell/obj/project.assets.json'
$assets=Get-Content -LiteralPath $assetsPath -Raw|ConvertFrom-Json
$packages=@($assets.packageFolders.PSObject.Properties.Name)[0].TrimEnd('\')
$external=Get-ChildPath $fixture.fixture 'default-user-cache/packages'
foreach($file in @(Get-PlainFiles $packages -Recurse)) {
    $target=Get-ChildPath $external $file.FullName.Substring($packages.Length+1).Replace('\','/')
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
foreach($file in @(Get-PlainFiles $external -Recurse|Where-Object{$_.Name-ceq '.nupkg.metadata'})) {
    $metadata=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
    $metadata.source='https://api.nuget.org/v3/index.json'
    [IO.File]::WriteAllText($file.FullName,($metadata|ConvertTo-Json),$encoding)
}
$assets.packageFolders=[pscustomobject]@{($external+'\')=[pscustomobject]@{}}
[IO.File]::WriteAllText($assetsPath,($assets|ConvertTo-Json -Depth 18),$encoding)
$library='Microsoft.WindowsAppSDK.Foundation/2.3.12'
$asset='runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll'
$archive=Get-ChildPath $external 'microsoft.windowsappsdk.foundation/2.3.12/microsoft.windowsappsdk.foundation.2.3.12.nupkg'
$side=$archive+'.sha512'
$extract=Get-ChildPath $external ('microsoft.windowsappsdk.foundation/2.3.12/'+$asset)
$publish=Get-ChildPath $fixture.publish 'Microsoft.WindowsAppRuntime.Bootstrap.dll'
$metadataPath=Get-ChildPath $external 'microsoft.windowsappsdk.foundation/2.3.12/.nupkg.metadata'
$trustPaths=@($assetsPath,(Get-ChildPath $fixture.repository 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.deps.json'),
    $fixture.manifest,(Get-ChildPath $fixture.publish 'Momoka.deps.json'),$metadataPath)
$trustBefore=@($trustPaths|ForEach-Object{[pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash;bytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_))}})
$result=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish
Save 'complete synthetic default-style cache and NuGet URL baseline' 0 $result
$saved=@{};foreach($path in @($archive,$side,$extract,$publish)){$saved[$path]=[IO.File]::ReadAllBytes($path)}
try {
    $bytes=[byte[]]$saved[$extract].Clone();$bytes[0]=$bytes[0]-bxor 1
    $memory=[IO.MemoryStream]::new()
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        $entry=$zip.CreateEntry($asset);$entry.LastWriteTime=[DateTimeOffset]::new(2026,10,24,0,0,0,[TimeSpan]::Zero)
        $stream=$entry.Open();try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    } finally {$zip.Dispose()}
    try{$archiveBytes=$memory.ToArray()}finally{$memory.Dispose()}
    $hashAlgorithm=[Security.Cryptography.SHA512]::Create()
    try{$raw=[Convert]::ToBase64String($hashAlgorithm.ComputeHash($archiveBytes))}finally{$hashAlgorithm.Dispose()}
    [IO.File]::WriteAllBytes($archive,$archiveBytes)
    [IO.File]::WriteAllText($side,$raw,$encoding)
    [IO.File]::WriteAllBytes($extract,$bytes)
    [IO.File]::WriteAllBytes($publish,$bytes)
    $trustAfter=@($trustBefore|ForEach-Object{[pscustomobject]@{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash;
        unchanged=($_.bytes-ceq[Convert]::ToBase64String([IO.File]::ReadAllBytes($_.path)))}})
    $unchanged=@($trustAfter|Where-Object{-not $_.unchanged}).Count-eq 0
    $attack=Get-ChildPath $run 'joint-mutation-bytes'
    foreach($path in @($archive,$side,$extract,$publish)){
        $target=Get-ChildPath $attack ($path.Substring($fixture.fixture.Length+1).Replace('\','/'))
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
        Copy-Item -LiteralPath $path -Destination $target
    }
    $result=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish
    Save 'joint archive/sha512/extract/publish replacement cannot change pinned approval' 1 $result ([pscustomobject]@{
        trustInputsUnchanged=$unchanged;expectedFailure='实际包内容摘要与固定 restore 不符';unmodifiedTrustInputs=$trustAfter;pinnedLibrary=$library;pinnedAsset=$asset;
        restoreContentHash=$assets.libraries.$library.sha512;metadataContentHash=(Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json).contentHash;
        changedRawArchiveHash=$raw;sideMatchesChangedArchive=$true;extractMatchesChangedArchive=$true;publishMatchesChangedExtract=$true;
        allAttackTargetsInsideExtraScratch=$true;attackBytes=$attack})
} finally {foreach($path in $saved.Keys){[IO.File]::WriteAllBytes($path,$saved[$path])}}
$result=Invoke-MpvPublishFixture $check $fixture.repository $fixture.publish
Save 'same pinned synthetic package restored remains accepted' 0 $result
# 本机已恢复的真实签名 managed 包只读作官方算法验收；攻击仅在scratch副本，绝不加载该包的DLL。
. (Join-Path $PSScriptRoot 'mpv-nuget-content-hash.ps1')
$currentAssets=Get-Content -LiteralPath (Get-ChildPath $repo 'src/Momoka.Shell/obj/project.assets.json') -Raw|ConvertFrom-Json
$signedLibrary='CommunityToolkit.Common/8.2.1'
$packageFolders=@($currentAssets.packageFolders.PSObject.Properties.Name)
if($packageFolders.Count-ne 1){throw '当前固定restore包根不唯一，停止而非跳过签名包测试。'}
$signedRoot=[IO.Path]::GetFullPath($packageFolders[0]).TrimEnd('\')
Assert-PlainPath $signedRoot
$signedArchive=Get-ChildPath $signedRoot 'communitytoolkit.common/8.2.1/communitytoolkit.common.8.2.1.nupkg'
$signedSourceHash=(Get-FileHash -LiteralPath $signedArchive -Algorithm SHA256).Hash
$expectedSigned=$currentAssets.libraries.$signedLibrary.sha512
$computed=Get-MpvNuGetPackageContentHash $signedArchive
$rawAlgorithm=[Security.Cryptography.SHA512]::Create()
try{$rawSigned=[Convert]::ToBase64String($rawAlgorithm.ComputeHash([IO.File]::ReadAllBytes($signedArchive)))}finally{$rawAlgorithm.Dispose()}
$ok=[string]::Equals($computed,$expectedSigned,[StringComparison]::Ordinal) -and $computed-cne $rawSigned
Save 'official signed-package content hash matches pinned restore not raw archive hash' 0 ([pscustomobject]@{exitCode=$(if($ok){0}else{1});output=('actualContent='+$computed+'; fixedRestore='+$expectedSigned+'; raw='+$rawSigned);arguments=@('Get-MpvNuGetPackageContentHash',$signedArchive)}) ([pscustomobject]@{sourceReadOnly=$true;nativeLoaded=$false})
$signedCopy=Get-ChildPath $run 'signed-managed-fixture/communitytoolkit.common.8.2.1.nupkg'
[IO.Directory]::CreateDirectory((Split-Path -Parent $signedCopy))|Out-Null
Copy-Item -LiteralPath $signedArchive -Destination $signedCopy
$copyBytes=[IO.File]::ReadAllBytes($signedCopy)
$computed=Get-MpvNuGetPackageContentHash $signedCopy
Save 'untouched signed-package scratch copy passes official integrity' 0 ([pscustomobject]@{exitCode=$(if([string]::Equals($computed,$expectedSigned,[StringComparison]::Ordinal)){0}else{1});output=('contentHash='+$computed);arguments=@('Get-MpvNuGetPackageContentHash',$signedCopy)})
try{
    $memory=[IO.MemoryStream]::new()
    $memory.Write($copyBytes,0,$copyBytes.Length);$memory.Position=0
    $zip=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Update,$true)
    try{
        $entry=$zip.GetEntry('lib/net6.0/CommunityToolkit.Common.dll')
        if($null-eq $entry -or $null-eq $zip.GetEntry('.signature.p7s')){throw '真实签名managed包固定entry不存在。'}
        $stream=$entry.Open();try{$payload=[IO.MemoryStream]::new();try{$stream.CopyTo($payload);$bytes=$payload.ToArray()}finally{$payload.Dispose()}}finally{$stream.Dispose()}
        $entry.Delete();$bytes[0]=$bytes[0]-bxor 1
        $entry=$zip.CreateEntry('lib/net6.0/CommunityToolkit.Common.dll')
        $stream=$entry.Open();try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    }finally{$zip.Dispose()}
    try{$changed=$memory.ToArray()}finally{$memory.Dispose()}
    [IO.File]::WriteAllBytes($signedCopy,$changed)
    $result=[pscustomobject]@{exitCode=0;output='';arguments=@('Get-MpvNuGetPackageContentHash',$signedCopy)}
    try{$result.output=Get-MpvNuGetPackageContentHash $signedCopy}catch{$result.exitCode=1;$result.output=$_.Exception.ToString()}
    Save 'changed signed-package content with old signature rejects locally' 1 $result ([pscustomobject]@{expectedFailure='实际包内容校验失败';sourcePackageUnchanged=($signedSourceHash-eq(Get-FileHash -LiteralPath $signedArchive -Algorithm SHA256).Hash);attackScratchOnly=$true;networkUsed=$false})
}finally{[IO.File]::WriteAllBytes($signedCopy,$copyBytes)}
$computed=Get-MpvNuGetPackageContentHash $signedCopy
$unchanged=$signedSourceHash-eq(Get-FileHash -LiteralPath $signedArchive -Algorithm SHA256).Hash
Save 'restored signed-package copy and untouched source remain valid' 0 ([pscustomobject]@{exitCode=$(if($unchanged -and [string]::Equals($computed,$expectedSigned,[StringComparison]::Ordinal)){0}else{1});output=('sourceUntouched='+$unchanged+'; hash='+$computed);arguments=@('Get-MpvNuGetPackageContentHash',$signedCopy)})
$failed=@($results|Where-Object{-not $_.passed}).Count
$summary=[ordered]@{phase=$Phase;date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K');timezone='Asia/Shanghai';total=$results.Count;passed=($results.Count-$failed);failed=$failed;skipped=0;cases=$results.ToArray();realNativeGatePassed=$false;
    scope='Actual production verifier with complete synthetic approved payload, default-style external cache and URL; five fixed trust inputs stay byte-identical during joint mutation. No real cache/source/native/network writes.'}
[IO.File]::WriteAllText((Get-ChildPath $run 'results.json'),($summary|ConvertTo-Json -Depth 16)+"`n",$encoding)
Write-Output "Package content checks: $($results.Count) total, $failed failed, 0 skipped; $run"
if($failed-gt 0){exit 1}
exit 0
