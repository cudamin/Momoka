[CmdletBinding()]
param(
    [string]$Dotnet = (Join-Path $HOME '.dotnet\dotnet.exe'),
    [string]$EvidenceRoot
)

# Windows 系统通知专项：自包含与框架依赖两种分发形态各发一条匿名通知，用通知中心回读唯一
# 匹配的通知 ID，再按 ID 清除并核对剩余为零。整个过程只用匿名测试身份，不读取真实账号、
# 设置、媒体或服务器；两次运行都占用共享桌面诊断锁（与主测试、自检、界面探针串行）。
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gitRoot = (& git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须在此 fixture 所属工作树运行。' }
$dotnetPath = (Resolve-Path -LiteralPath $Dotnet).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $root ('artifacts\system-notification-tests\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
$allowed = (Join-Path $root 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $evidence.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw '证据只能写本树 artifacts 下。' }
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$project = Join-Path $PSScriptRoot 'Momoka.SystemNotificationTests.csproj'
$commands = [Collections.Generic.List[string]]::new()

function Invoke-Logged {
    param([string]$Name, [string]$Executable, [string[]]$Arguments)
    $log = Join-Path $evidence ($Name + '.log')
    $commands.Add('"' + $Executable + '" ' + ($Arguments -join ' '))
    # Windows PowerShell 会把原生命令写到 stderr 的一行包成终止错误；成败只看真实退出码。
    $savedPreference = $ErrorActionPreference
    $global:LASTEXITCODE = $null
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments *> $log
        $code = $global:LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedPreference
    }
    Get-Content -LiteralPath $log -Tail 20
    [IO.File]::WriteAllText((Join-Path $evidence 'commands.txt'), ($commands -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    if ($null -eq $code) { throw "$Name 未取得退出码；日志 $log。" }
    if ($code -ne 0) { throw "$Name 失败，退出码 $code；日志 $log（3 表示共享桌面被占用或当前会话不允许通知）。" }
}

# --artifacts-path 会把 obj 一起搬到证据目录，那里没有 project.assets.json；这里必须允许还原，
# 否则两次发布都会在 NETSDK1004 停下。包来自本机固定缓存，不联网。
$publishFlags = @('-c', 'Release', '-p:NuGetAudit=false', '-p:BuildInParallel=false', '-p:MSBuildNodeReuse=false')

$selfContained = Join-Path $evidence 'self-contained'
Invoke-Logged 'publish-self-contained' $dotnetPath (@('publish', $project, '--self-contained', 'true',
    '-p:WindowsAppSDKSelfContained=true', '--artifacts-path', (Join-Path $evidence 'obj-self-contained'),
    '-o', $selfContained) + $publishFlags)
$selfExe = Join-Path $selfContained 'MomokaSystemNotificationProbe.exe'
if (-not (Test-Path -LiteralPath $selfExe -PathType Leaf)) { throw "自包含发布缺少探针：$selfExe" }
Invoke-Logged 'run-self-contained' $selfExe @('--send-self-contained')

$frameworkDependent = Join-Path $evidence 'framework-dependent'
Invoke-Logged 'publish-framework-dependent' $dotnetPath (@('publish', $project, '--self-contained', 'false',
    '-p:WindowsAppSDKSelfContained=false', '--artifacts-path', (Join-Path $evidence 'obj-framework-dependent'),
    '-o', $frameworkDependent) + $publishFlags)
$frameworkExe = Join-Path $frameworkDependent 'MomokaSystemNotificationProbe.exe'
if (-not (Test-Path -LiteralPath $frameworkExe -PathType Leaf)) { throw "框架依赖发布缺少探针：$frameworkExe" }
Invoke-Logged 'run-framework-dependent' $frameworkExe @('--send-framework-dependent')

$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $evidence 'verification.json'), (([ordered]@{
    date = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'); root = $root; head = (& git -C $root rev-parse HEAD).Trim()
    selfContained = $selfExe; frameworkDependent = $frameworkExe
    anonymousIdentity = $true; notificationCleared = $true; noServerOrPlayback = $true
} | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"), $utf8)
Write-Output "系统通知两形态均投递并清除成功；证据 $evidence"
