[CmdletBinding()]
param(
    [string]$Dotnet = (Join-Path $HOME '.dotnet\dotnet.exe'),
    [string]$Packages = (Join-Path $HOME '.nuget\packages'),
    [string]$ShellDll,
    [string]$EvidenceRoot
)

# Source from the calling worktree only. This runner never copies product snapshots,
# starts Momoka/App, invokes native/UI, reads settings/secrets, or contacts a media server.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gitRoot = (& git -C $root rev-parse --show-toplevel).Trim().Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须在本 fixture 所属工作树运行。' }
$dotnetPath = (Resolve-Path -LiteralPath $Dotnet).Path
$expectedShell = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
if ([string]::IsNullOrWhiteSpace($ShellDll)) { $ShellDll = $expectedShell }
if ([IO.Path]::GetFullPath($ShellDll) -ne $expectedShell) { throw '只接受本树标准 fresh Shell Release DLL。' }
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $root ('artifacts\r025-delete-command-fixture\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
$allowed = (Join-Path $root 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $evidence.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw '证据只能写本树 artifacts 下。' }
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$commands = [Collections.Generic.List[string]]::new()

function Invoke-Logged {
    param([string]$Name, [string[]]$Arguments)
    $log = Join-Path $evidence ($Name + '.log')
    $commands.Add('"' + $dotnetPath + '" ' + ($Arguments -join ' '))
    & $dotnetPath @Arguments *> $log
    $exit = $LASTEXITCODE
    Get-Content -LiteralPath $log -Tail 35
    [IO.File]::WriteAllText((Join-Path $evidence 'commands.txt'), ($commands -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    if ($exit -ne 0) { throw "$Name 失败，退出码 $exit；日志 $log。停止，不运行旧产物。" }
}

$shellProject = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'
$fixtureProject = Join-Path $PSScriptRoot 'Momoka.DetailDeleteCommandTests.csproj'
$restore = @('--packages', [IO.Path]::GetFullPath($Packages), '--disable-parallel', '-p:NuGetAudit=false',
    '-p:BuildInParallel=false', '-p:MSBuildNodeReuse=false')
$build = @('-c', 'Release', '--no-restore', '-m:1', '-p:BuildInParallel=false',
    '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
$sourcePaths = @('src/Momoka.Shell/ViewModels/DetailViewModel.cs', 'src/Momoka.Shell/Views/DetailPage.xaml.cs',
    'src/Momoka.Shell/Views/ItemCommands.cs', 'src/Momoka.Shell/Views/ItemCommands.FnNas.cs')
$before = @($sourcePaths | ForEach-Object {
    [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash }
})

Invoke-Logged 'shell-restore' (@('restore', $shellProject) + $restore)
Invoke-Logged 'fixture-restore' (@('restore', $fixtureProject) + $restore)
Invoke-Logged 'shell-build' (@('build', $shellProject) + $build)
Invoke-Logged 'fixture-build' (@('build', $fixtureProject) + $build)
foreach ($source in $before) {
    if ($source.Sha256 -ne (Get-FileHash -LiteralPath (Join-Path $root $source.Path) -Algorithm SHA256).Hash) {
        throw '构建期间题内产品源变化，拒绝给快照盖章；需确认新快照后重验。'
    }
}
$fixtureDll = Join-Path $PSScriptRoot 'bin\Release\net10.0\Momoka.DetailDeleteCommandTests.dll'
$proof = [ordered]@{
    ShellPath = $expectedShell
    ShellSha256 = (Get-FileHash -LiteralPath $expectedShell -Algorithm SHA256).Hash
    FixtureSha256 = (Get-FileHash -LiteralPath $fixtureDll -Algorithm SHA256).Hash
    BuildExit = 0
    Sources = $before
    BuiltUtc = [DateTime]::UtcNow.ToString('O')
}
$proofPath = Join-Path $evidence 'build-proof.json'
[IO.File]::WriteAllText($proofPath, ($proof | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
Invoke-Logged 'test' @($fixtureDll, $expectedShell, $root, $proofPath)
Write-Output "实际命令专项通过；证据 $evidence"
