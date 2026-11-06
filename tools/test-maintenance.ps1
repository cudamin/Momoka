[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$root = Join-Path $repo ('artifacts\tool-tests\' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$utf8 = [System.Text.UTF8Encoding]::new($true)
$passed = 0

function Save-Script([string]$File, [string]$Text) {
    [System.IO.File]::WriteAllText($File, ($Text -replace "`r`n", "`n") + "`n", $utf8)
}

function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "[失败] $Name" }
    $script:passed++
    Write-Output "[通过] $Name"
}

function Run-Script([string]$File, [string[]]$Arguments = @()) {
    # A failing child script must not abort the harness before its exit code is checked.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $File @Arguments 2>&1)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    } finally {
        $ErrorActionPreference = $saved
    }
}

$checker = Join-Path $PSScriptRoot 'check-scripts.ps1'
$good = Join-Path $root 'good.ps1'
Save-Script $good "Write-Output 'fixture'"
Check ((Run-Script $checker @('-Path', $good)).ExitCode -eq 0) '脚本检查接受 BOM/LF 和合法语法'

$noBom = Join-Path $root 'no-bom.ps1'
[System.IO.File]::WriteAllText($noBom, "Write-Output 'fixture'`n", [System.Text.UTF8Encoding]::new($false))
Check ((Run-Script $checker @('-Path', $noBom)).ExitCode -ne 0) '脚本检查拒绝无 BOM'

$crlf = Join-Path $root 'crlf.ps1'
[System.IO.File]::WriteAllText($crlf, "Write-Output 'fixture'`r`n", $utf8)
Check ((Run-Script $checker @('-Path', $crlf)).ExitCode -ne 0) '脚本检查拒绝 CRLF'

$badSyntax = Join-Path $root 'bad-syntax.ps1'
Save-Script $badSyntax 'if ('
Check ((Run-Script $checker @('-Path', $badSyntax)).ExitCode -ne 0) '脚本检查拒绝无效语法'

$fixture = Join-Path $root 'publish fixture'
foreach ($directory in @('tools', 'src\Momoka.Shell', 'assets\shaders', 'assets\mpv-runtime')) {
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish.ps1') -Destination (Join-Path $fixture 'tools\publish.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mpv-payload-common.ps1') -Destination (Join-Path $fixture 'tools\mpv-payload-common.ps1')
[System.IO.File]::WriteAllText((Join-Path $fixture 'Directory.Build.props'), '<Project><PropertyGroup><Version>0.0.0</Version></PropertyGroup></Project>')
foreach ($file in @('src\Momoka.Shell\Momoka.Shell.csproj', 'libmpv-2.dll', 'assets\mpv-runtime\vulkan-1.dll')) {
    [System.IO.File]::WriteAllText((Join-Path $fixture $file), 'fixture')
}
Save-Script (Join-Path $fixture 'tools\verify-publish.ps1') '$global:LASTEXITCODE = 0'
$fakeDotnet = Join-Path $fixture 'dotnet.ps1'
Save-Script $fakeDotnet @'
if ($args -contains '--version') {
    Write-Output '10.0.400'
    $global:LASTEXITCODE = 0
    return
}
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'publish-args.json'), (ConvertTo-Json -InputObject @($args)))
$index = [array]::IndexOf($args, '-o')
$destination = $args[$index + 1]
$null = New-Item -ItemType Directory -Path $destination -Force
[System.IO.File]::WriteAllText((Join-Path $destination 'Momoka.exe'), 'fixture')
$global:LASTEXITCODE = 0
'@

$publish = Join-Path $fixture 'tools\publish.ps1'
$result = Run-Script $publish @('-NoArchive', '-DotnetPath', $fakeDotnet)
Check ($result.ExitCode -eq 0) '发布脚本可通过隔离 SDK 夹具执行'
$arguments = Get-Content -LiteralPath (Join-Path $fixture 'publish-args.json') -Raw | ConvertFrom-Json
foreach ($flag in @('-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')) {
    Check ($arguments -contains $flag) "发布向 SDK 传递 $flag"
}

$guard = Join-Path $fixture 'guard.ps1'
Save-Script $guard @'
function Get-Process {
    [pscustomobject]@{ Path = (Join-Path $PSScriptRoot 'artifacts\publish\win-x64\Momoka.exe') }
}
try {
    & (Join-Path $PSScriptRoot 'tools\publish.ps1') -NoArchive -DotnetPath (Join-Path $PSScriptRoot 'dotnet.ps1')
} catch {
    Write-Output $_.Exception.Message
    exit 17
}
exit 0
'@
$sentinel = Join-Path $fixture 'artifacts\publish\win-x64\preserve.txt'
[System.IO.File]::WriteAllText($sentinel, 'keep')
$result = Run-Script $guard
Check ($result.ExitCode -eq 17) '目标程序在运行时发布提前拒绝'
Check ((Test-Path -LiteralPath $sentinel) -and [System.IO.File]::ReadAllText($sentinel) -eq 'keep') '发布拒绝时保留原输出目录'

