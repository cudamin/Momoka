#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^[a-z0-9-]+$')][string]$Phase = 'green')

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/review-batch10/installer-' + $Phase + '-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($true)
. (Join-Path $PSScriptRoot 'mpv-payload-common.ps1')
. (Join-Path $PSScriptRoot 'mpv-payload-fixture.ps1')
$fixture = New-MpvPayloadFixture $repo (Join-Path $run 'synthetic')
if (-not $fixture.nativeIsSynthetic) { throw '只接受固定非 PE 夹具。' }
$tools = Join-Path $fixture.repository 'tools'
[IO.Directory]::CreateDirectory($tools) | Out-Null
foreach ($file in @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.ps1')) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $tools $file.Name)
}
foreach ($relative in @('installer/Momoka.iss','installer/Languages/ChineseSimplified.isl')) {
    $target = Join-Path $tools $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relative) -Destination $target
}
# This observer forwards to the unchanged complete verifier, including all native,
# dependency, resource and license checks. It only records the input mode/order.
Move-Item -LiteralPath (Join-Path $tools 'verify-publish.ps1') -Destination (Join-Path $tools 'verify-publish-real.ps1')
[IO.File]::WriteAllText((Join-Path $tools 'verify-publish.ps1'), @'
[CmdletBinding()]
param([string]$PublishRoot,[string]$RepositoryRoot,[switch]$SelfContained)
[IO.File]::AppendAllText($env:MOMOKA_INSTALLER_EVENTS, "verify:$($SelfContained.IsPresent)`n")
& (Join-Path $PSScriptRoot 'verify-publish-real.ps1') @PSBoundParameters
'@ + "`n", $utf8)
$iscc = Join-Path $run 'anonymous-iscc.ps1'
[IO.File]::WriteAllText($iscc, @'
[IO.File]::AppendAllText($env:MOMOKA_INSTALLER_EVENTS, "iscc`n")
$arguments = if ($args.Count -eq 1 -and $args[0] -is [array]) { @($args[0]) } else { @($args) }
[IO.File]::WriteAllText($env:MOMOKA_INSTALLER_ISCC_ARGS, ($arguments | ConvertTo-Json))
$version = [string]$arguments[0]
if (-not $version.StartsWith('/DMyAppVersion=')) { throw 'wrong version argument' }
$version = $version.Substring('/DMyAppVersion='.Length)
$root = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $arguments[-1]) '../..'))
[IO.File]::WriteAllText((Join-Path $root "artifacts/Momoka_windows-x64_$version.exe"), 'anonymous NON-PE installer sentinel; never execute')
$global:LASTEXITCODE = 0
'@ + "`n", $utf8)
$dotnet = Join-Path $run 'anonymous-dotnet.ps1'
[IO.File]::WriteAllText($dotnet, @'
if ($args[0] -eq '--version') { $global:LASTEXITCODE=0; Write-Output '10.0.400'; return }
if ($args[0] -ne 'publish') { throw 'unexpected dotnet command' }
[IO.File]::AppendAllText($env:MOMOKA_INSTALLER_EVENTS, "publish`n")
[IO.File]::WriteAllText($env:MOMOKA_INSTALLER_DOTNET_ARGS, ($args | ConvertTo-Json))
if ($env:MOMOKA_INSTALLER_PUBLISH_FAIL -eq '1') { $global:LASTEXITCODE=9; return }
$output = $args[[array]::IndexOf($args, '-o') + 1]
$mode = $args[[array]::IndexOf($args, '--self-contained') + 1]
$seed = if ($mode -eq 'true') { $env:MOMOKA_INSTALLER_SEED_SC } elseif ($mode -eq 'false') { $env:MOMOKA_INSTALLER_SEED_FD } else { throw 'missing self-contained mode' }
[IO.Directory]::CreateDirectory($output) | Out-Null
Get-ChildItem -LiteralPath $seed -Force | Copy-Item -Destination $output -Recurse
$global:LASTEXITCODE=0
'@ + "`n", $utf8)
$runner = Join-Path $run 'runner.ps1'
[IO.File]::WriteAllText($runner, @'
param([string]$CasePath)
$ErrorActionPreference='Stop'
$case=Get-Content -LiteralPath $CasePath -Raw | ConvertFrom-Json
$env:MOMOKA_INSTALLER_EVENTS=$case.events
$env:MOMOKA_INSTALLER_ISCC_ARGS=$case.isccArgs
$env:MOMOKA_INSTALLER_DOTNET_ARGS=$case.dotnetArgs
$env:MOMOKA_INSTALLER_SEED_SC=$case.seedSC
$env:MOMOKA_INSTALLER_SEED_FD=$case.seedFD
$env:MOMOKA_INSTALLER_PUBLISH_FAIL=$case.publishFail
$env:DOTNET_HOST_PATH=$case.dotnet
$flags=@{ISCCPath=$case.iscc}
if ($case.skip) { $flags.SkipPublish=$true }
if ($case.fd) { $flags.FrameworkDependent=$true }
try { & $case.installer @flags; exit 0 } catch { Write-Output $_; exit 1 }
'@ + "`n", $utf8)
$seedSC = $fixture.publish
$seedFD = Join-Path $run 'seed-fd'
[IO.Directory]::CreateDirectory($seedFD) | Out-Null
Get-ChildItem -LiteralPath $seedSC -Force | Copy-Item -Destination $seedFD -Recurse
$project = [xml][IO.File]::ReadAllText((Join-Path $fixture.repository 'src/Momoka.Shell/Momoka.Shell.csproj'))
$tfm = $project.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
$trusted = Join-Path $fixture.repository "src/Momoka.Shell/bin/x64/Release/$tfm/win-x64/Momoka.deps.json"
$depsSC = [IO.File]::ReadAllText($trusted)
# Independent FD graph: no hostfxr or bootstrap mapping; SDK/framework optional
# payloads remain governed by the real verifier. Never derive approval from a mutation.
$depsFD = $depsSC | ConvertFrom-Json
$active = '.NETCoreApp,Version=v10.0/win-x64'
$depsFD.targets.$active.PSObject.Properties.Remove('R067.Offline.Native/1.0.0')
$depsFD.libraries.PSObject.Properties.Remove('R067.Offline.Native/1.0.0')
$depsFD.targets.$active.'Microsoft.WindowsAppSDK.Foundation/2.3.12'.PSObject.Properties.Remove('native')
$depsFDText = $depsFD | ConvertTo-Json -Depth 16
[IO.File]::WriteAllText((Join-Path $seedFD 'Momoka.deps.json'), $depsFDText, $utf8)
foreach ($name in @('hostfxr.dll','Microsoft.WindowsAppRuntime.Bootstrap.dll')) { Remove-Item -LiteralPath (Join-Path $seedFD $name) }
$publish = Join-Path $fixture.repository 'artifacts/publish/win-x64'
$props = [xml][IO.File]::ReadAllText((Join-Path $fixture.repository 'Directory.Build.props'))
$version = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$setup = Join-Path $fixture.repository "artifacts/Momoka_windows-x64_$version.exe"
$results = [Collections.Generic.List[object]]::new()
function Check([bool]$Condition,[string]$Name) {
    $results.Add([pscustomobject]@{name=$Name;passed=$Condition})
    Write-Output ("[{0}] {1}" -f $(if ($Condition) { '通过' } else { '失败' }), $Name)
}
function Reset-Publish([bool]$FD) {
    $full = [IO.Path]::GetFullPath($publish)
    if (-not $full.StartsWith($run + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '拒绝夹具范围外清理。' }
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse }
    [IO.Directory]::CreateDirectory($publish) | Out-Null
    $seed = if ($FD) { $seedFD } else { $seedSC }
    Get-ChildItem -LiteralPath $seed -Force | Copy-Item -Destination $publish -Recurse
    [IO.File]::WriteAllText($trusted, $(if ($FD) { $depsFDText } else { $depsSC }), $utf8)
    if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup }
}
$cases = @(
    @{name='complete-sc';skip=$true;exit=0;mode='True'},
    @{name='complete-fd';skip=$true;fd=$true;exit=0;mode='False'},
    @{name='wrong-mode';skip=$true;seedFD=$true;exit=1;mode='True'},
    @{name='only-exe';skip=$true;onlyExe=$true;exit=1;mode='True'},
    @{name='missing-exe';skip=$true;missingFile='Momoka.exe';exit=1;mode='none'},
    @{name='missing-core';skip=$true;missingFile='Momoka.Core.dll';exit=1;mode='True'},
    @{name='missing-main';skip=$true;missingFile='Momoka.dll';exit=1;mode='True'},
    @{name='missing-libmpv';skip=$true;missingFile='libmpv-2.dll';exit=1;mode='True'},
    @{name='missing-vulkan';skip=$true;missingFile='vulkan-1.dll';exit=1;mode='True'},
    @{name='missing-license';skip=$true;missingFile='LICENSE.LGPL-3.0.txt';exit=1;mode='True'},
    @{name='missing-pri';skip=$true;missingFile='Momoka.pri';exit=1;mode='True'},
    @{name='changed-exe';skip=$true;alter='Momoka.exe';exit=1;mode='True'},
    @{name='changed-native';skip=$true;alter='libmpv-2.dll';exit=1;mode='True'},
    @{name='publish-sc';exit=0;mode='True'},
    @{name='publish-fd';fd=$true;exit=0;mode='False'},
    @{name='publish-failure';publishFail='1';exit=1;mode='none'}
)
foreach ($hostPath in @((Get-Command powershell.exe).Source,(Get-Command pwsh.exe).Source)) {
    $hostName=[IO.Path]::GetFileNameWithoutExtension($hostPath)
    foreach ($scenario in $cases) {
        Reset-Publish ([bool]($scenario.fd -or $scenario.seedFD))
        if ($scenario.onlyExe) {
            Get-ChildItem -LiteralPath $publish -Force | Where-Object Name -ne 'Momoka.exe' | Remove-Item -Recurse
        }
        if ($scenario.missingFile) { Remove-Item -LiteralPath (Join-Path $publish $scenario.missingFile) }
        if ($scenario.alter) { [IO.File]::AppendAllText((Join-Path $publish $scenario.alter), ' simulated signature or corrupted bytes') }
        $before = @(Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
        $directory=Join-Path $run ($hostName+'-'+$scenario.name)
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $events=Join-Path $directory 'events.txt'; [IO.File]::WriteAllText($events,'')
        $data=@{installer=(Join-Path $tools 'installer.ps1');iscc=$iscc;dotnet=$dotnet;seedSC=$seedSC;seedFD=$seedFD;events=$events;
            skip=[bool]$scenario.skip;fd=[bool]$scenario.fd;publishFail=$scenario.publishFail;isccArgs=(Join-Path $directory 'iscc-args.json');dotnetArgs=(Join-Path $directory 'dotnet-args.json')}
        $casePath=Join-Path $directory 'case.json'
        [IO.File]::WriteAllText($casePath,($data|ConvertTo-Json),$utf8)
        $saved=$ErrorActionPreference
        try { $ErrorActionPreference='Continue'; & $hostPath -NoProfile -ExecutionPolicy Bypass -File $runner -CasePath $casePath *> (Join-Path $directory 'output.log'); $code=$LASTEXITCODE }
        finally { $ErrorActionPreference=$saved }
        $eventLines=@([IO.File]::ReadAllLines($events))
        $prefix="$hostName $($scenario.name)"
        Check ($code -eq $scenario.exit) "$prefix 退出码预期 $($scenario.exit) 实际 $code"
        Check (@($eventLines | Where-Object { $_ -eq 'iscc' }).Count -eq $(if ($scenario.exit -eq 0) { 1 } else { 0 })) "$prefix 编译器调用次数"
        $verifyLines=@($eventLines | Where-Object { $_ -like 'verify:*' })
        Check ($(if ($scenario.mode -eq 'none') { $verifyLines.Count -eq 0 } else { $verifyLines.Count -eq 1 -and $verifyLines[0] -eq ('verify:'+$scenario.mode) })) "$prefix 完整验收模式"
        Check ((Test-Path -LiteralPath $setup) -eq ($scenario.exit -eq 0)) "$prefix 匿名安装产物"
        if ($scenario.skip) {
            $after=@(Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
            Check ($before -eq $after) "$prefix 待验文件字节不变"
        } else {
            $arguments=Get-Content -LiteralPath $data.dotnetArgs -Raw | ConvertFrom-Json
            Check ($arguments[[array]::IndexOf($arguments,'--self-contained')+1] -eq $(if ($scenario.fd) { 'false' } else { 'true' })) "$prefix 发布模式传递"
            Check (-not @(Get-ChildItem -LiteralPath (Join-Path $fixture.repository 'artifacts') -Filter '*.zip').Count) "$prefix NoArchive 保持"
        }
        if ($scenario.exit -eq 0) {
            Check ($eventLines[-2] -like 'verify:*' -and $eventLines[-1] -eq 'iscc') "$prefix 验收在编译前"
        }
    }
}
$failed=@($results|Where-Object { -not $_.passed }).Count
$receipt=@{phase=$Phase;total=$results.Count;failed=$failed;nativeIsSynthetic=$true;installerSha256=(Get-FileHash -LiteralPath (Join-Path $tools 'installer.ps1')).Hash;results=@($results)}
[IO.File]::WriteAllText((Join-Path $run 'results.json'),($receipt|ConvertTo-Json -Depth 8),$utf8)
Write-Output "$($results.Count-$failed)/$($results.Count)，失败 $failed；证据 $run"
if ($failed) { exit 1 }
