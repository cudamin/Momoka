[CmdletBinding()]
param(
    [string]$Phase = 'dotnet-runtimepack-content',
    [switch]$PublishedMappingOnly,
    [string]$PreviousChecker
)

$ErrorActionPreference = 'Stop'
if ($Phase -cnotmatch '^[a-z0-9-]{1,64}$') { throw 'Phase 只接受 1 至 64 个小写字母、数字和连字符。' }
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
Assert-PlainPath $repo
# 短路径兼容 WinPS 5.1/CodeDom 和既有 fixture 的深包路径；Phase 只写证据，不加长路径。
# 每次全新目录，既不清理旧输出，也不复用上一轮批准值或 fixture。
$run = Get-ChildPath $repo ('artifacts/dn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
if (Test-Path -LiteralPath $run) { throw '离线回归输出已存在，拒绝覆盖。' }
$encoding = [Text.UTF8Encoding]::new($true)
$assetEncoding = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
Assert-PlainPath $powershell
if (-not (Test-Path -LiteralPath $powershell -PathType Leaf)) { throw '缺少 Windows PowerShell 5.1，离线停止。' }

function Assert-FixturePath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($absolute -cne $run -and -not $absolute.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "离线 fixture 写入/变异目标越界：$absolute"
    }
    Assert-PlainPath $absolute
    # Common 检查重解析；额外拒绝所有已存在但不是目录的祖先，不能把文件当父目录。
    $ancestor = [IO.Path]::GetDirectoryName($absolute)
    while (-not [string]::IsNullOrEmpty($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and -not (Test-Path -LiteralPath $ancestor -PathType Container)) {
            throw "离线 fixture 已存在祖先不是目录：$ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $absolute
}
function Fixture-Path([string]$Root, [string]$Relative) {
    return Assert-FixturePath (Get-ChildPath $Root $Relative)
}
function New-FixtureDirectory([string]$Path, [switch]$Fresh) {
    $target = Assert-FixturePath $Path
    if ($Fresh -and (Test-Path -LiteralPath $target)) { throw "拒绝覆盖已有 fixture 目录：$target" }
    if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $target -PathType Container)) {
        throw "fixture 目录目标是文件：$target"
    }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $null = Assert-FixturePath $target
}
function Write-FixtureBytes([string]$Path, [byte[]]$Bytes, [switch]$New) {
    $target = Assert-FixturePath $Path
    New-FixtureDirectory ([IO.Path]::GetDirectoryName($target))
    $null = Assert-FixturePath $target
    $mode = if ($New) { [IO.FileMode]::CreateNew } else { [IO.FileMode]::Create }
    $stream = [IO.File]::Open($target, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}
function Write-FixtureText([string]$Path, [string]$Text, [switch]$New) {
    $lf = $Text.Replace([string][char]13, '').TrimEnd([char]10) + [char]10
    $bytes = [byte[]]($encoding.GetPreamble() + $encoding.GetBytes($lf))
    Write-FixtureBytes $Path $bytes -New:$New
}
function Write-FixtureJson([string]$Path, $Value, [switch]$New) {
    Write-FixtureText $Path ($Value | ConvertTo-Json -Depth 24) -New:$New
}
function Remove-FixtureFile([string]$Path) {
    $target = Assert-FixturePath $Path
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "fixture 待删目标不是普通文件：$target" }
    Remove-Item -LiteralPath $target -Force
}
function Read-FixtureJson([string]$Path) {
    return ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Assert-FixturePath $Path)))
}
function Sha512([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA512]::Create()
    try { return [Convert]::ToBase64String($algorithm.ComputeHash($Bytes)) } finally { $algorithm.Dispose() }
}
function Sha256([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function New-FixedZipBytes([object[]]$Entries) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $memory = [IO.MemoryStream]::new()
    $zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($item in @($Entries | Sort-Object source)) {
            Assert-RelativePath $item.source
            $entry = $zip.CreateEntry($item.source)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 10, 24, 0, 0, 0, [TimeSpan]::Zero)
            $stream = $entry.Open()
            try { $stream.Write($item.bytes, 0, $item.bytes.Length) } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    try { return ,$memory.ToArray() } finally { $memory.Dispose() }
}
function File-State([string[]]$Paths) {
    $rows = foreach ($target in @($Paths | Sort-Object -Unique)) {
        Assert-PlainPath $target
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            [pscustomobject]@{path=$target; exists=$true; size=(Get-Item -LiteralPath $target).Length;
                sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()}
        } elseif (Test-Path -LiteralPath $target) { throw "审计目标不是普通文件：$target" }
        else { [pscustomobject]@{path=$target; exists=$false; size=$null; sha256=$null} }
    }
    return @($rows)
}
function State-Text($State) { return ConvertTo-Json -InputObject @($State) -Depth 8 -Compress }
function Sentinel-State {
    $rows = [Collections.Generic.List[object]]::new()
    function Visit-Sentinel([string]$Directory) {
        Assert-PlainPath $Directory
        foreach ($child in @(Get-ChildItem -LiteralPath $Directory -Force | Sort-Object FullName)) {
            Assert-PlainPath $child.FullName
            if ($child.PSIsContainer) {
                $rows.Add([pscustomobject]@{path=$child.FullName; kind='directory'})
                Visit-Sentinel $child.FullName
            } else {
                $rows.Add([pscustomobject]@{path=$child.FullName; kind='file'; size=$child.Length;
                    sha256=(Get-FileHash -LiteralPath $child.FullName -Algorithm SHA256).Hash})
            }
        }
    }
    Visit-Sentinel $sentinelRoot
    return State-Text $rows.ToArray()
}
function Invoke-Tool([string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $powershell @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    return [pscustomobject]@{exitCode=$code; output=($lines | Out-String -Width 4096); executable=$powershell; arguments=$Arguments}
}
function Invoke-Checker([switch]$FrameworkDependent, [switch]$ExplicitRoot) {
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $check, '-PublishRoot', $fixture.publish)
    if (-not $FrameworkDependent) { $arguments += '-SelfContained' }
    if ($ExplicitRoot) { $arguments += @('-RepositoryRoot', $fixture.repository) }
    return Invoke-Tool $arguments
}
function Invoke-Probe([string]$Mode, [string]$PackageId = '', [string]$Archive = '', [string]$Tfm = 'net10.0-windows10.0.19041.0', [string]$Rid = 'win-x64') {
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $probe, '-Mode', $Mode, '-Tfm', $Tfm, '-Rid', $Rid)
    if ($PackageId) { $arguments += @('-PackageId', $PackageId) }
    if ($Archive) { $arguments += @('-Archive', (Assert-FixturePath $Archive)) }
    return Invoke-Tool $arguments
}
# 每个反例保留前/后输入和完整 stdout/stderr、命令、退出码；用例异常是 harness 失败，不冒充预期拒绝。
# 仅定点恢复本轮声明的 scratch 文件；批准 pin 不从任一变异 cache/ZIP/extract/pub 重建。
function Run-Case([string]$Name, [int]$Expected, [string[]]$MutablePaths = @(), [scriptblock]$Action,
    [string[]]$Needles = @(), [scriptblock]$Operation, [scriptblock]$AssertResult, [string]$Kind = 'production-checker') {
    if ($Expected -notin @(0, 1)) { throw '用例只接受真实 success=0 或 expected refusal=1。' }
    if ($Expected -eq 1 -and $Needles.Count -eq 0) { throw '拒绝用例必须指定预期错误，不能把无关失败当通过。' }
    $caseRoot = Fixture-Path $run ('c' + ($results.Count + 1).ToString('D3'))
    New-FixtureDirectory $caseRoot -Fresh
    $savedBytes = @{}
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($target in $MutablePaths) {
        $target = Assert-FixturePath $target
        if (-not $target.StartsWith($fixture.fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '变异目标不在内部合成 fixture。' }
        if (-not $allowed.Add($target)) { throw '用例变异目标重复。' }
        $savedBytes[$target] = if (Test-Path -LiteralPath $target -PathType Leaf) { [IO.File]::ReadAllBytes($target) } else { $null }
    }
    $observed = @(@($auditPaths) + @($MutablePaths) | Sort-Object -Unique)
    $before = File-State $observed
    $beforeGuard = State-Text @($before | Where-Object { -not $allowed.Contains($_.path) })
    $beforeSentinel = Sentinel-State
    $details = [ordered]@{mutationTargets=@($MutablePaths); independentPinNeverReapproved=$true}
    $result = $null
    $failure = $null
    $asserted = $true
    $after = $null
    try {
        $index = 0
        foreach ($target in $MutablePaths) {
            if ($null -ne $savedBytes[$target]) { Write-FixtureBytes (Fixture-Path $caseRoot ('before-' + (++$index) + '.bin')) $savedBytes[$target] -New }
        }
        if ($Action) { & $Action $details | Out-Null }
        $after = File-State $observed
        $index = 0
        foreach ($target in $MutablePaths) {
            $index++
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Write-FixtureBytes (Fixture-Path $caseRoot ('mutated-' + $index + '.bin')) ([IO.File]::ReadAllBytes((Assert-FixturePath $target))) -New
            }
        }
        Write-FixtureJson (Fixture-Path $caseRoot 'inputs.json') ([ordered]@{before=$before; mutated=$after}) -New
        $result = if ($Operation) { & $Operation $details } else { Invoke-Checker }
        if ($result -isnot [pscustomobject] -or $result.exitCode -isnot [int]) { throw '未取得真实子进程退出码。' }
        if ($AssertResult -and $result.exitCode -eq $Expected) { $asserted = [bool](& $AssertResult $result) }
    } catch { $failure = $_.Exception.ToString() }
    finally {
        foreach ($target in $savedBytes.Keys) {
            if ($null -ne $savedBytes[$target]) { Write-FixtureBytes $target $savedBytes[$target] }
            elseif (Test-Path -LiteralPath $target) { Remove-FixtureFile $target }
        }
    }
    $restored = File-State $observed
    $details.fixtureInputsRestored = (State-Text $before) -ceq (State-Text $restored)
    $details.undeclaredTrustInputsUnchanged = $null -ne $after -and $beforeGuard -ceq (State-Text @($after | Where-Object { -not $allowed.Contains($_.path) }))
    $details.outsideSentinelUnchanged = $beforeSentinel -ceq (Sentinel-State)
    $details.sourceRepositoryInputsUnchanged = $sourceStateText -ceq (State-Text (File-State $sourcePaths))
    if ($null -eq $result) { $result = [pscustomobject]@{exitCode=-1; output='Checker not executed: harness failure.'; executable=$powershell; arguments=@()} }
    if ($failure) { $result.output += [char]10 + 'HARNESS ERROR (NOT EXPECTED REFUSAL): ' + $failure }
    $matched = $true
    foreach ($needle in $Needles) { $matched = $matched -and $result.output.Contains($needle) }
    $passed = -not $failure -and $result.exitCode -eq $Expected -and $matched -and $asserted -and
        $details.fixtureInputsRestored -and $details.undeclaredTrustInputsUnchanged -and
        $details.outsideSentinelUnchanged -and $details.sourceRepositoryInputsUnchanged
    $log = Fixture-Path $caseRoot 'checker.log'
    Write-FixtureText $log $result.output -New
    $record = [pscustomobject]@{name=$Name; kind=$Kind; passed=$passed; expectedExit=$Expected; exitCode=$result.exitCode;
        expectedErrors=$Needles; log=$log; executable=$result.executable; arguments=$result.arguments;
        details=[pscustomobject]$details; nativeLoaded=$false; networkUsed=$false; realPublish=$false; output=$result.output}
    Write-FixtureJson (Fixture-Path $caseRoot 'result.json') $record -New
    Write-FixtureText (Fixture-Path $caseRoot 'exitcode.txt') ([string]$result.exitCode) -New
    $results.Add($record)
    Write-Output ("{0}: {1}; expected={2}, actual={3}" -f $Name, $passed, $Expected, $result.exitCode)
    if (-not $details.fixtureInputsRestored -or -not $details.outsideSentinelUnchanged -or -not $details.sourceRepositoryInputsUnchanged) {
        throw '输入/边界恢复审计失败，保留证据并停止，不继续污染后续用例。'
    }
}
function Pin-Case([string]$Name, [scriptblock]$Mutation, [string[]]$Needles) {
    Run-Case $Name 1 @($dotnetPinPath) -Action {
        param($details)
        $pin = Read-FixtureJson $dotnetPinPath
        & $Mutation $pin
        Write-FixtureJson $dotnetPinPath $pin
    } -Needles $Needles
}
function Deps-Case([string]$Name, [string[]]$Paths, [scriptblock]$Mutation, [string[]]$Needles) {
    Run-Case $Name 1 $Paths -Action {
        foreach ($target in $Paths) {
            $deps = Read-FixtureJson $target
            & $Mutation $deps
            Write-FixtureJson $target $deps
        }
    } -Needles $Needles
}
function Corrupt-Bytes([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes((Assert-FixturePath $Path))
    if ($bytes.Length -eq 0) { throw '变异输入不能为空。' }
    $bytes[0] = $bytes[0] -bxor 1
    Write-FixtureBytes $Path $bytes
}

function Published-MappingCases {
    foreach ($item in @($dotnetPlan)) {
        $removedPath = $item.path
        $removedKind = $item.kind
        Deps-Case ('仅删 published 映射、保所有 DLL/EXE：' + $removedPath) @($publishDepsPath) {
            param($d)
            $d.targets.$active.$dotnetLibrary.$removedKind.PSObject.Properties.Remove($removedPath)
        } @('待验 runtimepack deps 未完整索引批准资产')
        Run-Case ('恢复 published 映射及固定 pin 后成功：' + $removedPath) 0 -Needles @('发布验证通过：')
    }
    Deps-Case '仅删 published 整个 .NET native group、保所有 DLL/EXE 仍拒绝' @($publishDepsPath) {
        param($d)
        $d.targets.$active.$dotnetLibrary.PSObject.Properties.Remove('native')
    } @('待验 runtimepack deps 未完整索引批准资产')
    Run-Case '恢复 published .NET native group 后成功' 0 -Needles @('发布验证通过：')
    foreach ($library in @($dotnetLibrary, $sdkLibrary)) {
        $removedLibrary = $library
        Deps-Case ('仅删 published runtimepack 整库、保 pin/可信 build/归档/全文件：' + $removedLibrary) @($publishDepsPath) {
            param($d)
            $d.targets.$active.PSObject.Properties.Remove($removedLibrary)
            $d.libraries.PSObject.Properties.Remove($removedLibrary)
        } @('待验 runtimepack deps 未完整索引批准资产')
        Run-Case ('恢复 published runtimepack 整库后成功：' + $removedLibrary) 0 -Needles @('发布验证通过：')
    }
    foreach ($name in @('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll')) {
        $removedSdkName = $name
        Deps-Case ('仅删旧 SDK published managed 映射也拒绝：' + $removedSdkName) @($publishDepsPath) {
            param($d)
            $d.targets.$active.$sdkLibrary.runtime.PSObject.Properties.Remove($removedSdkName)
        } @('待验 runtimepack deps 未完整索引批准资产')
        Run-Case ('恢复旧 SDK published managed 映射后成功：' + $removedSdkName) 0 -Needles @('发布验证通过：')
    }
}
function Finish-Run {
    $failed = @($results | Where-Object { -not $_.passed }).Count
    $sourceUnchanged = $sourceStateText -ceq (State-Text (File-State $sourcePaths))
    $summary = [ordered]@{phase=$Phase; completed=(Get-Date).ToString('o'); timezone='Asia/Shanghai';
        total=$results.Count; passed=($results.Count - $failed); failed=$failed; skipped=0;
        publishedMappingOnly=[bool]$PublishedMappingOnly; previousChecker=$previousEvidence;
        checkerCases=@($results | Where-Object { $_.kind -eq 'production-checker' }).Count;
        successCases=@($results | Where-Object { $_.expectedExit -eq 0 }).Count;
        expectedOneRefusals=@($results | Where-Object { $_.expectedExit -eq 1 }).Count;
        realNativeGatePassed=$false; noWinUI=$true; noPlayback=$true; noRealCacheReadOrWrite=$true; networkUsed=$false;
        sourceInputsUnchanged=$sourceUnchanged; cases=$results.ToArray();
        scope='Actual production checker/reader and pinned SDK only, complete fixed NON-PE synthetic two-pack fixture. No real cache/native/account/settings/network access; not a real-publish or PE/runtime test.'}
    Write-FixtureJson (Fixture-Path $run 'results.json') $summary -New
    Write-FixtureText (Fixture-Path $run 'exitcode.txt') $(if ($failed -gt 0 -or -not $sourceUnchanged) { '1' } else { '0' }) -New
    Write-Output (".NET runtimepack 离线回归：{0} 项，{1} 失败，0 跳过；证据 {2}" -f $results.Count, $failed, $run)
    if ($failed -gt 0 -or -not $sourceUnchanged) { exit 1 }
    exit 0
}

New-FixtureDirectory $run -Fresh
try {
    $sourcePaths = @('tools/test-mpv-dotnet-runtimepack-content.ps1', 'tools/verify-publish.ps1',
        'tools/verify-mpv-payload.ps1', 'tools/mpv-payload-common.ps1', 'tools/mpv-payload-fixture.ps1',
        'tools/mpv-payload-dependencies.ps1', 'tools/mpv-nuget-content-hash.ps1', 'global.json', 'Directory.Build.props',
        'src/Momoka.Shell/Momoka.Shell.csproj', 'src/Momoka.Core/Momoka.Core.csproj',
        'assets/mpv-runtime/approved-payload.json', 'assets/mpv-runtime/approved-runtimepack.json',
        'assets/mpv-runtime/approved-dotnet-runtimepack.json', 'assets/mpv-runtime/vulkan-1.dll', 'libmpv-2.dll') |
        ForEach-Object { Get-ChildPath $repo $_ }
    $sourceState = File-State $sourcePaths
    $sourceStateText = State-Text $sourceState
    $realSdkPin = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Get-ChildPath $repo 'assets/mpv-runtime/approved-runtimepack.json')))
    if ($realSdkPin.schemaVersion -ne 1 -or $realSdkPin.sdkVersion -cne '10.0.400' -or
        $realSdkPin.targetFramework -cne 'net10.0-windows10.0.19041.0' -or $realSdkPin.runtimeIdentifier -cne 'win-x64' -or
        $realSdkPin.packageId -cne 'Microsoft.Windows.SDK.NET.Ref' -or $realSdkPin.packageVersion -cne '10.0.19041.57' -or
        $realSdkPin.contentHash -cne '5I8PT7WbBvGU7kq8caXQwxm8V4/+5DD6XKpg5TfRK8QyFoxr1xvpa7Q5o+rmrdJweLAh7Dm9TqKkLTym3klvDA==' -or
        $realSdkPin.archive.size -ne 12719136 -or $realSdkPin.archive.sha256 -cne '90b18f2cf7d7a23f5b0bfe2b86d8dda45881481f677ce36eccaf6e9ab50b6b4b' -or
        @($realSdkPin.assets).Count -ne 2 -or $realSdkPin.assets[0].path -cne 'Microsoft.Windows.SDK.NET.dll' -or
        $realSdkPin.assets[0].source -cne 'lib/net8.0/Microsoft.Windows.SDK.NET.dll' -or
        $realSdkPin.assets[1].path -cne 'WinRT.Runtime.dll' -or $realSdkPin.assets[1].source -cne 'lib/net8.0/WinRT.Runtime.dll') {
        throw '既有 Windows SDK pin 固定字段已改变，停止而非重生成旧批准。'
    }
    $sentinelRoot = Fixture-Path $run 'outside-fixture-sentinel'
    New-FixtureDirectory $sentinelRoot -Fresh
    Write-FixtureText (Fixture-Path $sentinelRoot 'sentinel.txt') 'R067 immutable outside-inner-fixture sentinel -- never a user file' -New
    Write-FixtureText (Fixture-Path $sentinelRoot 'LICENSE') 'R067 isolated traversal sentinel -- do not change' -New
    $fixture = New-MpvPayloadFixture $repo (Fixture-Path $run 'f')
    if (-not $fixture.nativeIsSynthetic) { throw '必须使用明确的非 PE 合成 native fixture。' }
    $tfm = 'net10.0-windows10.0.19041.0'
    $rid = 'win-x64'
    $active = '.NETCoreApp,Version=v10.0/win-x64'
    $dotnetId = 'Microsoft.NETCore.App.Runtime.win-x64'
    $dotnetVersion = '10.0.11'
    $dotnetLibrary = 'runtimepack.' + $dotnetId + '/' + $dotnetVersion
    $sdkLibrary = 'runtimepack.Microsoft.Windows.SDK.NET.Ref/10.0.19041.57'
    $foundationLibrary = 'Microsoft.WindowsAppSDK.Foundation/2.3.12'
    $assetsPath = Fixture-Path $fixture.repository 'src/Momoka.Shell/obj/project.assets.json'
    $buildOutput = Fixture-Path $fixture.repository ('src/Momoka.Shell/bin/x64/Release/' + $tfm + '/' + $rid)
    $trustedDepsPath = Fixture-Path $buildOutput 'Momoka.deps.json'
    $publishDepsPath = Fixture-Path $fixture.publish 'Momoka.deps.json'
    $dotnetPinPath = Fixture-Path $fixture.repository 'assets/mpv-runtime/approved-dotnet-runtimepack.json'
    $sdkPinPath = Fixture-Path $fixture.repository 'assets/mpv-runtime/approved-runtimepack.json'
    $assets = Read-FixtureJson $assetsPath
    $originalPackages = @($assets.packageFolders.PSObject.Properties.Name)
    if ($originalPackages.Count -ne 1) { throw '既有合成 fixture 包根不唯一。' }
    $originalPackages = Assert-FixturePath $originalPackages[0].TrimEnd('\')
    # 模拟普通用户 cache 的位置与 URL 元数据，但 cache/feed/所有攻击仍全在当前全新 fixture 内。
    $packages = Fixture-Path $fixture.fixture 'cache/p'
    $copyPlan = @(Get-PlainFiles $originalPackages -Recurse | ForEach-Object {
        [pscustomobject]@{source=(Assert-FixturePath $_.FullName);
            target=(Fixture-Path $packages $_.FullName.Substring($originalPackages.Length + 1).Replace('\', '/'))}
    })
    foreach ($copy in $copyPlan) { Write-FixtureBytes $copy.target ([IO.File]::ReadAllBytes($copy.source)) -New }
    foreach ($file in @(Get-PlainFiles $packages -Recurse | Where-Object { $_.Name -ceq '.nupkg.metadata' })) {
        $metadata = Read-FixtureJson $file.FullName
        $metadata.source = 'https://api.nuget.org/v3/index.json'
        Write-FixtureJson $file.FullName $metadata
    }
    $assets.packageFolders = [pscustomobject]@{($packages + '\')=[pscustomobject]@{}}

    # 独立批准只从以下四项固定常量和固定 ZIP 构建计划产生，不读待验 archive/extract/pub 建 pin。
    # 内容不是 PE；整个测试不会加载这些 DLL/EXE，也不会起 App、WinUI 或播放器。
    $dotnetPlan = @(
        [pscustomobject]@{kind='runtime'; path='R067.DotNet.ManagedA.dll'; source='runtimes/win-x64/lib/net10.0/R067.DotNet.ManagedA.dll';
            bytes=$assetEncoding.GetBytes('R067 DOTNET FIXED SYNTHETIC MANAGED A v1 -- NON-PE NEVER LOAD')},
        [pscustomobject]@{kind='runtime'; path='R067.DotNet.ManagedB.dll'; source='runtimes/win-x64/lib/net10.0/R067.DotNet.ManagedB.dll';
            bytes=$assetEncoding.GetBytes('R067 DOTNET FIXED SYNTHETIC MANAGED B v1 -- NON-PE NEVER LOAD')},
        [pscustomobject]@{kind='native'; path='R067.DotNet.Native.dll'; source='runtimes/win-x64/native/R067.DotNet.Native.dll';
            bytes=$assetEncoding.GetBytes('R067 DOTNET FIXED SYNTHETIC NATIVE v1 -- NON-PE NEVER LOAD')},
        [pscustomobject]@{kind='native'; path='createdump.exe'; source='runtimes/win-x64/native/createdump.exe';
            bytes=$assetEncoding.GetBytes('R067 DOTNET FIXED SYNTHETIC CREATEDUMP v1 -- NON-PE NEVER LAUNCH')}
    )
    $dotnetMetadata = [pscustomobject]@{source='Microsoft.NETCore.App.Runtime.win-x64.nuspec'; bytes=$assetEncoding.GetBytes(
        '<package><metadata><id>Microsoft.NETCore.App.Runtime.win-x64</id><version>10.0.11</version><authors>R067 Offline Fixture</authors><description>Fixed synthetic NON-PE package; never execute.</description></metadata></package>')}
    $dotnetZip = New-FixedZipBytes (@($dotnetPlan) + @($dotnetMetadata))
    $dotnetPin = [ordered]@{schemaVersion=1; approvalId='R067-OFFLINE-DOTNET-FIXED-NON-PE-NOT-PRODUCTION';
        sdkVersion='10.0.400'; targetFramework=$tfm; runtimeIdentifier=$rid; packageId=$dotnetId; packageVersion=$dotnetVersion;
        contentHash=(Sha512 $dotnetZip); archive=[ordered]@{source='R067 independent four-constant synthetic ZIP plan; NOT official .NET approval';
            size=$dotnetZip.Length; sha256=(Sha256 $dotnetZip)};
        assets=@($dotnetPlan | Select-Object kind, path, source)}
    $packDirectory = Fixture-Path $packages ($dotnetId.ToLowerInvariant() + '/' + $dotnetVersion)
    $archivePath = Fixture-Path $packDirectory ($dotnetId.ToLowerInvariant() + '.' + $dotnetVersion + '.nupkg')
    $sidePath = Assert-FixturePath ($archivePath + '.sha512')
    $metadataPath = Assert-FixturePath (Join-Path $packDirectory '.nupkg.metadata')
    Write-FixtureBytes $archivePath $dotnetZip -New
    Write-FixtureText $sidePath (Sha512 $dotnetZip) -New
    Write-FixtureJson $metadataPath ([ordered]@{version=2; contentHash=$dotnetPin.contentHash;
        source='https://api.nuget.org/v3/index.json'; synthetic=$true}) -New
    foreach ($item in $dotnetPlan) {
        Write-FixtureBytes (Fixture-Path $packDirectory $item.source) $item.bytes -New
        Write-FixtureBytes (Fixture-Path $fixture.publish $item.path) $item.bytes -New
    }
    Write-FixtureJson $dotnetPinPath $dotnetPin -New
    $assets.project.frameworks.$tfm.downloadDependencies += [pscustomobject]@{name=$dotnetId; version='[10.0.11, 10.0.11]'}
    $packDeps = [pscustomobject]@{runtime=[pscustomobject]@{}; native=[pscustomobject]@{}}
    foreach ($item in $dotnetPlan) {
        $packDeps.($item.kind) | Add-Member -NotePropertyName $item.path -NotePropertyValue ([pscustomobject]@{})
    }
    # 可信输出与待验输出各从独立的原始 fixture deps 加同一固定计划，不借待验 deps 反推可信资产。
    foreach ($target in @($trustedDepsPath, $publishDepsPath)) {
        $deps = Read-FixtureJson $target
        $deps.targets.$active | Add-Member -NotePropertyName $dotnetLibrary -NotePropertyValue $packDeps
        $deps.libraries | Add-Member -NotePropertyName $dotnetLibrary -NotePropertyValue ([pscustomobject]@{type='runtimepack'})
        Write-FixtureJson $target $deps
    }

    # Foundation RestartAgent 正例也来自独立固定字节计划，不用新 EXE 的存在自授权。
    $bootstrapSource = 'runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll'
    $restartSource = 'runtimes-framework/win-x64/native/RestartAgent.exe'
    $foundationPlan = @(
        [pscustomobject]@{source=$bootstrapSource; bytes=$assetEncoding.GetBytes('R067 offline host fixture themeresources -- never launch')},
        [pscustomobject]@{source=$restartSource; bytes=$assetEncoding.GetBytes('R067 FIXED FOUNDATION RESTART AGENT -- NON-PE NEVER LAUNCH')}
    )
    $foundationDirectory = Fixture-Path $packages 'microsoft.windowsappsdk.foundation/2.3.12'
    $foundationArchive = Fixture-Path $foundationDirectory 'microsoft.windowsappsdk.foundation.2.3.12.nupkg'
    $foundationMetadata = Assert-FixturePath (Join-Path $foundationDirectory '.nupkg.metadata')
    $foundationZip = New-FixedZipBytes $foundationPlan
    Write-FixtureBytes $foundationArchive $foundationZip
    Write-FixtureText (Assert-FixturePath ($foundationArchive + '.sha512')) (Sha512 $foundationZip)
    Write-FixtureJson $foundationMetadata ([ordered]@{version=2; contentHash=(Sha512 $foundationZip);
        source='https://api.nuget.org/v3/index.json'; synthetic=$true})
    foreach ($item in $foundationPlan) { Write-FixtureBytes (Fixture-Path $foundationDirectory $item.source) $item.bytes }
    $assets.libraries.$foundationLibrary.sha512 = Sha512 $foundationZip
    $assets.libraries.$foundationLibrary.files = @($foundationPlan.source)
    Write-FixtureJson $assetsPath $assets
    $restartExtract = Fixture-Path $foundationDirectory $restartSource
    $restartPublish = Fixture-Path $fixture.publish 'RestartAgent.exe'
    Write-FixtureBytes $restartPublish $foundationPlan[1].bytes -New
    $apphost = Fixture-Path $buildOutput 'Momoka.exe'
    $apphostPublish = Fixture-Path $fixture.publish 'Momoka.exe'
    $coreCopy = Fixture-Path $buildOutput 'Momoka.Core.dll'
    $coreAnyCpu = Fixture-Path $fixture.repository 'src/Momoka.Core/bin/Release/net10.0/Momoka.Core.dll'
    $coreX64 = Fixture-Path $fixture.repository 'src/Momoka.Core/bin/x64/Release/net10.0/Momoka.Core.dll'
    $corePublish = Fixture-Path $fixture.publish 'Momoka.Core.dll'
    Write-FixtureBytes $corePublish ($assetEncoding.GetBytes('registered dependency fixture')) -New
    Write-FixtureBytes (Fixture-Path $fixture.publish 'Momoka.dll') ($assetEncoding.GetBytes('R067 fixed synthetic compiled Momoka DLL -- never launch')) -New
    if (-not (Test-Path -LiteralPath $apphost -PathType Leaf) -or -not (Test-Path -LiteralPath $coreCopy -PathType Leaf)) {
        throw '需要更新后的既有 fixture，固定主 apphost/Core 引用副本缺失。'
    }
    $tools = Fixture-Path $fixture.repository 'tools'
    $toolPlan = @('verify-publish.ps1', 'verify-mpv-payload.ps1', 'mpv-payload-common.ps1',
        'mpv-payload-dependencies.ps1', 'mpv-nuget-content-hash.ps1') | ForEach-Object {
        [pscustomobject]@{source=(Get-ChildPath $repo ('tools/' + $_)); target=(Fixture-Path $tools $_)}
    }
    foreach ($copy in $toolPlan) { Write-FixtureBytes $copy.target ([IO.File]::ReadAllBytes($copy.source)) -New }
    $previousEvidence = $null
    if ($PreviousChecker) {
        if (-not $PublishedMappingOnly) { throw '旧 checker 仅允许 published-mapping 定向红回归，不运行其余用例。' }
        $previousPath = [IO.Path]::GetFullPath($PreviousChecker)
        Assert-PlainPath $previousPath
        if (-not $previousPath.StartsWith($repo + '\artifacts\', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $previousPath -PathType Leaf)) { throw '旧 checker 必须是本树 artifacts 内此前只读 source 副本。' }
        $previousHash = (Get-FileHash -LiteralPath $previousPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-FixtureBytes (Fixture-Path $tools 'mpv-payload-dependencies.ps1') ([IO.File]::ReadAllBytes($previousPath))
        $previousEvidence = [ordered]@{source=$previousPath; sourceSha256=$previousHash; purpose='P2 prior-source expected1 red evidence, not a passing current checker'}
    }
    $check = Fixture-Path $tools 'verify-publish.ps1'
    $probe = Fixture-Path $tools 'runtimepack-probe.ps1'
    # Probe 不重写 checker：子进程原样调用生产 reader/SDK hash，仅提供稳定的机器可读证据。
    $probeText = @'
param([ValidateSet('Pin','DefaultPin','Hash')][string]$Mode, [string]$PackageId, [string]$Archive,
    [string]$Tfm, [string]$Rid)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
    . (Join-Path $PSScriptRoot 'mpv-payload-dependencies.ps1')
    $repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
    if ($Mode -eq 'Hash') {
        . (Join-Path $PSScriptRoot 'mpv-nuget-content-hash.ps1')
        Write-Output (ConvertTo-Json -InputObject ([ordered]@{contentHash=(Get-MpvNuGetPackageContentHash $Archive)}) -Compress)
    } elseif ($Mode -eq 'DefaultPin') {
        Read-RuntimepackApproval $repository $Tfm $Rid | ConvertTo-Json -Depth 10
    } else {
        Read-RuntimepackApproval $repository $Tfm $Rid $PackageId | ConvertTo-Json -Depth 10
    }
    exit 0
} catch { Write-Output $_.Exception.ToString(); exit 1 }
'@
    Write-FixtureText $probe $probeText -New
    $auditPaths = @($assetsPath, $trustedDepsPath, $publishDepsPath, $dotnetPinPath, $sdkPinPath, $fixture.manifest,
        $apphost, $apphostPublish, $coreCopy, $coreAnyCpu, $corePublish,
        (Fixture-Path $fixture.repository 'libmpv-2.dll'), (Fixture-Path $fixture.repository 'assets/mpv-runtime/vulkan-1.dll'),
        (Fixture-Path $fixture.publish 'libmpv-2.dll'), (Fixture-Path $fixture.publish 'vulkan-1.dll')) +
        @($toolPlan.target) + @((Get-PlainFiles $packages -Recurse).FullName) +
        @($dotnetPlan | ForEach-Object { Fixture-Path $fixture.publish $_.path }) + @($restartPublish)
    $setup = [ordered]@{phase=$Phase; started=(Get-Date).ToString('o'); repository=$repo; run=$run; fixture=$fixture;
        packageRoots=@{syntheticOnly=$packages; originalSynthetic=$originalPackages}; sourceInputs=$sourceState;
        preservedProductionSdkPin=$realSdkPin; syntheticDotnetPin=$dotnetPin; fixedFoundationEntries=@($foundationPlan.source);
        noRealCacheReadOrWrite=$true; nativeAndExeAreNonPE=$true; networkUsed=$false}
    Write-FixtureJson (Fixture-Path $run 'setup.json') $setup -New
    Write-FixtureJson (Fixture-Path $run 'baseline-inputs.json') (File-State $auditPaths) -New
    Run-Case '两个独立 pack 共存，默认生产 SelfContained 校验成功（含主 apphost/合法 RestartAgent）' 0 -Needles @('发布验证通过：')
    if (-not $results[$results.Count - 1].passed) { throw '完整合成正例未通过，停止；不把后续无关失败算成反例。' }
    Published-MappingCases
    if ($PublishedMappingOnly) { Finish-Run }
    Run-Case '同一完整 fixture 显式 RepositoryRoot 的 SelfContained 校验成功' 0 -Needles @('发布验证通过：') -Operation { Invoke-Checker -ExplicitRoot }
    Run-Case '既有 reader 默认 PackageId 仍只读旧 Windows SDK pin（两个 managed）' 0 -Operation { Invoke-Probe DefaultPin } -AssertResult {
        param($r)
        $p = ConvertFrom-Json -InputObject $r.output
        return $p.packageId -ceq 'Microsoft.Windows.SDK.NET.Ref' -and $p.packageVersion -ceq '10.0.19041.57' -and
            @($p.assets).Count -eq 2 -and $p.assets[0].source -ceq 'lib/net8.0/Microsoft.Windows.SDK.NET.dll' -and
            $p.assets[1].source -ceq 'lib/net8.0/WinRT.Runtime.dll'
    } -Kind 'production-reader'
    Run-Case '精确 .NET PackageId reader 选择独立四资产 pin' 0 -Operation { Invoke-Probe Pin $dotnetId } -AssertResult {
        param($r)
        $p = ConvertFrom-Json -InputObject $r.output
        return $p.packageId -ceq $dotnetId -and $p.packageVersion -ceq $dotnetVersion -and $p.contentHash -ceq $dotnetPin.contentHash -and
            @($p.assets | Where-Object { $_.kind -ceq 'runtime' }).Count -eq 2 -and
            @($p.assets | Where-Object { $_.kind -ceq 'native' }).Count -eq 2 -and
            @($p.assets | Where-Object { $_.path -ceq 'createdump.exe' -and $_.kind -ceq 'native' }).Count -eq 1
    } -Kind 'production-reader'
    Run-Case '真实 SDK GetContentHash 与独立固定合成 ZIP pin 一致（无旁文件 fallback）' 0 -Operation { Invoke-Probe Hash '' $archivePath } -AssertResult {
        param($r)
        return (ConvertFrom-Json -InputObject $r.output).contentHash -ceq $dotnetPin.contentHash
    } -Kind 'official-sdk-content-hash'
    Run-Case '完整 .NET runtimepack 在 FrameworkDependent 模式必须拒绝' 1 -Needles @('框架依赖发布不接受自包含 .NET runtimepack') -Operation { Invoke-Checker -FrameworkDependent }
    Run-Case '缺独立 .NET pin 不能借旧 SDK pin 或 cache metadata 放行' 1 @($dotnetPinPath) -Action {
        Remove-FixtureFile $dotnetPinPath
    } -Needles @('缺少独立 runtimepack 批准清单')
    Run-Case '双 pack 下缺旧 SDK pin 也不能借 .NET pin 放行' 1 @($sdkPinPath) -Action {
        Remove-FixtureFile $sdkPinPath
    } -Needles @('缺少独立 runtimepack 批准清单')
    foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'Microsoft.NETCore.App.Runtime.win-arm64',
        'Microsoft.NETCore.App.Runtime.win-x64.Extra', 'Microsoft.Windows.SDK.NET.Ref.Extra', 'R067.Unknown.Runtimepack')) {
        $candidateId = $id
        Run-Case ('reader 拒未知/大小写别名 PackageId：' + $candidateId) 1 -Operation { Invoke-Probe Pin $candidateId } -Needles @('未知独立 runtimepack 包') -Kind 'production-reader'
    }
    Run-Case 'reader 调用 TFM 与独立 pin 不符拒绝' 1 -Operation { Invoke-Probe Pin $dotnetId '' 'net9.0-windows10.0.19041.0' $rid } -Needles @('批准 SDK/TFM/RID') -Kind 'production-reader'
    Run-Case 'reader 调用 RID 与独立 pin 不符拒绝' 1 -Operation { Invoke-Probe Pin $dotnetId '' $tfm 'win-arm64' } -Needles @('批准 SDK/TFM/RID') -Kind 'production-reader'

    Pin-Case '.NET pin 包版本漂移拒绝' { param($p); $p.packageVersion = '10.0.12' } @('独立批准包/版本不符')
    Pin-Case '.NET pin TFM 漂移拒绝' { param($p); $p.targetFramework = 'net9.0-windows10.0.19041.0' } @('批准 SDK/TFM/RID')
    Pin-Case '.NET pin RID 漂移拒绝' { param($p); $p.runtimeIdentifier = 'win-arm64' } @('批准 SDK/TFM/RID')
    Pin-Case '.NET pin SDK 漂移拒绝' { param($p); $p.sdkVersion = '10.0.401' } @('批准 SDK/TFM/RID')
    Pin-Case '.NET pin 伪装 SDK 包身份拒绝' { param($p); $p.packageId = 'Microsoft.Windows.SDK.NET.Ref' } @('批准包身份')
    Pin-Case '.NET pin 大小写包身份别名拒绝' { param($p); $p.packageId = $p.packageId.ToLowerInvariant() } @('批准包身份')
    Pin-Case '.NET pin schema 不是1拒绝' { param($p); $p.schemaVersion = 2 } @('schemaVersion')
    Pin-Case '.NET pin 多未知顶层字段拒绝' { param($p); $p | Add-Member -NotePropertyName allowAnyRuntimepack -NotePropertyValue $true } @('未知字段')
    Pin-Case '.NET pin 无效内容摘要拒绝' { param($p); $p.contentHash = 'invalid' } @('内容摘要')
    Pin-Case '.NET pin 数组内容摘要拒绝（非标量）' { param($p); $p.contentHash = @($p.contentHash) } @('非空标量字符串')
    Pin-Case '.NET pin 规范但错误 SHA512 不得信 cache metadata' { param($p); $p.contentHash = [Convert]::ToBase64String([byte[]]::new(64)) } @('可信包内容/归档与当前 restore 不符')
    Pin-Case '.NET pin 归档 size 漂移拒绝' { param($p); $p.archive.size++ } @('独立批准字节索引')
    Pin-Case '.NET pin 归档 SHA256 漂移拒绝' { param($p); $p.archive.sha256 = '0' * 64 } @('独立批准字节索引')
    Pin-Case '.NET pin 归档 size 非整数拒绝' { param($p); $p.archive.size = [string]$p.archive.size } @('出处/字节索引无效')
    Pin-Case '.NET pin 空资产索引拒绝' { param($p); $p.assets = @() } @('非空数组')
    Pin-Case '.NET pin 重复映射拒绝' { param($p); $p.assets += $p.assets[0] } @('路径/来源重复')
    Pin-Case '.NET pin 多未获双 deps 批准的 managed 映射拒绝' {
        param($p)
        $p.assets += [pscustomobject]@{kind='runtime'; path='R067.Foreign.dll'; source='runtimes/win-x64/lib/net10.0/R067.Foreign.dll'}
    } @('未完整索引批准资产')
    foreach ($spec in @(
        @{name='runtime kind 越界'; index=0; field='kind'; value='runtimeTargets'; error='批准资产映射无效'},
        @{name='managed 目标子目录'; index=0; field='path'; value='sub/R067.DotNet.ManagedA.dll'; error='批准资产映射无效'},
        @{name='managed 目标 traversal'; index=0; field='path'; value='../outside-fixture-sentinel/LICENSE'; error='非法清单路径'},
        @{name='managed 来源 traversal'; index=0; field='source'; value='../../outside-fixture-sentinel/LICENSE'; error='非法清单路径'},
        @{name='managed 来源 Windows 保留名'; index=0; field='source'; value='runtimes/win-x64/lib/net10.0/NUL.dll'; error='非法清单路径'},
        @{name='managed 借旧 SDK lib 目录'; index=0; field='source'; value='lib/net8.0/R067.DotNet.ManagedA.dll'; error='批准资产映射无效'},
        @{name='managed 错 RID 目录'; index=0; field='source'; value='runtimes/win-arm64/lib/net10.0/R067.DotNet.ManagedA.dll'; error='批准资产映射无效'},
        @{name='managed 错 TFM 目录'; index=0; field='source'; value='runtimes/win-x64/lib/net9.0/R067.DotNet.ManagedA.dll'; error='批准资产映射无效'},
        @{name='managed 嵌套 lib 目录'; index=0; field='source'; value='runtimes/win-x64/lib/net10.0/sub/R067.DotNet.ManagedA.dll'; error='批准资产映射无效'},
        @{name='native 错目录'; index=2; field='source'; value='runtimes/win-x64/native/sub/R067.DotNet.Native.dll'; error='批准资产映射无效'},
        @{name='native 错 RID'; index=2; field='source'; value='runtimes/win-arm64/native/R067.DotNet.Native.dll'; error='批准资产映射无效'},
        @{name='native basename 不等于目标'; index=2; field='source'; value='runtimes/win-x64/native/R067.Different.dll'; error='批准资产映射无效'},
        @{name='createdump 不能是 runtime executable'; index=3; field='kind'; value='runtime'; error='批准资产映射无效'}
    )) {
        $mappingSpec = $spec
        Pin-Case ('.NET pin 拒绝越界映射：' + $mappingSpec.name) {
            param($p)
            $p.assets[$mappingSpec.index].($mappingSpec.field) = $mappingSpec.value
        } @($mappingSpec.error)
    }
    Pin-Case '.NET native 只允许 createdump.exe，不接受任意 EXE' {
        param($p)
        $p.assets[3].path = 'R067.Unknown.exe'
        $p.assets[3].source = 'runtimes/win-x64/native/R067.Unknown.exe'
    } @('批准资产映射无效')
    Pin-Case '.NET managed 不能批准 EXE' {
        param($p)
        $p.assets[0].path = 'R067.Unknown.exe'
        $p.assets[0].source = 'runtimes/win-x64/lib/net10.0/R067.Unknown.exe'
    } @('批准资产映射无效')
    Run-Case '.NET pin 转义重复 JSON contentHash 字段拒绝' 1 @($dotnetPinPath) -Action {
        $text = [IO.File]::ReadAllText($dotnetPinPath)
        $text = $text.Replace('"contentHash":', ('"contentHash":"' + $dotnetPin.contentHash + '","content\u0048ash":'))
        Write-FixtureText $dotnetPinPath $text
    } -Needles @('重复 JSON 字段')

    $publishManaged = Fixture-Path $fixture.publish $dotnetPlan[0].path
    $extractManaged = Fixture-Path $packDirectory $dotnetPlan[0].source
    $publishNative = Fixture-Path $fixture.publish $dotnetPlan[2].path
    $extractNative = Fixture-Path $packDirectory $dotnetPlan[2].source
    $publishDump = Fixture-Path $fixture.publish 'createdump.exe'
    $extractDump = Fixture-Path $packDirectory $dotnetPlan[3].source
    # 缺资产与字节错误均单独运行完整生产 checker；不是只测 reader 或修改双 deps 后相互作证。
    foreach ($item in $dotnetPlan) {
        $currentAsset = $item
        $currentPublish = Fixture-Path $fixture.publish $item.path
        $currentExtract = Fixture-Path $packDirectory $item.source
        Run-Case ('发布缺批准 .NET 资产拒绝：' + $currentAsset.path) 1 @($currentPublish) -Action {
            Remove-FixtureFile $currentPublish
        } -Needles @('发布缺少可信运行时资产：', $currentAsset.path)
        Run-Case ('发布 .NET 资产同大小错字节拒绝：' + $currentAsset.path) 1 @($currentPublish) -Action {
            Corrupt-Bytes $currentPublish
        } -Needles @('运行时载荷字节与可信固定包/当前编译资产不符：', $currentAsset.path)
        Run-Case ('extract .NET 资产同大小错字节拒绝：' + $currentAsset.path) 1 @($currentExtract) -Action {
            Corrupt-Bytes $currentExtract
        } -Needles @('恢复资产与固定归档字节不符：', $currentAsset.path)
        Run-Case ('extract/pub 一起换字节也拒绝：' + $currentAsset.path) 1 @($currentExtract, $currentPublish) -Action {
            Corrupt-Bytes $currentExtract
            Write-FixtureBytes $currentPublish ([IO.File]::ReadAllBytes($currentExtract))
        } -Needles @('恢复资产与固定归档字节不符：', $currentAsset.path)
        $missingName = $item.path
        $missingKind = $item.kind
        Deps-Case ('可信 build 省批准 .NET 资产拒绝：' + $missingName) @($trustedDepsPath) {
            param($d)
            $d.targets.$active.$dotnetLibrary.$missingKind.PSObject.Properties.Remove($missingName)
        } @('未完整索引批准资产')
        Deps-Case ('双 deps 一起省批准资产仍拒绝：' + $missingName) @($trustedDepsPath, $publishDepsPath) {
            param($d)
            $d.targets.$active.$dotnetLibrary.$missingKind.PSObject.Properties.Remove($missingName)
        } @('未完整索引批准资产')
        Pin-Case ('.NET pin 省 build 已用资产拒绝：' + $missingName) {
            param($p)
            $p.assets = @($p.assets | Where-Object { $_.path -cne $missingName })
        } @('独立批准映射')
    }
    foreach ($item in @($dotnetPlan[0], $dotnetPlan[2], $dotnetPlan[3])) {
        $currentAsset = $item
        $currentExtract = Fixture-Path $packDirectory $item.source
        Run-Case ('extract 缺批准 .NET 资产拒绝：' + $currentAsset.path) 1 @($currentExtract) -Action {
            Remove-FixtureFile $currentExtract
        } -Needles @($currentAsset.path)
    }
    Run-Case 'restore 缺 .NET downloadDependencies 时拒绝未知 pack' 1 @($assetsPath) -Action {
        $a = Read-FixtureJson $assetsPath
        $a.project.frameworks.$tfm.downloadDependencies = @($a.project.frameworks.$tfm.downloadDependencies | Where-Object { $_.name -cne $dotnetId })
        Write-FixtureJson $assetsPath $a
    } -Needles @('未知可信 runtime pack：', $dotnetId)
    Run-Case 'restore .NET downloadDependencies 重复必须拒绝' 1 @($assetsPath) -Action {
        $a = Read-FixtureJson $assetsPath
        $a.project.frameworks.$tfm.downloadDependencies += [pscustomobject]@{name=$dotnetId; version='[10.0.11, 10.0.11]'}
        Write-FixtureJson $assetsPath $a
    } -Needles @('未知可信 runtime pack：', $dotnetId)
    Run-Case 'restore .NET downloadDependencies 非精确版本拒绝' 1 @($assetsPath) -Action {
        $a = Read-FixtureJson $assetsPath
        @($a.project.frameworks.$tfm.downloadDependencies | Where-Object { $_.name -ceq $dotnetId })[0].version = '[10.0.11, )'
        Write-FixtureJson $assetsPath $a
    } -Needles @('未知可信 runtime pack：', $dotnetId)
    Deps-Case '待验 deps runtimeTarget 错 RID 拒绝' @($publishDepsPath) {
        param($d); $d.runtimeTarget.name = '.NETCoreApp,Version=v10.0/win-arm64'
    } @('runtimeTarget/RID 与可信当前构建不符')
    Deps-Case '可信 build deps runtimeTarget 错 TFM 拒绝' @($trustedDepsPath) {
        param($d); $d.runtimeTarget.name = '.NETCoreApp,Version=v9.0/win-x64'
    } @('可信构建 deps selected target/RID 不符')
    Deps-Case '待验 deps 多未选 target 拒绝' @($publishDepsPath) {
        param($d)
        $d.targets | Add-Member -NotePropertyName '.NETCoreApp,Version=v10.0/win-arm64' -NotePropertyValue ([pscustomobject]@{})
    } @('未知/未选 target')
    Deps-Case '待验 deps 独改 .NET 包版本拒绝' @($publishDepsPath) {
        param($d)
        $value = $d.targets.$active.$dotnetLibrary
        $d.targets.$active.PSObject.Properties.Remove($dotnetLibrary)
        $d.targets.$active | Add-Member -NotePropertyName ('runtimepack.' + $dotnetId + '/10.0.12') -NotePropertyValue $value
    } @('待验 deps 未批准依赖')
    Run-Case '双 deps/restore 同改 .NET 版本也不能更新固定 pin' 1 @($trustedDepsPath, $publishDepsPath, $assetsPath) -Action {
        foreach ($target in @($trustedDepsPath, $publishDepsPath)) {
            $d = Read-FixtureJson $target
            $value = $d.targets.$active.$dotnetLibrary
            $d.targets.$active.PSObject.Properties.Remove($dotnetLibrary)
            $d.targets.$active | Add-Member -NotePropertyName ('runtimepack.' + $dotnetId + '/10.0.12') -NotePropertyValue $value
            Write-FixtureJson $target $d
        }
        $a = Read-FixtureJson $assetsPath
        @($a.project.frameworks.$tfm.downloadDependencies | Where-Object { $_.name -ceq $dotnetId })[0].version = '[10.0.12, 10.0.12]'
        Write-FixtureJson $assetsPath $a
    } -Needles @('独立批准包/版本不符')
    $foreignDll = Fixture-Path $fixture.publish 'R067.Foreign.Runtimepack.dll'
    Run-Case '双 deps 多新 managed DLL 不能自授权独立 .NET pin' 1 @($trustedDepsPath, $publishDepsPath, $foreignDll) -Action {
        Write-FixtureBytes $foreignDll ($assetEncoding.GetBytes('R067 foreign NON-PE DLL -- never load')) -New
        foreach ($target in @($trustedDepsPath, $publishDepsPath)) {
            $d = Read-FixtureJson $target
            $d.targets.$active.$dotnetLibrary.runtime | Add-Member -NotePropertyName 'R067.Foreign.Runtimepack.dll' -NotePropertyValue ([pscustomobject]@{})
            Write-FixtureJson $target $d
        }
    } -Needles @('独立批准映射')
    Deps-Case '双 deps 将 managed 映射到子目录也不能用相同 basename 放行' @($trustedDepsPath, $publishDepsPath) {
        param($d)
        $group = $d.targets.$active.$dotnetLibrary.runtime
        $group | Add-Member -NotePropertyName 'sub/R067.DotNet.ManagedA.dll' -NotePropertyValue ([pscustomobject]@{})
    } @('独立批准映射')
    Deps-Case '双 deps 将 createdump 错放 runtime kind 拒绝' @($trustedDepsPath, $publishDepsPath) {
        param($d)
        $d.targets.$active.$dotnetLibrary.runtime | Add-Member -NotePropertyName 'createdump.exe' -NotePropertyValue ([pscustomobject]@{})
    } @('独立批准映射')
    Deps-Case '待验 deps 多不批准 native 映射拒绝' @($publishDepsPath) {
        param($d)
        $d.targets.$active.$dotnetLibrary.native | Add-Member -NotePropertyName 'R067.Foreign.Native.dll' -NotePropertyValue ([pscustomobject]@{})
    } @('待验 deps 未批准资产/RID')
    Run-Case '未知 runtimepack 即使加入 restore/downloads 与双 deps 也拒绝' 1 @($assetsPath, $trustedDepsPath, $publishDepsPath) -Action {
        $unknownId = 'R067.Unknown.Runtimepack'
        $unknownLibrary = 'runtimepack.' + $unknownId + '/1.0.0'
        $a = Read-FixtureJson $assetsPath
        $a.project.frameworks.$tfm.downloadDependencies += [pscustomobject]@{name=$unknownId; version='[1.0.0, 1.0.0]'}
        Write-FixtureJson $assetsPath $a
        foreach ($target in @($trustedDepsPath, $publishDepsPath)) {
            $d = Read-FixtureJson $target
            $d.targets.$active | Add-Member -NotePropertyName $unknownLibrary -NotePropertyValue ([pscustomobject]@{runtime=[pscustomobject]@{'R067.Unknown.dll'=[pscustomobject]@{}}})
            $d.libraries | Add-Member -NotePropertyName $unknownLibrary -NotePropertyValue ([pscustomobject]@{type='runtimepack'})
            Write-FixtureJson $target $d
        }
    } -Needles @('未知独立 runtimepack 包')

    Run-Case '.NET archive 原始字节漂移与旧 sideHash 不符拒绝' 1 @($archivePath) -Action {
        Corrupt-Bytes $archivePath
    } -Needles @('可信包内容/归档与当前 restore 不符')
    Run-Case '.NET archive sideHash 错摘要拒绝' 1 @($sidePath) -Action {
        Write-FixtureText $sidePath ([Convert]::ToBase64String([byte[]]::new(64)))
    } -Needles @('可信包内容/归档与当前 restore 不符')
    Run-Case '.NET metadata 内容摘要漂移拒绝' 1 @($metadataPath) -Action {
        $m = Read-FixtureJson $metadataPath
        $m.contentHash = [Convert]::ToBase64String([byte[]]::new(64))
        Write-FixtureJson $metadataPath $m
    } -Needles @('可信包内容/归档与当前 restore 不符')
    Run-Case '.NET archive/sideHash 一起换字节，仍须实际 SDK GetContentHash 对独立 pin' 1 @($archivePath, $sidePath) -Action {
        $attackPlan = @($dotnetPlan | ForEach-Object { [pscustomobject]@{source=$_.source; bytes=[byte[]]$_.bytes.Clone()} })
        $attackPlan[0].bytes[0] = $attackPlan[0].bytes[0] -bxor 1
        $changed = New-FixedZipBytes (@($attackPlan) + @($dotnetMetadata))
        Write-FixtureBytes $archivePath $changed
        Write-FixtureText $sidePath (Sha512 $changed)
    } -Needles @('NuGet 实际包内容摘要与独立 runtimepack 批准', '不符：Microsoft.NETCore.App.Runtime.win-x64/10.0.11')
    Run-Case '.NET archive/sideHash/metadata/extract/pub 一起替换不能重新批准 pin' 1 @($archivePath, $sidePath, $metadataPath, $extractManaged, $publishManaged) -Action {
        param($details)
        $attackPlan = @($dotnetPlan | ForEach-Object { [pscustomobject]@{source=$_.source; bytes=[byte[]]$_.bytes.Clone()} })
        $attackPlan[0].bytes[0] = $attackPlan[0].bytes[0] -bxor 1
        $changed = New-FixedZipBytes (@($attackPlan) + @($dotnetMetadata))
        $newHash = Sha512 $changed
        Write-FixtureBytes $archivePath $changed
        Write-FixtureText $sidePath $newHash
        $m = Read-FixtureJson $metadataPath
        $m.contentHash = $newHash
        Write-FixtureJson $metadataPath $m
        Write-FixtureBytes $extractManaged $attackPlan[0].bytes
        Write-FixtureBytes $publishManaged $attackPlan[0].bytes
        $details.changedContentHash = $newHash
        $details.fixedContentHash = $dotnetPin.contentHash
        $details.attackBytesAllInsideFixture = $true
    } -Needles @('可信包内容/归档与当前 restore 不符')
    Run-Case '.NET ZIP 缺 managed entry，同步 sideHash 仍不能通过批准内容摘要' 1 @($archivePath, $sidePath) -Action {
        $changed = New-FixedZipBytes (@($dotnetPlan | Where-Object { $_.path -cne 'R067.DotNet.ManagedA.dll' }) + @($dotnetMetadata))
        Write-FixtureBytes $archivePath $changed
        Write-FixtureText $sidePath (Sha512 $changed)
    } -Needles @('NuGet 实际包内容摘要与独立 runtimepack 批准', '不符：Microsoft.NETCore.App.Runtime.win-x64/10.0.11')

    foreach ($exe in @(
        @{name='主 Apphost'; source=$apphost; published=$apphostPublish},
        @{name='合法 Foundation RestartAgent'; source=$restartExtract; published=$restartPublish}
    )) {
        $currentExe = $exe
        Run-Case ($currentExe.name + ' 发布错字节拒绝') 1 @($currentExe.published) -Action {
            Corrupt-Bytes $currentExe.published
        } -Needles @('运行时载荷字节与可信固定包/当前编译资产不符：', [IO.Path]::GetFileName($currentExe.published))
    }
    Run-Case '主 Apphost 缺可信 build 源不能只信发布 EXE' 1 @($apphost) -Action {
        Remove-FixtureFile $apphost
    } -Needles @('出现清单之外的运行时载荷：Momoka.exe')
    Run-Case 'RestartAgent extract 错字节拒绝' 1 @($restartExtract) -Action {
        Corrupt-Bytes $restartExtract
    } -Needles @('恢复资产与固定归档字节不符：', 'RestartAgent.exe')
    Run-Case 'RestartAgent extract/pub 一起改字节也拒绝' 1 @($restartExtract, $restartPublish) -Action {
        Corrupt-Bytes $restartExtract
        Write-FixtureBytes $restartPublish ([IO.File]::ReadAllBytes($restartExtract))
    } -Needles @('恢复资产与固定归档字节不符：', 'RestartAgent.exe')
    foreach ($relative in @('R067.Unknown.exe', 'sub/createdump.exe', 'sub/RestartAgent.exe', 'sub/Momoka.exe', 'shaders/R067.Unknown.exe', 'R067.Unknown.dll', 'sub/R067.DotNet.ManagedA.dll')) {
        $extraRelative = $relative
        $extraPath = Fixture-Path $fixture.publish $extraRelative
        Run-Case ('发布额外或越界 DLL/EXE 拒绝：' + $extraRelative) 1 @($extraPath) -Action {
            $bytes = if ($extraRelative.EndsWith('createdump.exe')) { $dotnetPlan[3].bytes }
                elseif ($extraRelative.EndsWith('RestartAgent.exe')) { $foundationPlan[1].bytes }
                elseif ($extraRelative.EndsWith('Momoka.exe')) { [IO.File]::ReadAllBytes($apphost) }
                else { $assetEncoding.GetBytes('R067 unknown NON-PE executable/DLL -- never launch/load') }
            Write-FixtureBytes $extraPath $bytes -New
        } -Needles @('出现清单之外的运行时载荷：', $extraRelative)
    }
    # 校验实际 Shell 引用副本/anchored Core 输出，不由 pub 同名 DLL 或时间戳任选来源。
    Run-Case 'Core Shell 引用副本错字节拒绝，即便普通 Core 输出存在' 1 @($coreCopy) -Action {
        Corrupt-Bytes $coreCopy
    } -Needles @('Shell 引用副本与可信 Core 编译资产不符')
    Run-Case 'Core publish 错字节与当前 Shell 引用副本不符拒绝' 1 @($corePublish) -Action {
        Corrupt-Bytes $corePublish
    } -Needles @('运行时载荷字节与可信固定包/当前编译资产不符：Momoka.Core.dll')
    Run-Case 'Core 已有但错误 Shell 引用副本不能借普通 fallback 或同改 pub 洗白' 1 @($coreCopy, $corePublish) -Action {
        Corrupt-Bytes $coreCopy
        Write-FixtureBytes $corePublish ([IO.File]::ReadAllBytes($coreCopy))
    } -Needles @('Shell 引用副本与可信 Core 编译资产不符')
    Run-Case 'Core 普通 anchored 输出不同，Shell 副本匹配 x64 anchored 输出仍成功' 0 @($coreX64, $coreAnyCpu) -Action {
        Write-FixtureBytes $coreX64 ([IO.File]::ReadAllBytes($coreCopy)) -New
        Corrupt-Bytes $coreAnyCpu
    } -Needles @('发布验证通过：')
    Run-Case 'Core 无 Shell copied 输出时只允许普通 anchored project fallback' 0 @($coreCopy) -Action {
        Remove-FixtureFile $coreCopy
    } -Needles @('发布验证通过：')
    # New-MpvPayloadFixture 的普通 SDK copied-on-demand DLL 默认就未发布；保持缺省可选状态。
    Run-Case '普通 SDK copied-on-demand DLL 保持缺省缺失，不抢 runtimepack 必需资产错误' 0 -Action {
        if (Test-Path -LiteralPath (Fixture-Path $fixture.publish 'Microsoft.UI.Xaml.Controls.dll')) { throw '此正例要求保留既有 fixture 的默认可选缺失。' }
    } -Needles @('发布验证通过：')
    # 第一写入前必须拒绝重解析祖先；junction 和 outside sentinel 都仅建在本轮 fixture 内。
    foreach ($linkLevel in @('artifacts', 'artifacts/dn')) {
        $entryRoot = Fixture-Path $fixture.fixture ('entry-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $entryTools = Fixture-Path $entryRoot 'tools'
        New-FixtureDirectory $entryTools -Fresh
        foreach ($name in @('test-mpv-dotnet-runtimepack-content.ps1', 'mpv-payload-common.ps1', 'mpv-payload-fixture.ps1')) {
            $source = Get-ChildPath $repo ('tools/' + $name)
            Write-FixtureBytes (Fixture-Path $entryTools $name) ([IO.File]::ReadAllBytes($source)) -New
        }
        $link = Fixture-Path $entryRoot $linkLevel
        New-FixtureDirectory ([IO.Path]::GetDirectoryName($link))
        $null = Assert-FixturePath $sentinelRoot
        New-Item -ItemType Junction -Path $link -Target $sentinelRoot | Out-Null
        try {
            $entryScript = Fixture-Path $entryTools 'test-mpv-dotnet-runtimepack-content.ps1'
            Run-Case ('新脚本第一 mkdir 前拒绝重解析祖先、sentinel 零变化：' + $linkLevel) 1 -Operation {
                Invoke-Tool @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $entryScript, '-Phase', 'entry-boundary')
            } -Needles @('拒绝重解析路径') -Kind 'script-entry-boundary'
        } finally {
            # 不递归、不跟随 junction 删除目录；先复核绝对链接位置及普通父目录仍在本轮 fixture 内。
            $absoluteLink = [IO.Path]::GetFullPath($link)
            if (-not $absoluteLink.StartsWith($fixture.fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '待移除 junction 越界。' }
            $null = Assert-FixturePath ([IO.Path]::GetDirectoryName($absoluteLink))
            $item = Get-Item -LiteralPath $absoluteLink -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -or -not $item.PSIsContainer) { throw '待移除对象不是此轮 junction。' }
            [IO.Directory]::Delete($absoluteLink)
        }
    }
    # 合成 CMS 只在内存签名，证书/私钥不写盘、不导入 store，不做链/吊销/时间戳检查。
    # 正例和保留旧签名的 entry 篡改反例实际调用原生产 SDK helper，不拿假的 .signature 充覆盖。
    $signedRoot = Fixture-Path $fixture.repository 'artifacts/s'
    New-FixtureDirectory $signedRoot -Fresh
    $unsigned = Fixture-Path $signedRoot 'unsigned.nupkg'
    $signed = Fixture-Path $signedRoot 'signed.nupkg'
    Write-FixtureBytes $unsigned $dotnetZip -New
    $signer = Fixture-Path $tools 'synthetic-sign.ps1'
    $signerText = @'
param([string]$Unsigned, [string]$Signed)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
    . (Join-Path $PSScriptRoot 'mpv-nuget-content-hash.ps1')
    $repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
    $scratch = Get-ChildPath $repository 'artifacts'
    foreach ($target in @($Unsigned, $Signed)) {
        Assert-PlainPath $target
        if (-not [IO.Path]::GetFullPath($target).StartsWith($scratch + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Signing scratch path escaped fixture repository.' }
    }
    $null = Get-MpvNuGetPackageContentHash $Unsigned
    $sdk = Join-Path (Join-Path $env:USERPROFILE '.dotnet') 'sdk/10.0.400/Sdks/Microsoft.NET.Sdk/tools/net472'
    foreach ($name in @('System.Memory', 'System.Buffers', 'System.Runtime.CompilerServices.Unsafe',
        'System.Threading.Tasks.Extensions', 'System.Collections.Immutable', 'NuGet.Common', 'NuGet.Versioning', 'NuGet.Frameworks', 'NuGet.Packaging')) {
        $file = Join-Path $sdk ($name + '.dll')
        Assert-PlainPath $file
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing fixed SDK managed library: $name" }
        $null = [Reflection.Assembly]::LoadFrom($file)
    }
    $source = @"
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace Momoka.OfflineFixture
{
    public static class SignatureWriter
    {
        private static string Root;
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            switch (name)
            {
                case "System.Memory": case "System.Buffers": case "System.Runtime.CompilerServices.Unsafe":
                case "System.Threading.Tasks.Extensions": case "System.Collections.Immutable":
                case "NuGet.Common": case "NuGet.Versioning": case "NuGet.Frameworks": case "NuGet.Packaging":
                    string path = Path.Combine(Root, name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                default: return null;
            }
        }
        public static void Add(string root, string inputPath, string outputPath, byte[] signature)
        {
            Root = root;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try
            {
                Assembly assembly = Assembly.LoadFrom(Path.Combine(root, "NuGet.Packaging.dll"));
                Type type = assembly.GetType("NuGet.Packaging.Signing.SignedPackageArchive", true);
                using (Stream input = File.OpenRead(inputPath))
                using (Stream output = File.Open(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (IDisposable package = (IDisposable)Activator.CreateInstance(type, new object[] { input, output }))
                using (Stream stream = new MemoryStream(signature, false))
                {
                    Task task = (Task)type.GetMethod("AddSignatureAsync").Invoke(package,
                        new object[] { stream, CancellationToken.None });
                    task.GetAwaiter().GetResult();
                }
            }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; }
        }
    }
}
"@
    $temp = Get-ChildPath $repository 'artifacts/n'
    [IO.Directory]::CreateDirectory($temp) | Out-Null
    $parameters = [CodeDom.Compiler.CompilerParameters]::new()
    $parameters.GenerateInMemory = $true
    $parameters.TempFiles = [CodeDom.Compiler.TempFileCollection]::new($temp)
    foreach ($reference in @('System.dll', 'System.Core.dll')) { $null = $parameters.ReferencedAssemblies.Add($reference) }
    Add-Type -TypeDefinition $source -CompilerParameters $parameters
    Add-Type -AssemblyName System.Security
    $rsa = [Security.Cryptography.RSACng]::new(2048)
    try {
        if (-not $rsa.Key.IsEphemeral) { throw 'Fixture signing key must stay in memory, never use certificate stores.' }
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=R067 SYNTHETIC OFFLINE NEVER TRUST',
            $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(1))
        try {
            $reader = [NuGet.Packaging.PackageArchiveReader]::new($Unsigned, $null, $null)
            try { $digest = $reader.GetArchiveHashAsync([NuGet.Common.HashAlgorithmName]::SHA256, [Threading.CancellationToken]::None).GetAwaiter().GetResult() }
            finally { $reader.Dispose() }
            $content = [NuGet.Packaging.Signing.SignatureContent]::new([NuGet.Packaging.Signing.SigningSpecifications]::V1,
                [NuGet.Common.HashAlgorithmName]::SHA256, [Convert]::ToBase64String($digest))
            $cms = [Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new($content.GetBytes()), $false)
            $signer = [Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
            $signer.IncludeOption = [Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
            $signer.DigestAlgorithm = [Security.Cryptography.Oid]::new('2.16.840.1.101.3.4.2.1')
            $author = [NuGet.Packaging.Signing.AuthorSignPackageRequest]::new($certificate, [NuGet.Common.HashAlgorithmName]::SHA256)
            try {
                # Only creates CMS signed attributes from supplied certificate objects; no SigningUtility.Verify,
                # X509Chain, timestamp provider, signer-chain/revocation check, store import or network calls.
                $certificates = [Security.Cryptography.X509Certificates.X509Certificate2[]]@($certificate)
                $attributes = [NuGet.Packaging.Signing.SigningUtility]::CreateSignedAttributes($author, $certificates)
                foreach ($attribute in $attributes) { $null = $signer.SignedAttributes.Add($attribute) }
                $cms.ComputeSignature($signer, $true)
                $signatureBytes = $cms.Encode()
                [Momoka.OfflineFixture.SignatureWriter]::Add($sdk, $Unsigned, $Signed, $signatureBytes)
            } finally { $author.Dispose() }
            $hash = Get-MpvNuGetPackageContentHash $Signed
            Write-Output (ConvertTo-Json -InputObject ([ordered]@{contentHash=$hash; signer='ephemeral synthetic self-signed CMS';
                noTrustStore=$true; noNetwork=$true; localSignedIntegrity=$true}) -Compress)
        } finally { $certificate.Dispose() }
    } finally { $rsa.Dispose() }
    exit 0
} catch { Write-Output $_.Exception.ToString(); exit 1 }

'@
    Write-FixtureText $signer $signerText -New
    Run-Case '纯合成非 PE .NET ZIP 创建本地签名并通过实际 SDK integrity（不访问 cache/store/network）' 0 @($signed) -Action {
        param($details)
        $details.signResult = Invoke-Tool @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $signer, '-Unsigned', $unsigned, '-Signed', $signed)
    } -Operation { param($details); return $details.signResult } -AssertResult {
        param($r)
        $data = ConvertFrom-Json -InputObject $r.output
        return $data.localSignedIntegrity -and $data.noTrustStore -and $data.noNetwork -and -not [string]::IsNullOrWhiteSpace($data.contentHash)
    } -Kind 'official-sdk-synthetic-signature'
    # Run-Case 将新建文件恢复为不存在；从该用例完整保留的 mutated 字节取回签名夹具，不更新独立 pin。
    $signatureCase = $results[$results.Count - 1]
    if (-not $signatureCase.passed) { throw '真实 SDK 合成签名 fixture 建立失败，不能跳过签名完整性回归。' }
    $signedArtifact = Fixture-Path ([IO.Path]::GetDirectoryName($signatureCase.log)) 'mutated-1.bin'
    Write-FixtureBytes $signed ([IO.File]::ReadAllBytes($signedArtifact)) -New
    $signedHashResult = Invoke-Probe Hash '' $signed
    if ($signedHashResult.exitCode -ne 0) { throw '签名 fixture 正例无法取得官方内容摘要。' }
    $signedExpected = (ConvertFrom-Json -InputObject $signedHashResult.output).contentHash
    $signedRaw = Sha512 ([IO.File]::ReadAllBytes($signed))
    if ($signedExpected -ceq $signedRaw -or $signedExpected -cne $dotnetPin.contentHash) { throw '签名包实际内容摘要必须保持独立固定计划 pin，不能用整归档 raw SHA512 替代。' }
    Run-Case '合成签名包 SDK 内容摘要不同于 raw archive SHA512，完整签名仍成功' 0 -Operation {
        Invoke-Probe Hash '' $signed
    } -AssertResult {
        param($r)
        return (ConvertFrom-Json -InputObject $r.output).contentHash -ceq $signedExpected
    } -Kind 'official-sdk-synthetic-signature'
    Run-Case '合成签名包 entry 改字节、保旧真实 CMS 签名，SDK 本地 integrity 必须拒绝' 1 @($signed) -Action {
        $memory = [IO.MemoryStream]::new()
        $original = [IO.File]::ReadAllBytes($signed)
        $memory.Write($original, 0, $original.Length)
        $memory.Position = 0
        $zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Update, $true)
        try {
            if ($null -eq $zip.GetEntry('.signature.p7s')) { throw '必须保留实际 NuGet primary signature。' }
            $entry = $zip.GetEntry($dotnetPlan[0].source)
            if ($null -eq $entry) { throw '合成签名包缺固定 managed entry。' }
            $entry.Delete()
            $entry = $zip.CreateEntry($dotnetPlan[0].source)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 10, 24, 0, 0, 0, [TimeSpan]::Zero)
            $bytes = [byte[]]$dotnetPlan[0].bytes.Clone()
            $bytes[0] = $bytes[0] -bxor 1
            $stream = $entry.Open()
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        } finally { $zip.Dispose() }
        try { $changed = $memory.ToArray() } finally { $memory.Dispose() }
        Write-FixtureBytes $signed $changed
    } -Operation { Invoke-Probe Hash '' $signed } -Needles @('NuGet 实际包内容校验失败', 'The package integrity check failed.', 'changed since it was signed') -Kind 'official-sdk-synthetic-signature'
    Run-Case '合成签名 fixture 恢复后官方 SDK 完整性再次成功' 0 -Operation {
        Invoke-Probe Hash '' $signed
    } -AssertResult {
        param($r)
        return (ConvertFrom-Json -InputObject $r.output).contentHash -ceq $signedExpected
    } -Kind 'official-sdk-synthetic-signature'
    Run-Case '最终双 pack/完整 mappings/独立 pin/固定 native/全部 EXE 恢复正例' 0 -Needles @('发布验证通过：')
    Finish-Run
} catch {
    $failure = $_.Exception.ToString()
    Write-FixtureText (Fixture-Path $run 'harness-failure.log') $failure -New
    if (-not (Test-Path -LiteralPath (Fixture-Path $run 'results.json'))) {
        Write-FixtureJson (Fixture-Path $run 'results.json') ([ordered]@{phase=$Phase; total=$results.Count;
            passed=@($results | Where-Object passed).Count; failed=@($results | Where-Object { -not $_.passed }).Count;
            skipped=0; incomplete=$true; harnessFailure=$failure; cases=$results.ToArray()}) -New
        Write-FixtureText (Fixture-Path $run 'exitcode.txt') '1' -New
    }
    Write-Output $failure
    Write-Output "离线回归未完成，保留全部证据：$run"
    exit 1
}