# Run the full production script; only SDK/verifier endpoints above are anonymous. A junction
# and its destination both stay inside this new fixture, and cleanup never follows the junction.
foreach ($level in @('output', 'publish', 'msix-stage', 'target', 'nested')) {
    $outside = Join-Path $root ("outside-$level")
    $output = Join-Path $root ("output-$level")
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    $link = switch ($level) {
        'output' { $output }
        'publish' { Join-Path $output 'publish' }
        'msix-stage' { Join-Path $output 'msix-stage' }
        'target' { Join-Path $output 'publish\win-x64' }
        'nested' { Join-Path $output 'publish\win-x64\nested' }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($link)) | Out-Null
    $relative = if ($level -in @('publish', 'msix-stage')) { 'win-x64\preserve.txt' } else { 'preserve.txt' }
    $marker = Join-Path $outside $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($marker)) | Out-Null
    [IO.File]::WriteAllText($marker, 'outside bytes')
    $earlier = Join-Path $output 'msix-stage\win-x64\preserve.txt'
    if ($level -eq 'nested') {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($earlier)) | Out-Null
        [IO.File]::WriteAllText($earlier, 'earlier output bytes')
    }
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    try {
        $result = Run-Script $publish @('-NoArchive', '-DotnetPath', $fakeDotnet, '-OutputRoot', $output)
        Check ($result.ExitCode -ne 0 -and $result.Output.Contains('重解析')) "发布提前拒绝 $level 联接"
        Check ((Test-Path -LiteralPath $marker) -and [IO.File]::ReadAllText($marker) -eq 'outside bytes') "发布保留 $level 联接外哨兵"
        if ($level -eq 'nested') {
            Check ((Test-Path -LiteralPath $earlier) -and [IO.File]::ReadAllText($earlier) -eq 'earlier output bytes') '全部路径预检失败时也保留先处理的普通输出'
        }
    } finally {
        # Directory.Delete without recursion removes the link only, even if the assertion failed.
        if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    }
}

$ordinary = Join-Path $root 'ordinary-output'
$oldPublish = Join-Path $ordinary 'publish\win-x64\stale.txt'
$oldStage = Join-Path $ordinary 'msix-stage\win-x64\stale.txt'
foreach ($file in @($oldPublish, $oldStage)) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
    [IO.File]::WriteAllText($file, 'stale bytes')
}
$result = Run-Script $publish @('-NoArchive', '-DotnetPath', $fakeDotnet, '-OutputRoot', $ordinary)
Check ($result.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $oldPublish) -and -not (Test-Path -LiteralPath $oldStage)) '普通输出正确清理旧发布与打包目录'

$result = Run-Script $publish @('-NoArchive', '-DotnetPath', $fakeDotnet, '-Configuration', 'Debug')
Check ($result.ExitCode -ne 0) '发布拒绝不受支持的 Debug 配置'
Check ((Test-Path -LiteralPath $sentinel) -and [IO.File]::ReadAllText($sentinel) -eq 'keep') '非法配置不清理原输出'

$refresh = Join-Path $fixture 'tools\refresh-analyzers.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'refresh-analyzers.ps1') -Destination $refresh
$source = Join-Path $fixture 'candidate'
$target = Join-Path $fixture 'tools\analyzers'
$null = New-Item -ItemType Directory -Path $source, $target
foreach ($name in @('Microsoft.WindowsAppSDK.Analyzers.dll', 'Microsoft.WindowsAppSDK.Analyzers.targets')) {
    [System.IO.File]::WriteAllText((Join-Path $source $name), 'new candidate')
    [System.IO.File]::WriteAllText((Join-Path $target $name), 'accepted version')
}
$result = Run-Script $refresh @('-Check', '-SkillRoot', $source)
Check ($result.ExitCode -eq 1) '分析器候选不同会返回差异状态'
Check ([System.IO.File]::ReadAllText((Join-Path $target 'Microsoft.WindowsAppSDK.Analyzers.dll')) -eq 'accepted version') '只检查分析器不会覆盖仓库载荷'

Write-Output "维护工具测试：$passed 项通过。夹具与输出：$root"
