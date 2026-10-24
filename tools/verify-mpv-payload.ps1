[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

# 只验收 Momoka 随包的 mpv 载荷，不参与外部 mpv 或用户 LGPL 替换的运行期选择。
# approved-payload.json 是经审阅的固定期望；不得从待供应 DLL 自动更新批准值。
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')

$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\')
$publish = (Resolve-Path -LiteralPath $PublishRoot).Path.TrimEnd('\')
Assert-PlainPath $repository
Assert-PlainPath $publish
$manifest = Read-Manifest (Get-ChildPath $repository 'assets/mpv-runtime/approved-payload.json')

$native = Assert-NativeManifest $manifest
$lib = $native['libmpv-2.dll']
$vulkan = $native['vulkan-1.dll']

$projectPath = Get-ChildPath $repository 'src/Momoka.Shell/Momoka.Shell.csproj'
$project = [xml]([IO.File]::ReadAllText($projectPath))
Assert-ShippingItem $project '..\..\assets\mpv-ui\**\*.*' 'mpv-ui\%(RecursiveDir)%(Filename)%(Extension)' '..\..\assets\mpv-ui\**\*.md'
Assert-ShippingItem $project '..\..\assets\mpv-runtime\*.dll;..\..\assets\mpv-runtime\*.txt' '%(Filename)%(Extension)'
Assert-ShippingItem $project '..\..\libmpv-2.dll' 'libmpv-2.dll' '' "Exists('..\..\libmpv-2.dll')"
Assert-ShippingItem $project '..\..\LICENSE' 'LICENSE'
Assert-ShippingItem $project '..\..\THIRD-PARTY-NOTICES.md' 'THIRD-PARTY-NOTICES.md'

$shipping = @{}
Add-Shipping $shipping 'libmpv-2.dll' 'libmpv-2.dll'
Add-Shipping $shipping 'LICENSE' 'LICENSE'
Add-Shipping $shipping 'THIRD-PARTY-NOTICES.md' 'THIRD-PARTY-NOTICES.md'
$uiSource = Get-ChildPath $repository 'assets/mpv-ui'
foreach ($file in @(Get-PlainFiles $uiSource -Recurse | Where-Object { $_.Extension -ine '.md' })) {
    $relative = $file.FullName.Substring($uiSource.Length + 1).Replace('\', '/')
    Add-Shipping $shipping ('mpv-ui/' + $relative) ('assets/mpv-ui/' + $relative)
}
$runtimeSource = Get-ChildPath $repository 'assets/mpv-runtime'
foreach ($file in @(Get-PlainFiles $runtimeSource | Where-Object { $_.Extension -in @('.dll', '.txt') })) {
    Add-Shipping $shipping $file.Name ('assets/mpv-runtime/' + $file.Name)
}
$expected = @{}
foreach ($entry in @($manifest.native) + @($manifest.files)) {
    if (-not $shipping.ContainsKey($entry.path) -or $shipping[$entry.path] -cne $entry.source) {
        Fail-Payload "清单路径/来源不属于 Shell shipping inclusion：$($entry.path) -> $($entry.source)"
    }
    $expected[$entry.path] = $entry
}
foreach ($path in $shipping.Keys) {
    if (-not $expected.ContainsKey($path)) { Fail-Payload "Shell shipping inclusion 出现未批准载荷：$path" }
}
# 不只对照发布件：源树的许可/UI/Vulkan 与批准清单也须相符，不能用缺模块或已漂移来源刷新期望。
# libmpv 不入库；源 DLL 缺失时仍按固定批准 hash 验发布字节，不下载、不借库，也不降低门禁。
foreach ($entry in @($manifest.files) + @($manifest.evidence) + @($vulkan)) {
    Assert-FileBytes (Get-ChildPath $repository $entry.source) $entry ('批准来源 ' + $entry.source)
}
if (Test-Path -LiteralPath (Get-ChildPath $repository $lib.source) -PathType Leaf) {
    Assert-FileBytes (Get-ChildPath $repository $lib.source) $lib '供应 libmpv-2.dll'
}

$notice = [IO.File]::ReadAllText((Get-ChildPath $repository 'assets/mpv-runtime/NOTICE.txt'))
$readme = [IO.File]::ReadAllText((Get-ChildPath $repository 'assets/mpv-runtime/README.md'))
$thirdParty = [IO.File]::ReadAllText((Get-ChildPath $repository 'THIRD-PARTY-NOTICES.md'))
$libNotice = [regex]::Match($notice, '(?ms)^libmpv-2\.dll.*?(?=^vulkan-1\.dll|\z)').Value
$vulkanNotice = [regex]::Match($notice, '(?ms)^vulkan-1\.dll.*?(?=^License text sources|\z)').Value
$libReadme = [regex]::Match($readme, '(?ms)^## 播放内核本身.*?(?=^## |\z)').Value
$vulkanReadme = [regex]::Match($readme, '(?ms)^\| 项 \| 值 \|.*?(?=^## |\z)').Value
$libThird = [regex]::Match($thirdParty, '(?ms)^### mpv / libmpv.*?(?=^### |\z)').Value
$vulkanThird = [regex]::Match($thirdParty, '(?ms)^### vulkan-1\.dll.*?(?=^---|^### |\z)').Value
$sizeText = $lib.size.ToString('N0', [Globalization.CultureInfo]::InvariantCulture)
foreach ($entry in @($lib, $vulkan)) {
    $block = if ($entry.path -ceq 'libmpv-2.dll') { $libNotice } else { $vulkanNotice }
    $versionSuffix = if ($entry.path -ceq 'libmpv-2.dll') { '\r?$' } else { ' \([^()\r\n]+\)\r?$' }
    Assert-Declaration $block ('(?m)^  Version: +'+ [regex]::Escape($entry.version) + $versionSuffix) ($entry.path + ' version')
    $prefix = if ($entry.path -ceq 'libmpv-2.dll') { '  File: +SHA256 ' } else { '  SHA256: +' }
    Assert-Declaration $block ('(?m)^' + $prefix + $entry.sha256 + '\r?$') ($entry.path + ' SHA256')
    foreach ($license in $entry.licenses) {
        Assert-Declaration $notice ([regex]::Escape($license)) ($entry.path + ' LICENSE/NOTICE 索引')
        if (-not $expected.ContainsKey($license)) { Fail-Payload "清单缺少原生许可正文：$license" }
    }
}
Assert-Declaration $libNotice ('(?m)^ +'+ [regex]::Escape($sizeText) + ' bytes\r?$') 'libmpv size'
Assert-Declaration $libNotice ('(?m)^  Release: +tag ' + [regex]::Escape($lib.release) + ', asset\r?$') 'libmpv release'
Assert-Declaration $libNotice ('(?m)^ +' + [regex]::Escape($lib.asset) + '\r?$') 'libmpv release asset'
Assert-Declaration $libNotice ('(?m)^  Archive: +SHA256 ' + $lib.archiveSha256 + '\r?$') 'libmpv archive SHA256'
Assert-Declaration $libNotice '(?m)^  License: +LGPL-2\.1-or-later \(mpv itself\), statically linked against an LGPL-3\.0\r?$' 'libmpv LGPL 许可归属'
Assert-Declaration $libNotice '-Dgpl=false' 'libmpv LGPL 构建声明'
Assert-Declaration $vulkanNotice '(?m)^  License: +Apache-2\.0\. See LICENSE\.Apache-2\.0\.txt\.\r?$' 'Vulkan Apache 许可归属'
$noticeIndex = @{
    'LICENSE.LGPL-2.1.txt' = 'libmpv-2.dll, the mpv code'
    'LICENSE.LGPL-3.0.txt' = 'libmpv-2.dll, the FFmpeg it links statically'
    'LICENSE.GPL-3.0.txt' = 'referenced by the LGPL-3.0 text above'
    'LICENSE.Apache-2.0.txt' = 'vulkan-1.dll'
}
foreach ($license in $noticeIndex.Keys) {
    Assert-Declaration $notice ('(?m)^ +' + [regex]::Escape($license) + ' +' + [regex]::Escape($noticeIndex[$license]) + '\r?$') ('LICENSE/NOTICE 归属索引 ' + $license)
}

foreach ($value in @($lib.version, $lib.release, $lib.asset, $lib.archiveSha256, $lib.sha256, $sizeText)) {
    Assert-Declaration $libThird ([regex]::Escape($value)) 'THIRD-PARTY-NOTICES libmpv 批准索引'
}
Assert-Declaration $libReadme ('(?m)^\| 文件 SHA256 \| `' + $lib.sha256 + '` \|\r?$') 'README libmpv SHA256'
Assert-Declaration $libReadme ('(?m)^\| 大小 \| ' + [regex]::Escape($sizeText) + ' 字节') 'README libmpv size'
Assert-Declaration $libReadme ('(?m)^\| 版本 \| `' + [regex]::Escape($lib.version) + '`（提交 ') 'README libmpv version'
Assert-Declaration $libReadme ('(?m)^\| 出处 \| zhongfly/mpv-winbuild 的 release `' + [regex]::Escape($lib.release) + '`，资产 `' + [regex]::Escape($lib.asset) + '` \|\r?$') 'README libmpv release/asset'
Assert-Declaration $libThird ('(?m)^\| 版本 \| ' + [regex]::Escape($lib.version) + '（提交 ') 'THIRD-PARTY-NOTICES libmpv version'
Assert-Declaration $vulkanReadme ('(?m)^\| 版本 \| ' + [regex]::Escape($vulkan.version) + '（') 'README Vulkan version'
Assert-Declaration $vulkanThird ('(?m)^\| 版本 \| ' + [regex]::Escape($vulkan.version) + '（') 'THIRD-PARTY-NOTICES Vulkan version'

Assert-Declaration $vulkanReadme ('(?m)^\| SHA256 \| `' + $vulkan.sha256 + '` \|\r?$') 'README Vulkan SHA256'
Assert-Declaration $vulkanReadme ('(?m)^\| 大小 \| ' + [regex]::Escape($vulkan.size.ToString('N0', [Globalization.CultureInfo]::InvariantCulture)) + ' 字节 \|\r?$') 'README Vulkan size'


foreach ($entry in @($manifest.native) + @($manifest.files)) {
    Assert-FileBytes (Get-ChildPath $publish $entry.path) $entry ('发布载荷 ' + $entry.path)
}
$uiRoot = Get-ChildPath $publish 'mpv-ui'
foreach ($file in @(Get-PlainFiles $uiRoot -Recurse)) {
    $relative = 'mpv-ui/' + $file.FullName.Substring($uiRoot.Length + 1).Replace('\', '/')
    if (-not $expected.ContainsKey($relative)) { Fail-Payload "出现清单之外的载荷：$relative" }
}
# 待验 deps 不授予白名单；信任当前树固定 restore/selected TFM-RID/包归档与 Release 编译输出。
. (Join-Path $PSScriptRoot 'mpv-payload-dependencies.ps1')
$dependencySources = Assert-TrustedDependencies $repository $publish $project -SelfContained:$SelfContained
foreach ($file in @(Get-PlainFiles $publish)) {
    if ($expected.ContainsKey($file.Name)) { continue }
    if ($file.Extension -ieq '.txt' -or $file.Name -match '^(LICENSE|.*NOTICE|(?:lib)?mpv|vulkan|lua).*') {
        Fail-Payload "出现清单之外的运行时载荷：$($file.Name)"
    }
}
[pscustomobject]@{ approvalId=$manifest.approvalId; nativeCount=$manifest.native.Count; uiCount=@($manifest.files | Where-Object { $_.path.StartsWith('mpv-ui/') }).Count; fileCount=$expected.Count }
