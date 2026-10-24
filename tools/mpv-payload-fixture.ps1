# 完全固定的合成可信依赖：模拟独立 restore/编译输入，不复制实际 native，也不从待验 deps 推导批准值。
function New-MpvDependencyFixturePlan([string]$SourceRepository,[string]$Repository,[string]$Publish) {
    $project=[xml]([IO.File]::ReadAllText((Get-ChildPath $SourceRepository 'src/Momoka.Shell/Momoka.Shell.csproj')))
    $tfm=$project.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
    $rid=$project.SelectSingleNode('/Project/PropertyGroup/RuntimeIdentifier').InnerText
    $active='.NETCoreApp,Version=v10.0/'+$rid
    $selected=@{}; $libraries=@{}; $files=[Collections.Generic.List[object]]::new()
    $targets=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    function Plan-Bytes([string]$Root,[string]$Relative,[byte[]]$Bytes) {
        $target=Get-ChildPath $Root $Relative
        if (-not $targets.Add($target)) { Fail-Payload "合成依赖写计划重复：$Relative" }
        $files.Add([pscustomobject]@{target=$target;bytes=$Bytes})
    }
    $encoding=[Text.UTF8Encoding]::new($false)
    $packages=Get-ChildPath $Repository 'artifacts/r067-fixed-dependency-packages'
    $feed=Get-ChildPath $Repository 'artifacts/r067-fixed-dependency-feed'
    foreach ($reference in $project.SelectNodes('/Project/ItemGroup/PackageReference')) {
        $key=$reference.GetAttribute('Include')+'/'+$reference.GetAttribute('Version').Trim('[',']')
        $selected[$key]=@{type='package'}
        $libraries[$key]=@{type='package';path=$key.ToLowerInvariant();sha512='';files=@()}
    }
    $selected['Momoka.Core/0.1.7']=@{type='project';runtime=@{'bin/placeholder/Momoka.Core.dll'=@{}}}
    $libraries['Momoka.Core/0.1.7']=@{type='project';msbuildProject='../Momoka.Core/Momoka.Core.csproj'}
    $dependencyGroups=@{
        'Microsoft.WindowsAppSDK.Foundation/2.3.12'=@{'runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll'='R067 offline host fixture themeresources -- never launch'}
        'Microsoft.WindowsAppSDK.WinUI/2.3.9'=@{'runtimes-framework/win-x64/native/Microsoft.UI.Xaml.Controls.dll'='fixed SDK target copied DLL fixture'}
        'Microsoft.WindowsAppSDK.InteractiveExperiences/2.1.9'=@{'runtimes-framework/win-x64/native/CoreMessagingXP.dll'='R067 fixed synthetic SDK content'}
        'Microsoft.WindowsAppSDK.DWrite/2.1.0'=@{'runtimes-framework/win-x64/native/DWriteCore.dll'='R067 fixed synthetic DWrite content'}
        'Microsoft.Web.WebView2/1.0.3719.77'=@{'runtimes/win-x64/native_uap/Microsoft.Web.WebView2.Core.dll'='fixed WebView2 target copied DLL fixture'}
        'R067.Offline.Native/1.0.0'=@{'runtimes/win-x64/native/hostfxr.dll'='R067 offline host fixture themeresources -- never launch'}
    }
    # runtimepack 的 restore downloadDependencies 没有摘要；合成独立 pin 只取以下固定计划字节，绝不取待验 cache。
    $packKey='Microsoft.Windows.SDK.NET.Ref/10.0.19041.57'
    $dependencyGroups[$packKey]=@{
        'lib/net8.0/Microsoft.Windows.SDK.NET.dll'='R067 OFFLINE SYNTHETIC FIXED Windows SDK runtimepack DLL -- never load'
        'lib/net8.0/WinRT.Runtime.dll'='R067 OFFLINE SYNTHETIC FIXED WinRT runtimepack DLL -- never load'
    }
    $runtimepackApproval=$null
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($key in $dependencyGroups.Keys) {
        if ($key -cne $packKey -and -not $selected.ContainsKey($key)) { $selected[$key]=@{type='package'} }
        $memory=[IO.MemoryStream]::new(); $zip=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($relative in ($dependencyGroups[$key].Keys | Sort-Object)) {
                Assert-RelativePath $relative
                $entry=$zip.CreateEntry($relative); $entry.LastWriteTime=[DateTimeOffset]::new(2026,10,24,0,0,0,[TimeSpan]::Zero)
                $bytes=$encoding.GetBytes($dependencyGroups[$key][$relative]); $stream=$entry.Open()
                try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
                Plan-Bytes $packages ($key.ToLowerInvariant()+'/'+$relative) $bytes
            }
        } finally { $zip.Dispose() }
        try { $archive=$memory.ToArray() } finally { $memory.Dispose() }
        $algorithm=[Security.Cryptography.SHA512]::Create(); try { $hash=[Convert]::ToBase64String($algorithm.ComputeHash($archive)) } finally { $algorithm.Dispose() }
        $record=@{type='package';path=$key.ToLowerInvariant();sha512=$hash;files=@($dependencyGroups[$key].Keys)}
        $name=$key.ToLowerInvariant().Replace('/','.')+'.nupkg'
        if ($key -ceq $packKey) {
            $sha256=[Security.Cryptography.SHA256]::Create()
            try { $archiveHash=[BitConverter]::ToString($sha256.ComputeHash($archive)).Replace('-','').ToLowerInvariant() } finally { $sha256.Dispose() }
            $runtimepackApproval=[ordered]@{
                schemaVersion=1; approvalId='R067-OFFLINE-SYNTHETIC-RUNTIMEPACK-NOT-PRODUCTION'
                sdkVersion=(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Get-ChildPath $SourceRepository 'global.json')))).sdk.version
                targetFramework=$tfm; runtimeIdentifier=$rid; packageId='Microsoft.Windows.SDK.NET.Ref'; packageVersion='10.0.19041.57'
                contentHash=$hash
                archive=@{source='R067 fixed synthetic ZIP plan; never a real NuGet approval';size=$archive.Length;sha256=$archiveHash}
                assets=@(
                    @{kind='runtime';path='Microsoft.Windows.SDK.NET.dll';source='lib/net8.0/Microsoft.Windows.SDK.NET.dll'},
                    @{kind='runtime';path='WinRT.Runtime.dll';source='lib/net8.0/WinRT.Runtime.dll'}
                )
            }
        } else { $libraries[$key]=$record }
        Plan-Bytes $packages ($record.path+'/'+$name) $archive
        Plan-Bytes $packages ($record.path+'/'+$name+'.sha512') $encoding.GetBytes($hash)
        Plan-Bytes $packages ($record.path+'/.nupkg.metadata') $encoding.GetBytes((@{version=2;contentHash=$hash;source=$feed;synthetic=$true}|ConvertTo-Json -Compress))
        Plan-Bytes $feed $name $archive
    }
    $bootstrap='runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll'
    $selected['Microsoft.WindowsAppSDK.Foundation/2.3.12'].native=@{ $bootstrap=@{} }
    $hostAsset='runtimes/win-x64/native/hostfxr.dll'; $selected['R067.Offline.Native/1.0.0'].native=@{$hostAsset=@{}}
    $assets=@{targets=@{ ($tfm+'/'+$rid)=$selected }; libraries=$libraries; packageFolders=@{($packages+'\')=@{}};
        project=@{restore=@{projectPath=(Get-ChildPath $Repository 'src/Momoka.Shell/Momoka.Shell.csproj')};frameworks=@{$tfm=@{downloadDependencies=@(@{name='Microsoft.Windows.SDK.NET.Ref';version='[10.0.19041.57, 10.0.19041.57]'})}}}}
    $deps=@{runtimeTarget=@{name=$active};targets=@{$active=@{
        'Momoka/0.1.7'=@{runtime=@{'Momoka.dll'=@{}}};'Momoka.Core/0.1.7'=@{runtime=@{'Momoka.Core.dll'=@{}}};
        'Microsoft.WindowsAppSDK.Foundation/2.3.12'=@{native=@{$bootstrap=@{}}};'R067.Offline.Native/1.0.0'=@{native=@{$hostAsset=@{}}}
    }};libraries=@{'Momoka/0.1.7'=@{type='project'};'Momoka.Core/0.1.7'=@{type='project'};
        'Microsoft.WindowsAppSDK.Foundation/2.3.12'=@{type='package'};'R067.Offline.Native/1.0.0'=@{type='package'}}}
    $packLibrary='runtimepack.'+$packKey
    $deps.targets[$active][$packLibrary]=@{runtime=@{'Microsoft.Windows.SDK.NET.dll'=@{};'WinRT.Runtime.dll'=@{}}}
    $deps.libraries[$packLibrary]=@{type='runtimepack'}
    foreach ($relative in $dependencyGroups[$packKey].Keys) {
        Plan-Bytes $Publish ([IO.Path]::GetFileName($relative)) $encoding.GetBytes($dependencyGroups[$packKey][$relative])
    }
    Plan-Bytes $Repository 'assets/mpv-runtime/approved-runtimepack.json' $encoding.GetBytes(($runtimepackApproval|ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n")
    $output='src/Momoka.Shell/bin/x64/Release/'+$tfm+'/'+$rid
    Plan-Bytes $Repository 'src/Momoka.Shell/obj/project.assets.json' $encoding.GetBytes(($assets|ConvertTo-Json -Depth 16))
    Plan-Bytes $Repository ($output+'/Momoka.deps.json') $encoding.GetBytes(($deps|ConvertTo-Json -Depth 16))
    Plan-Bytes $Repository ($output+'/Momoka.dll') $encoding.GetBytes('R067 fixed synthetic compiled Momoka DLL -- never launch')
    Plan-Bytes $Repository ($output+'/Momoka.exe') $encoding.GetBytes('R067 offline host fixture themeresources -- never launch')
    Plan-Bytes $Repository ($output+'/Momoka.Core.dll') $encoding.GetBytes('registered dependency fixture')
    Plan-Bytes $Repository 'src/Momoka.Core/bin/Release/net10.0/Momoka.Core.dll' $encoding.GetBytes('registered dependency fixture')
    Plan-Bytes $Publish 'Momoka.deps.json' $encoding.GetBytes(($deps|ConvertTo-Json -Depth 16))
    return $files.ToArray()
}
# R-067 离线夹具；仅由工具测试显式调用。没有生产 bypass，也不读取/复制任何真实 libmpv DLL。
function New-MpvPayloadFixture([string]$RepositoryRoot, [string]$FixtureRoot) {
    . (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
    $repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\')
    $fixture = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\')
    Assert-PlainPath $repo
    Assert-PlainPath $fixture
    if (-not $fixture.StartsWith($repo + '\artifacts\', [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $fixture)) { throw '合成夹具必须是本树 artifacts 内的全新目录。' }
    $repository = Get-ChildPath $fixture 'repository'
    $publish = Get-ChildPath $fixture 'publish'

    # 先完成全量只读 preflight；这里之前/期间没有任何 NewDirectory / Copy / Write。
    # 不能等生产 verifier 拒绝：path 将用于建目录和复制，必须连最后一项都先验完。
    $manifest = Read-Manifest (Get-ChildPath $repo 'assets/mpv-runtime/approved-payload.json')
    $native = Assert-NativeManifest $manifest
    $lib = $native['libmpv-2.dll']
    $vulkan = $native['vulkan-1.dll']
    $shipping = @{}
    Add-Shipping $shipping 'libmpv-2.dll' 'libmpv-2.dll'
    Add-Shipping $shipping 'vulkan-1.dll' 'assets/mpv-runtime/vulkan-1.dll'
    Add-Shipping $shipping 'LICENSE' 'LICENSE'
    Add-Shipping $shipping 'THIRD-PARTY-NOTICES.md' 'THIRD-PARTY-NOTICES.md'
    $copies = [Collections.Generic.List[object]]::new()
    $sources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @('mpv-ui', 'shaders', 'mpv-runtime')) {
        $base = Get-ChildPath $repo ('assets/' + $asset)
        $files = @(Get-PlainFiles $base -Recurse:($asset -ne 'mpv-runtime'))
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
            $source = 'assets/' + $asset + '/' + $relative
            if ($asset -eq 'mpv-runtime' -and $file.Extension -ieq '.dll') {
                if ($file.Name -cne 'vulkan-1.dll') { Fail-Payload "夹具来源存在未批准原生载荷：$($file.Name)" }
                Assert-FileBytes $file.FullName $vulkan '夹具批准 Vulkan 来源'
            }
            if ($asset -eq 'mpv-runtime' -and $file.Extension -notin @('.txt', '.md')) { continue }
            $null = $sources.Add($source)
            if ($asset -eq 'mpv-ui' -and $file.Extension -ine '.md') {
                Add-Shipping $shipping ('mpv-ui/' + $relative) $source
            }
            if ($asset -eq 'mpv-runtime' -and $file.Extension -ieq '.txt') {
                Add-Shipping $shipping $relative $source
            }
        }
    }
    foreach ($source in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'src/Momoka.Shell/Momoka.Shell.csproj',
        'src/Momoka.Core/Momoka.Core.csproj', 'Directory.Build.props', 'global.json')) { $null = $sources.Add($source) }
    foreach ($source in $sources) {
        $from = Get-ChildPath $repo $source
        if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { Fail-Payload "夹具来源缺失：$source" }
        $copies.Add([pscustomobject]@{ source=$from; target=(Get-ChildPath $repository $source) })
    }
    $payloadCopyPlan = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($manifest.native) + @($manifest.files)) {
        $null = Get-ChildPath $repo $entry.source
        $null = Get-ChildPath $repository $entry.source
        $payloadCopyPlan.Add([pscustomobject]@{ source=(Get-ChildPath $repository $entry.source); target=(Get-ChildPath $publish $entry.path) })
        if (-not $shipping.ContainsKey($entry.path) -or $shipping[$entry.path] -cne $entry.source) {
            Fail-Payload "夹具清单路径/来源不属于 shipping inclusion：$($entry.path)"
        }
    }
    foreach ($path in $shipping.Keys) {
        if (@(@($manifest.native) + @($manifest.files) | Where-Object { $_.path -ceq $path }).Count -ne 1) {
            Fail-Payload "夹具清单遗漏 shipping inclusion：$path"
        }
    }
    foreach ($entry in @($manifest.files) + @($manifest.evidence)) {
        Assert-FileBytes (Get-ChildPath $repo $entry.source) $entry ('夹具批准来源 ' + $entry.source)
        $null = Get-ChildPath $repository $entry.source
    }
    foreach ($relative in @('assets/mpv-runtime/approved-payload.json', 'libmpv-2.dll',
        'assets/mpv-runtime/vulkan-1.dll', 'src/Momoka.Shell/obj/project.assets.json')) {
        $null = Get-ChildPath $repository $relative
    }
    $dependencyPlan = @(New-MpvDependencyFixturePlan $repo $repository $publish)
    # 全量 preflight 已完成；复制固定计划的单文件，避免递归 Copy 随链接跨界。
    foreach ($copy in $copies) {
        Assert-PlainPath $copy.source
        Assert-PlainPath $copy.target
        [IO.Directory]::CreateDirectory((Split-Path -Parent $copy.target)) | Out-Null
        Copy-Item -LiteralPath $copy.source -Destination $copy.target
    }
    [IO.Directory]::CreateDirectory($publish) | Out-Null
    # 固定的、醒目标注的非 PE 合成字节与固定合成批准 hash。不得对任意供应 DLL 自动计算批准值。
    $libBytes = [Convert]::FromBase64String('UjA2NyBvZmZsaW5lIGFwcHJvdmVkIGxpYm1wdiBzeW50aGV0aWMgcGF5bG9hZCB2MTsgbmV2ZXIgbG9hZCBuYXRpdmUuCg==')
    $vulkanBytes = [Convert]::FromBase64String('UjA2NyBvZmZsaW5lIGFwcHJvdmVkIHZ1bGthbiBzeW50aGV0aWMgcGF5bG9hZCB2MTsgbmV2ZXIgbG9hZCBuYXRpdmUuCg==')
    $replacement = [ordered]@{
        $lib.sha256 = 'd6374aff98695bac2538b24b1708806cd626202c2c1786212784e88aa62e9dd0'
        $vulkan.sha256 = 'ae9aeaf2111769aa745ae78432a716f47256a4d14eea889a1dc908fcfe20b3e3'
        $lib.version = 'mpv v0.0.0-r067-fixture'
        $vulkan.version = '0.0.0.67'
        $lib.release = 'r067-offline-fixture-v1'
        $lib.asset = 'r067-offline-fixture-v1.txt'
        $lib.archiveSha256 = 'ecc03910db2f181665a30d98cf1ba2e7263ad5944bdf93f6885325011ee0e6d9'
        '101,242,880' = '70'
        '581,120' = '70'
    }
    $encoding = [Text.UTF8Encoding]::new($false)
    foreach ($name in @('assets\mpv-runtime\README.md', 'assets\mpv-runtime\NOTICE.txt', 'THIRD-PARTY-NOTICES.md')) {
        $path = Join-Path $repository $name
        $text = [IO.File]::ReadAllText($path)
        foreach ($pair in $replacement.GetEnumerator()) { $text = $text.Replace($pair.Key, $pair.Value) }
        $text = "R067 OFFLINE SYNTHETIC FIXTURE -- NOT A REAL NATIVE/LGPL APPROVAL.`n`n" + $text
        [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"), $encoding)
    }
    foreach ($entry in @($lib, $vulkan)) {
        $entry.sha256 = $replacement[$entry.sha256]
        $entry.version = $replacement[$entry.version]
        $entry.size = 70
    }
    foreach ($field in @('release', 'asset', 'archiveSha256')) { $lib.$field = $replacement[$lib.$field] }
    $manifest.approvalId = 'mpv-lgpl-' + $lib.release
    [IO.File]::WriteAllBytes((Join-Path $repository 'libmpv-2.dll'), $libBytes)
    [IO.File]::WriteAllBytes((Join-Path $repository 'assets\mpv-runtime\vulkan-1.dll'), $vulkanBytes)
    foreach ($entry in @($manifest.files) + @($manifest.evidence)) {
        $path = Join-Path $repository $entry.source.Replace('/', '\')
        $entry.size = (Get-Item -LiteralPath $path).Length
        $entry.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $manifestPath = Join-Path $repository 'assets\mpv-runtime\approved-payload.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8).Replace("`r`n", "`n") + "`n", $encoding)
    foreach ($copy in $payloadCopyPlan) {
        Assert-PlainPath $copy.source
        Assert-PlainPath $copy.target
        [IO.Directory]::CreateDirectory((Split-Path -Parent $copy.target)) | Out-Null
        Copy-Item -LiteralPath $copy.source -Destination $copy.target
    }
    $shaderSource = Get-ChildPath $repository 'assets/shaders'
    foreach ($file in @(Get-PlainFiles $shaderSource -Recurse)) {
        $target = Get-ChildPath $publish ('shaders/' + $file.FullName.Substring($shaderSource.Length + 1).Replace('\', '/'))
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    foreach ($name in @('Momoka.exe', 'Momoka.runtimeconfig.json', 'Momoka.pri',
        'Microsoft.WindowsAppRuntime.Bootstrap.dll', 'hostfxr.dll')) {
        [IO.File]::WriteAllText((Join-Path $publish $name), 'R067 offline host fixture themeresources -- never launch', $encoding)
    }
    foreach ($write in $dependencyPlan) {
        Assert-PlainPath $write.target
        [IO.Directory]::CreateDirectory((Split-Path -Parent $write.target)) | Out-Null
        [IO.File]::WriteAllBytes($write.target,$write.bytes)
    }
    [pscustomobject]@{ repository=$repository; publish=$publish; manifest=$manifestPath; fixture=$fixture; nativeIsSynthetic=$true }
}

function Invoke-MpvPublishFixture([string]$Check, [string]$RepositoryRoot, [string]$PublishRoot, [switch]$SelfContained) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Check, '-PublishRoot', $PublishRoot, '-RepositoryRoot', $RepositoryRoot)
        if ($SelfContained) { $arguments += '-SelfContained' }
        $lines = @(& powershell @arguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    [pscustomobject]@{ exitCode=$code; output=($lines | Out-String); executable='powershell'; arguments=$arguments }
}
