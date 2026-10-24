# downloadDependencies 只固定名称/版本，内容批准来自 repo 固定清单，绝不从待验 cache/metadata/ZIP 建 pin。
function Read-RuntimepackApproval([string]$Repository,[string]$Tfm,[string]$Rid) {
    $path=Get-ChildPath $Repository 'assets/mpv-runtime/approved-runtimepack.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail-Payload '缺少独立 runtimepack 批准清单。' }
    try {
        $text=[Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
        # 与 native 清单相同的 JSON 原始字段守卫，包括转义/大小写重复；不是最后一个值胜出。
        Add-Type -AssemblyName System.Runtime.Serialization
        $reader=[Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader([Text.Encoding]::UTF8.GetBytes($text),[Xml.XmlDictionaryReaderQuotas]::Max)
        try {$document=[Xml.XmlDocument]::new();$document.Load($reader)} finally {$reader.Dispose()}
        foreach ($object in $document.SelectNodes('//*[@type="object"]')) {
            $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($child in $object.ChildNodes) {
                $name=if($child.NamespaceURI -eq 'item'){$child.GetAttribute('item')}else{$child.LocalName}
                if (-not $names.Add($name)) { Fail-Payload "runtimepack 清单重复 JSON 字段：$name" }
            }
        }
        $pin=ConvertFrom-Json -InputObject $text
    } catch { Fail-Payload "runtimepack 批准清单 JSON 无效：$($_.Exception.Message)" }
    Assert-Fields $pin @('schemaVersion','approvalId','sdkVersion','targetFramework','runtimeIdentifier','packageId','packageVersion','contentHash','archive','assets') 'runtimepack 批准清单'
    if (($pin.schemaVersion -isnot [int] -and $pin.schemaVersion -isnot [long]) -or $pin.schemaVersion -ne 1) { Fail-Payload 'runtimepack schemaVersion 无效。' }
    foreach ($field in @('approvalId','sdkVersion','targetFramework','runtimeIdentifier','packageId','packageVersion','contentHash')) {
        if ($pin.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($pin.$field)) { Fail-Payload "runtimepack $field 必须是非空标量字符串。" }
    }
    $sdk=(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Get-ChildPath $Repository 'global.json')))).sdk.version
    if ($sdk -isnot [string] -or -not [string]::Equals($pin.sdkVersion,$sdk,[StringComparison]::Ordinal) -or
        -not [string]::Equals($pin.targetFramework,$Tfm,[StringComparison]::Ordinal) -or
        -not [string]::Equals($pin.runtimeIdentifier,$Rid,[StringComparison]::Ordinal)) { Fail-Payload 'runtimepack 批准 SDK/TFM/RID 与当前项目不符。' }
    if ($pin.packageId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9.]*$' -or $pin.packageVersion -cnotmatch '^[0-9]+(\.[0-9]+){2,3}$' -or
        $pin.contentHash -cnotmatch '^[A-Za-z0-9+/]{86}==$') { Fail-Payload 'runtimepack 包身份/内容摘要格式无效。' }
    try {$hashBytes=[Convert]::FromBase64String($pin.contentHash)} catch { Fail-Payload 'runtimepack 内容摘要不是 SHA512。' }
    if ($hashBytes.Length -ne 64 -or -not [string]::Equals([Convert]::ToBase64String($hashBytes),$pin.contentHash,[StringComparison]::Ordinal)) { Fail-Payload 'runtimepack 内容摘要不是规范 SHA512。' }
    Assert-Fields $pin.archive @('source','size','sha256') 'runtimepack 批准归档出处'
    if ($pin.archive.source -isnot [string] -or [string]::IsNullOrWhiteSpace($pin.archive.source) -or
        ($pin.archive.size -isnot [int] -and $pin.archive.size -isnot [long]) -or $pin.archive.size -le 0 -or
        $pin.archive.sha256 -isnot [string] -or $pin.archive.sha256 -cnotmatch '^[0-9a-f]{64}$') { Fail-Payload 'runtimepack 批准归档出处/字节索引无效。' }
    if ($pin.assets -isnot [array] -or $pin.assets.Count -eq 0) { Fail-Payload 'runtimepack 批准资产必须是非空数组。' }
    $paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sources=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $pin.assets) {
        Assert-Fields $asset @('kind','path','source') 'runtimepack 批准资产'
        Assert-RelativePath $asset.path
        Assert-RelativePath $asset.source
        if ($asset.kind -isnot [string] -or $asset.kind -cne 'runtime' -or $asset.path -cnotmatch '^[^/]+\.dll$' -or
            $asset.source -cnotmatch '^lib/net[0-9.]+/[^/]+\.dll$' -or [IO.Path]::GetFileName($asset.source) -cne $asset.path) { Fail-Payload 'runtimepack 批准资产映射无效。' }
        if (-not $paths.Add($asset.path) -or -not $sources.Add($asset.source)) { Fail-Payload 'runtimepack 批准资产路径/来源重复。' }
    }
    return $pin
}

