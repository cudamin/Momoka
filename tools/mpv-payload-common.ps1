# 共用只读清单/路径验收。fixture 必须完成全部 preflight 后才写入；生产 verifier 同规则拒绝别名和空索引。
function Fail-Payload([string]$Message) {
    throw "发布验证失败：批准 mpv 载荷：$Message"
}

function Assert-RelativePath($Path) {
    if ($Path -isnot [string] -or [string]::IsNullOrWhiteSpace($Path) -or
        $Path -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') {
        Fail-Payload "非法清单路径：$Path"
    }
    foreach ($part in $Path.Split('/')) {
        if ($part -in @('', '.', '..') -or $part.EndsWith('.') -or
            $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') {
            Fail-Payload "非法清单路径：$Path"
        }
    }
}

function Assert-PlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Fail-Payload "拒绝重解析路径：$current"
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-ChildPath([string]$Root, [string]$Relative) {
    Assert-RelativePath $Relative
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative.Replace('/', '\')))
    if (-not $path.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Fail-Payload "清单路径越界：$Relative"
    }
    Assert-PlainPath $path
    return $path
}

function Get-PlainFiles([string]$Directory, [switch]$Recurse) {
    Assert-PlainPath $Directory
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        Fail-Payload "缺少载荷目录：$Directory"
    }
    foreach ($child in @(Get-ChildItem -LiteralPath $Directory -Force)) {
        if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Fail-Payload "拒绝重解析载荷：$($child.FullName)"
        }
        if ($child.PSIsContainer) {
            if ($Recurse) { Get-PlainFiles $child.FullName -Recurse }
        } else {
            $child
        }
    }
}

function Assert-Fields($Record, [string[]]$Required, [string]$Description) {
    if ($null -eq $Record -or $Record -isnot [pscustomobject]) {
        Fail-Payload "$Description 必须是对象。"
    }
    $names = @($Record.PSObject.Properties.Name)
    foreach ($name in $names) {
        if ($Required -cnotcontains $name) { Fail-Payload "$Description 有未知字段：$name" }
    }
    foreach ($name in $Required) {
        if ($names -cnotcontains $name) { Fail-Payload "$Description 缺少字段：$name" }
    }
}

function Assert-BytesRecord($Record, [string]$Description) {
    Assert-RelativePath $Record.source
    if (($Record.size -isnot [int] -and $Record.size -isnot [long]) -or $Record.size -le 0) {
        Fail-Payload "$Description 的 size 必须是正整数。"
    }
    if ($Record.sha256 -isnot [string] -or $Record.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        Fail-Payload "$Description 的 sha256 必须是 64 位小写十六进制。"
    }
}

function Assert-FileBytes([string]$Path, $Record, [string]$Description) {
    Assert-PlainPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail-Payload "缺少 $Description：$Path"
    }
    if ((Get-Item -LiteralPath $Path -Force).Length -ne $Record.size) {
        Fail-Payload "$Description 字节大小与批准清单不一致：$Path"
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Record.sha256) {
        Fail-Payload "$Description SHA256 与批准清单不一致：$Path"
    }
}

function Read-Manifest([string]$Path) {
    Assert-PlainPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail-Payload "缺少批准清单：$Path" }
    try {
        $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
        # ConvertFrom-Json 在不同 PS 版本对重复字段可能覆盖或报错。先用系统 JSON reader 保留原始字段，
        # 对每个对象检查解码后的名称（包括 p\u0061th），再进行类型/字段/路径检查；不接受最后一个值胜出。
        Add-Type -AssemblyName System.Runtime.Serialization
        $reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
            [Text.Encoding]::UTF8.GetBytes($text), [Xml.XmlDictionaryReaderQuotas]::Max)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.Load($reader)
        } finally { $reader.Dispose() }
        foreach ($object in $document.SelectNodes('//*[@type="object"]')) {
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($child in $object.ChildNodes) {
                $name = if ($child.NamespaceURI -eq 'item') { $child.GetAttribute('item') } else { $child.LocalName }
                if (-not $names.Add($name)) { Fail-Payload "清单重复 JSON 字段：$name" }
            }
        }
        $manifest = ConvertFrom-Json -InputObject $text
    } catch {
        Fail-Payload "批准清单 JSON 无效：$($_.Exception.Message)"
    }
    Assert-Fields $manifest @('schemaVersion', 'approvalId', 'native', 'files', 'evidence') '批准清单'
    if ($manifest.approvalId -isnot [string] -or [string]::IsNullOrWhiteSpace($manifest.approvalId)) {
        Fail-Payload '清单 approvalId 必须是非空标量字符串。'
    }
    if (($manifest.schemaVersion -isnot [int] -and $manifest.schemaVersion -isnot [long]) -or
        $manifest.schemaVersion -ne 1) { Fail-Payload '不支持的清单 schemaVersion。' }
    foreach ($group in @('native', 'files', 'evidence')) {
        if ($manifest.$group -isnot [array] -or $manifest.$group.Count -eq 0) {
            Fail-Payload "批准清单 $group 必须是非空数组。"
        }
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($manifest.native) + @($manifest.files)) {
        Assert-RelativePath $entry.path
        Assert-BytesRecord $entry $entry.path
        if (-not $paths.Add($entry.path)) { Fail-Payload "重复载荷路径：$($entry.path)" }
        if (-not $sources.Add($entry.source)) { Fail-Payload "重复来源路径：$($entry.source)" }
    }
    foreach ($entry in $manifest.files) {
        Assert-Fields $entry @('path', 'source', 'size', 'sha256') '载荷记录'
    }
    foreach ($entry in $manifest.evidence) {
        Assert-Fields $entry @('source', 'size', 'sha256') '批准证据'
        Assert-BytesRecord $entry '批准证据'
        if (-not $sources.Add($entry.source)) { Fail-Payload "重复来源路径：$($entry.source)" }
    }
    return $manifest
}

function Assert-ShippingItem($Project, [string]$Include, [string]$Link, [string]$Exclude = '', [string]$Condition = '') {
    $items = @($Project.SelectNodes('/Project/ItemGroup/None') | Where-Object { $_.GetAttribute('Include') -ceq $Include })
    if ($items.Count -ne 1 -or $items[0].GetAttribute('Exclude') -cne $Exclude -or
        $items[0].GetAttribute('Condition') -cne $Condition -or [string]$items[0].Link -cne $Link -or
        [string]$items[0].CopyToOutputDirectory -cne 'PreserveNewest') {
        Fail-Payload "Shell shipping inclusion 与批准清单边界不一致：$Include"
    }
}

function Add-Shipping($Map, [string]$Path, [string]$Source) {
    Assert-RelativePath $Path
    Assert-RelativePath $Source
    if ($Map.ContainsKey($Path)) { Fail-Payload "Shell shipping inclusion 路径重复：$Path" }
    $Map[$Path] = $Source
}

function Assert-Declaration([string]$Text, [string]$Pattern, [string]$Description) {
    if ($Text -cnotmatch $Pattern) { Fail-Payload "批准证据声明不一致：$Description" }
}

function Assert-NativeManifest($manifest) {
    $native = @{}
    foreach ($entry in $manifest.native) {
        $fields = @('path', 'source', 'size', 'sha256', 'version', 'licenses')
        if ($entry.path -ceq 'libmpv-2.dll') { $fields += @('release', 'asset', 'archiveSha256') }
        Assert-Fields $entry $fields '原生批准记录'
        if ($entry.path -cnotin @('libmpv-2.dll', 'vulkan-1.dll') -or
            $entry.version -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.version)) {
            Fail-Payload "未知原生批准记录或空版本：$($entry.path)"
        }
        $licenses = if ($entry.path -ceq 'libmpv-2.dll') {
            @('LICENSE.LGPL-2.1.txt', 'LICENSE.LGPL-3.0.txt', 'LICENSE.GPL-3.0.txt')
        } else { @('LICENSE.Apache-2.0.txt') }
        if ($entry.licenses -isnot [array] -or $entry.licenses.Count -ne $licenses.Count -or
            ($entry.licenses -join '|') -cne ($licenses -join '|')) {
            Fail-Payload "原生批准记录许可索引不一致：$($entry.path)"
        }
        $native[$entry.path] = $entry
    }
    if ($native.Count -ne 2 -or -not $native.ContainsKey('libmpv-2.dll') -or -not $native.ContainsKey('vulkan-1.dll')) {
        Fail-Payload '原生批准清单必须完整包含 libmpv-2.dll 与 vulkan-1.dll。'
    }
    $lib = $native['libmpv-2.dll']
    $vulkan = $native['vulkan-1.dll']
    foreach ($field in @('release', 'asset')) {
        if ($lib.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($lib.$field)) { Fail-Payload "空批准来源：$field" }
    }
    if ($lib.archiveSha256 -isnot [string] -or $lib.archiveSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        -not [string]::Equals($manifest.approvalId, ('mpv-lgpl-' + $lib.release), [StringComparison]::Ordinal)) { Fail-Payload '批准来源/归档索引不一致。' }
    if ($manifest.evidence.Count -ne 1 -or $manifest.evidence[0].source -cne 'assets/mpv-runtime/README.md') {
        Fail-Payload '批准证据必须完整索引 assets/mpv-runtime/README.md。'
    }
    return $native
}