# 只读信任输入：当前树 restore 的单一 TFM/RID + 固定包归档 + 当前项目 Release 输出。
# PublishRoot/Momoka.deps.json 是待验数据，任何 target/library/path 都不能自行授权 extra DLL。
function Assert-TrustedDependencies([string]$Repository, [string]$Publish, $Project, [switch]$SelfContained) {
    $tfms = @($Project.SelectNodes('/Project/PropertyGroup/TargetFramework') | ForEach-Object { $_.InnerText } | Select-Object -Unique)
    $rids = @($Project.SelectNodes('/Project/PropertyGroup/RuntimeIdentifier') | ForEach-Object { $_.InnerText } | Select-Object -Unique)
    if ($tfms.Count -ne 1 -or $rids.Count -ne 1 -or $rids[0] -cne 'win-x64') { Fail-Payload '可信构建 TFM/RID 不唯一或不是 win-x64。' }
    $tfm=$tfms[0]; $rid=$rids[0]
    $assetsPath=Get-ChildPath $Repository 'src/Momoka.Shell/obj/project.assets.json'
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) { Fail-Payload '缺少当前树可信 restore 资产；不能用待验 deps 建立白名单。' }
    $assets=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($assetsPath))
    $selected=$assets.targets.($tfm+'/'+$rid)
    $projectPath=Get-ChildPath $Repository 'src/Momoka.Shell/Momoka.Shell.csproj'
    if ($selected -isnot [pscustomobject] -or $assets.project.restore.projectPath -cne $projectPath) {
        Fail-Payload '可信 restore 项目/selected target 不符。'
    }
    $folderNames=@($assets.packageFolders.PSObject.Properties.Name)
    if ($folderNames.Count -ne 1) { Fail-Payload '可信 restore 包根必须唯一。' }
    $packages=[IO.Path]::GetFullPath($folderNames[0]).TrimEnd('\')
    Assert-PlainPath $packages
    # 默认 restore 的全局 NuGet cache 也可用：只读其 pinned restore/归档，不向该目录写入或联网。
    # 固定 project references 对上 restore，不接受待验 deps 自己给的依赖或版本。
    foreach ($reference in $Project.SelectNodes('/Project/ItemGroup/PackageReference')) {
        $id=$reference.GetAttribute('Include'); $version=$reference.GetAttribute('Version').Trim('[',']')
        if (-not $selected.PSObject.Properties[($id+'/'+$version)]) { Fail-Payload "可信 restore 与当前固定 NuGet 引用不一致：$id/$version" }
    }
    $assembly=[string]($Project.SelectSingleNode('/Project/PropertyGroup/AssemblyName').InnerText)
    if ([string]::IsNullOrWhiteSpace($assembly)) { Fail-Payload '缺少可信项目程序集名。' }
    $output=Get-ChildPath $Repository ('src/Momoka.Shell/bin/x64/Release/'+$tfm+'/'+$rid)
    $trustedDepsPath=Get-ChildPath $output ($assembly+'.deps.json')
    if (-not (Test-Path -LiteralPath $trustedDepsPath -PathType Leaf)) { Fail-Payload '缺少当前 Release 可信构建 deps；不能借待验数据放行。' }
    $trustedDeps=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($trustedDepsPath))
    $active=$trustedDeps.runtimeTarget.name
    if ($tfm -cnotmatch '^net(?<version>[0-9]+\.[0-9]+)(?:-|$)') { Fail-Payload '可信项目 TFM 无法映射。' }
    $expectedTarget='.NETCoreApp,Version=v'+$Matches.version+'/'+$rid
    $props=[xml]([IO.File]::ReadAllText((Get-ChildPath $Repository 'Directory.Build.props')))
    $version=$props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if ($active -isnot [string] -or -not [string]::Equals($active,$expectedTarget,[StringComparison]::Ordinal) -or
        $trustedDeps.targets.$active -isnot [pscustomobject]) { Fail-Payload '可信构建 deps selected target/RID 不符。' }
    . (Join-Path $PSScriptRoot 'mpv-nuget-content-hash.ps1')
    $packageCache=@{}
    $downloadRecords=@{}
    $sources=@{}
    $keys=@{}
    function Get-Package([string]$Library) {
        if ($packageCache.ContainsKey($Library)) { return $packageCache[$Library] }
        $record=if ($downloadRecords.ContainsKey($Library)) { $downloadRecords[$Library] } else { $assets.libraries.$Library }
        if ($record.type -cne 'package') { Fail-Payload "不属于可信包：$Library" }
        $directory=Get-ChildPath $packages $record.path
        $id=($Library -split '/')[0].ToLowerInvariant(); $version=($Library -split '/')[1]
        $archive=Get-ChildPath $directory ($id+'.'+$version+'.nupkg')
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { Fail-Payload "可信固定包归档缺失：$Library" }
        $algorithm=[Security.Cryptography.SHA512]::Create()
        try { $stream=[IO.File]::OpenRead($archive); try { $hash=[Convert]::ToBase64String($algorithm.ComputeHash($stream)) } finally { $stream.Dispose() } } finally { $algorithm.Dispose() }
        # signed-package contentHash 与原始 nupkg 的整文件 SHA512 口径不同，保持两道校验。
        # metadata.source 只记录还原来源（正常值是 nuget.org URL），不是可据此读盘的 fixed-feed 路径，
        # 更不授予 DLL 白名单。普通包批准来自固定 restore，runtimepack 来自独立固定 pin；缓存归档/ZIP entry/实际编译资产只接受比对。
        $metadataPath=Get-ChildPath $packages ($record.path+'/.nupkg.metadata')
        $metadata=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($metadataPath))
        $rawHash=[IO.File]::ReadAllText(($archive+'.sha512')).Trim()

        if ($metadata.contentHash -cne $record.sha512 -or $hash -cne $rawHash -or
            $metadata.source -isnot [string] -or [string]::IsNullOrWhiteSpace($metadata.source)) {
            Fail-Payload "可信包内容/归档与当前 restore 不符：$Library"
        }
        $actualContentHash=Get-MpvNuGetPackageContentHash $archive
        if (-not [string]::Equals($actualContentHash,$record.sha512,[StringComparison]::Ordinal)) {
            $basis=if($downloadRecords.ContainsKey($Library)){'独立 runtimepack 批准'}else{'固定 restore'}
            Fail-Payload "NuGet 实际包内容摘要与$basis 不符：$Library"
        }
        if ($downloadRecords.ContainsKey($Library)) {
            if ((Get-Item -LiteralPath $archive -Force).Length -ne $record.archivePin.size -or
                (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.archivePin.sha256) {
                Fail-Payload "runtimepack 实际归档与独立批准字节索引不符：$Library"
            }
        }
        $package=[pscustomobject]@{ directory=$directory; files=$record.files; archive=$archive }
        $packageCache[$Library]=$package
        return $package
    }
    function Add-Dependency([string]$Name,[string]$Source,[string]$AssetKey='') {
        Assert-RelativePath $Name
        Assert-PlainPath $Source
        if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { Fail-Payload "可信构建/包资产缺失：$Source" }
        if ($sources.ContainsKey($Name) -and $sources[$Name] -cne $Source) {
            if ((Get-FileHash -LiteralPath $sources[$Name] -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash) {
                Fail-Payload "可信 DLL 同名不同字节：$Name"
            }
        } else { $sources[$Name]=$Source }
        if ($AssetKey) { $keys[$AssetKey]=$true }
    }
    function Add-PackageAsset([string]$Library,[string]$Relative,[string]$AssetKey='') {
        Assert-RelativePath $Relative
        $package=Get-Package $Library
        if ($package.files -cnotcontains $Relative) { Fail-Payload "资产不在固定包索引：$Library/$Relative" }
        $source=Get-ChildPath $package.directory $Relative
        # 恢复目录也不是任意供应 DLL：逐资产与已固定归档 entry 的字节对上，不能只信 basename/文件大小。
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip=[IO.Compression.ZipFile]::OpenRead($package.archive)
        try {
            $entry=$zip.GetEntry($Relative)
            if ($null -eq $entry) { Fail-Payload "固定包归档缺少资产：$Library/$Relative" }
            $algorithm=[Security.Cryptography.SHA256]::Create()
            try { $stream=$entry.Open(); try { $entryHash=[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() } } finally { $algorithm.Dispose() }
            if ($entryHash -cne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) { Fail-Payload "恢复资产与固定归档字节不符：$Library/$Relative" }
        } finally { $zip.Dispose() }
        Add-Dependency ([IO.Path]::GetFileName($Relative)) $source $AssetKey
    }
    # 可信构建 selected deps 的每个资产还须由 selected restore 包/项目输出解释，不能信任任意同名 DLL。
    foreach ($library in $trustedDeps.targets.$active.PSObject.Properties) {
        $selectedLibrary=$selected.($library.Name)
        foreach ($kind in @('runtime','native','runtimeTargets')) {
            foreach ($asset in $library.Value.$kind.PSObject.Properties) {
                if ($kind -eq 'runtimeTargets' -and $asset.Value.rid -cne $rid) { continue }
                $key=$library.Name+'|'+$kind+'|'+$asset.Name
                $name=[IO.Path]::GetFileName($asset.Name)
                if ($selectedLibrary.type -ceq 'package') {
                    if (-not $selectedLibrary.$kind.PSObject.Properties[$asset.Name]) { Fail-Payload "构建资产不属于 selected restore：$key" }
                    Add-PackageAsset $library.Name $asset.Name $key
                } elseif ($selectedLibrary.type -ceq 'project') {
                    $sourceProject=[IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $assets.libraries.($library.Name).msbuildProject))
                    if (-not $sourceProject.StartsWith($Repository+'\',[StringComparison]::OrdinalIgnoreCase)) { Fail-Payload '可信 ProjectReference 越界。' }
                    $references=@($Project.SelectNodes('/Project/ItemGroup/ProjectReference') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $_.GetAttribute('Include'))) })
                    if ($references -cnotcontains $sourceProject) { Fail-Payload '不属于当前可信 ProjectReference。' }
                    $core=[xml]([IO.File]::ReadAllText($sourceProject)); $coreTfm=$core.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
                    $coreName=$core.SelectSingleNode('/Project/PropertyGroup/AssemblyName').InnerText
                    if ($name -cne ($coreName+'.dll')) { Fail-Payload "项目资产名不符：$key" }
                    Add-Dependency $name (Get-ChildPath (Split-Path -Parent $sourceProject) ('bin/Release/'+$coreTfm+'/'+$name)) $key
                } elseif ([string]::Equals($library.Name,($assembly+'/'+$version),[StringComparison]::Ordinal) -and $name -ceq ($assembly+'.dll')) {
                    Add-Dependency $name (Get-ChildPath $output $name) $key
                } elseif ($library.Name.StartsWith('Microsoft.Web.WebView2.Core.Projection/',[StringComparison]::Ordinal)) {
                    $webview=@($selected.PSObject.Properties | Where-Object { $_.Name -match '^Microsoft\.Web\.WebView2/' })
                    if ($webview.Count -ne 1 -or ($library.Name -split '/')[1] -cne ($webview[0].Name -split '/')[1]) { Fail-Payload 'WebView2 reference 与固定包版本不符。' }
                    Add-PackageAsset $webview[0].Name ('lib_manual/net8.0-windows10.0.17763.0/'+$name) $key
                } elseif ($library.Name.StartsWith('runtimepack.',[StringComparison]::Ordinal)) {
                    $pack=($library.Name.Substring(12) -split '/'); $downloads=@($assets.project.frameworks.$tfm.downloadDependencies)
                    if ($pack.Count -ne 2 -or @($downloads | Where-Object { $_.name -ceq $pack[0] -and [string]::Equals($_.version,('['+$pack[1]+', '+$pack[1]+']'),[StringComparison]::Ordinal) }).Count -ne 1) { Fail-Payload "未知可信 runtime pack：$($library.Name)" }
                    $packKey=$pack[0]+'/'+$pack[1]
                    if (-not $downloadRecords.ContainsKey($packKey)) {
                        $pin=Read-RuntimepackApproval $Repository $tfm $rid
                        if (-not [string]::Equals($pin.packageId,$pack[0],[StringComparison]::Ordinal) -or
                            -not [string]::Equals($pin.packageVersion,$pack[1],[StringComparison]::Ordinal)) { Fail-Payload "runtimepack 独立批准包/版本不符：$packKey" }
                        foreach ($approved in $pin.assets) {
                            if (-not $library.Value.($approved.kind).PSObject.Properties[$approved.path]) { Fail-Payload '可信 runtimepack 构建未完整索引批准资产。' }
                        }
                        $downloadRecords[$packKey]=[pscustomobject]@{
                            type='package';path=($pack[0].ToLowerInvariant()+'/'+$pack[1]);sha512=$pin.contentHash
                            files=@($pin.assets.source);assets=$pin.assets;archivePin=$pin.archive
                        }
                    }
                    $candidates=@($downloadRecords[$packKey].assets | Where-Object { $_.kind -ceq $kind -and $_.path -ceq $asset.Name })
                    if ($candidates.Count -ne 1) { Fail-Payload "runtimepack 构建资产不属于独立批准映射：$key" }
                    Add-PackageAsset $packKey $candidates[0].source $key
                } else { Fail-Payload "未知可信构建依赖：$key" }
            }
        }
    }
    # SDK targets 的内容复制也必须来自当前 selected 固定包，而非待验 deps.libraries 声明。
    foreach ($library in $selected.PSObject.Properties) {
        if ($library.Name -match '^Microsoft\.Web\.WebView2/') {
            Add-PackageAsset $library.Name ('runtimes/'+$rid+'/native_uap/Microsoft.Web.WebView2.Core.dll')
        }
        if ($SelfContained -and $library.Name -match '^Microsoft\.WindowsAppSDK\.(Foundation|InteractiveExperiences|WinUI|DWrite)/') {
            $package=Get-Package $library.Name
            foreach ($relative in $package.files | Where-Object { $_ -cmatch ('^runtimes-framework/'+$rid+'/native/[^/]+\.dll$') }) {
                Add-PackageAsset $library.Name $relative
            }
        }
    }
    $deps=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Get-ChildPath $Publish 'Momoka.deps.json')))
    if ($deps.targets -isnot [pscustomobject] -or $deps.targets.$active -isnot [pscustomobject]) {
        Fail-Payload '待验 deps 缺少可信 selected target 对象。'
    }
    if ($deps.runtimeTarget.name -isnot [string] -or -not [string]::Equals($deps.runtimeTarget.name,$active,[StringComparison]::Ordinal)) {
        Fail-Payload '待验 deps runtimeTarget/RID 与可信当前构建不符。'
    }
    foreach ($target in $deps.targets.PSObject.Properties) {
        if ($target.Name -cne $active) {
            if (-not $trustedDeps.targets.PSObject.Properties[$target.Name] -or @($target.Value.PSObject.Properties).Count -gt 0) { Fail-Payload "待验 deps 存在未知/未选 target：$($target.Name)" }
            continue
        }
        foreach ($library in $target.Value.PSObject.Properties) {
            if (-not $trustedDeps.targets.$active.PSObject.Properties[$library.Name]) { Fail-Payload "待验 deps 未批准依赖：$($library.Name)" }
            foreach ($kind in @('runtime','native','runtimeTargets')) {
                foreach ($asset in $library.Value.$kind.PSObject.Properties) {
                    $key=$library.Name+'|'+$kind+'|'+$asset.Name
                    if (-not $keys.ContainsKey($key) -or ($kind -eq 'runtimeTargets' -and $asset.Value.rid -cne $rid)) { Fail-Payload "待验 deps 未批准资产/RID：$key" }
                }
            }
        }
    }
    # 发布树里的每个 DLL 都核对完整相对位置；根目录的批准值不能放行 shaders/foo.dll 或旧内核副本。
    foreach ($file in @(Get-PlainFiles $Publish -Recurse)) {
        if ($file.Extension -ine '.dll') { continue }
        $relative=$file.FullName.Substring($Publish.Length+1).Replace('\','/')
        if ($relative -cin @('libmpv-2.dll','vulkan-1.dll')) { continue }
        if (-not $sources.ContainsKey($relative)) { Fail-Payload "出现清单之外的运行时载荷：$relative" }
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $sources[$relative] -Algorithm SHA256).Hash) {
            Fail-Payload "运行时载荷字节与可信固定包/当前编译资产不符：$relative"
        }
    }
    return $sources
}
