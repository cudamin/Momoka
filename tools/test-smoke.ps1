[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$root = Join-Path $repo ('artifacts\smoke-tests\' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$utf8 = [System.Text.UTF8Encoding]::new($false)
$dotnet = Join-Path $HOME '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw '缺少用户级 .NET SDK。' }
$smoke = Join-Path $repo 'smoke.ps1'
$powerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
$passed = 0

function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "[失败] $Name" }
    $script:passed++
    Write-Output "[通过] $Name"
}

# This project contains only a disposable console helper, not Momoka. Local build imports prevent
# inheriting product analyzers/packages. It uses the same pinned SDK, with all output in this tree.
$project = Join-Path $root 'fixture'
$null = New-Item -ItemType Directory -Path $project
[IO.File]::WriteAllText((Join-Path $project 'Directory.Build.props'), '<Project />', $utf8)
[IO.File]::WriteAllText((Join-Path $project 'Directory.Build.targets'), '<Project />', $utf8)
[IO.File]::WriteAllText((Join-Path $project 'SmokeFixture.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@, $utf8)
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'), @'
var pidFile = Environment.GetEnvironmentVariable("MOMOKA_SMOKE_FIXTURE_PID")!;
File.WriteAllText(pidFile, Environment.ProcessId.ToString());
var mode = Environment.GetEnvironmentVariable("MOMOKA_SMOKE_FIXTURE_MODE");
var log = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "Momoka", "logs",
    $"app-{DateTime.Now:yyyyMMdd}.log");
if (mode == "exit7-log")
{
    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
    File.AppendAllText(log, "synthetic smoke fixture log\n");
}
if (mode == "hold-bad-log") Directory.CreateDirectory(log);
if (mode is "exit7" or "exit7-log") return 7;
if (mode == "exit0") return 0;
await Task.Delay(TimeSpan.FromSeconds(45));
return 0;
'@, $utf8)
$projectFile = Join-Path $project 'SmokeFixture.csproj'
& $dotnet restore $projectFile --disable-parallel -m:1 -p:BuildInParallel=false *> (Join-Path $root 'fixture-restore.log')
if ($LASTEXITCODE -ne 0) { throw "隔离夹具还原失败，见 $root。" }
& $dotnet build $projectFile -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false *> (Join-Path $root 'fixture-build.log')
if ($LASTEXITCODE -ne 0) { throw "隔离夹具构建失败，见 $root。" }
$exe = Join-Path $project 'bin\Release\net10.0\SmokeFixture.exe'
Check (Test-Path -LiteralPath $exe -PathType Leaf) '当前 .NET 10 隔离夹具已成功编译'

function Stop-OwnedFixture([string]$PidFile) {
    if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) { return }
    $fixtureId = [int][IO.File]::ReadAllText($PidFile)
    $running = Get-Process -Id $fixtureId -ErrorAction SilentlyContinue
    if ($running) {
        # Both the PID receipt and executable path must identify our just-created disposable helper.
        if (-not [string]::Equals($running.Path, $exe, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝关闭不是本夹具的进程：$fixtureId。"
        }
        Stop-Process -Id $fixtureId -Force -ErrorAction Stop
    }
}

function Run-Smoke([string]$Name, [string]$Mode, [bool]$StopAfterCheck = $false, [string]$Executable = $exe,
    [int]$Seconds = 1) {
    $caseRoot = Join-Path $root $Name
    $null = New-Item -ItemType Directory -Path $caseRoot
    $profile = Join-Path $caseRoot 'localappdata'
    $null = New-Item -ItemType Directory -Path $profile
    $pidFile = Join-Path $caseRoot 'helper.pid'
    $info = [Diagnostics.ProcessStartInfo]::new($powerShell)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $utf8
    $info.StandardErrorEncoding = $utf8
    $info.EnvironmentVariables['LOCALAPPDATA'] = $profile
    $info.EnvironmentVariables['MOMOKA_SMOKE_FIXTURE_MODE'] = $Mode
    $info.EnvironmentVariables['MOMOKA_SMOKE_FIXTURE_PID'] = $pidFile
    $info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $smoke + '" -Exe "' + $Executable + '" -Seconds ' + $Seconds
    if ($StopAfterCheck) { $info.Arguments += ' -StopAfterCheck' }
    $child = [Diagnostics.Process]::Start($info)
    try {
        $output = $child.StandardOutput.ReadToEndAsync()
        $errors = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(15000)) { throw "smoke 隔离子脚本超时：$Name。" }
        $text = $output.GetAwaiter().GetResult() + $errors.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $caseRoot 'output.log'), $text, $utf8)
        $helperRunning = $false
        if (Test-Path -LiteralPath $pidFile -PathType Leaf) {
            $helperId = [int][IO.File]::ReadAllText($pidFile)
            $helper = Get-Process -Id $helperId -ErrorAction SilentlyContinue
            $helperRunning = $null -ne $helper -and [string]::Equals($helper.Path, $exe, [StringComparison]::OrdinalIgnoreCase)
        }
        return [pscustomobject]@{ ExitCode = $child.ExitCode; Output = $text; HelperRunning = $helperRunning }
    } finally {
        # Never stop Momoka or an unrelated process. Keep fixture logs and outputs for inspection.
        Stop-OwnedFixture $pidFile
        if (-not $child.HasExited) {
            # This retained handle is only our isolated PowerShell child, never an application PID.
            $child.Kill()
            $null = $child.WaitForExit(5000)
        }
        $child.Dispose()
    }
}

try {
    $result = Run-Smoke 'exit7-no-log' 'exit7'
    Check ($result.ExitCode -eq 7) '被测进程退出 7 时脚本返回 7，缺日志不能掩盖失败'
    Check ($result.Output.Contains('EXITED code=7')) '早退出报告保留实际子进程退出码'

    $result = Run-Smoke 'exit7-with-log' 'exit7-log'
    Check ($result.ExitCode -eq 7) '非零早退出并读取日志后仍返回失败'
    Check ($result.Output.Contains('synthetic smoke fixture log')) '早退出保留本次新增的隔离日志'

    $result = Run-Smoke 'exit0-early' 'exit0'
    Check ($result.ExitCode -ne 0) '存活检查期间提前退出 0 也不能冒充存活'
    Check ($result.Output.Contains('EXITED code=0')) '正常早退出的报告不篡改子进程退出码'

    $result = Run-Smoke 'hold-default' 'hold'
    Check ($result.ExitCode -eq 0 -and $result.Output.Contains('RUNNING pid=')) '正常存活报告成功及进程号'
    Check $result.HelperRunning '默认检查不强停被测程序，清理留给调用者'

    $result = Run-Smoke 'hold-optin-cleanup' 'hold' $true
    Check ($result.ExitCode -eq 0 -and -not $result.HelperRunning) '显式 StopAfterCheck 仅清理本次启动的隔离夹具'

    $result = Run-Smoke 'missing-exe' 'exit7' $false (Join-Path $root 'missing.exe')
    Check ($result.ExitCode -ne 0 -and $result.Output.Contains('NOT FOUND')) '缺少程序明确返回非零，不启动任何夹具'

    $result = Run-Smoke 'invalid-duration' 'hold' $false $exe -1
    Check ($result.ExitCode -ne 0 -and -not $result.HelperRunning) '负等待时间在启动前拒绝'

    $result = Run-Smoke 'bad-log' 'hold-bad-log'
    Check ($result.ExitCode -ne 0) '读取本次日志失败不能返回成功'
    Check $result.HelperRunning '日志失败也不擅自强停被测程序'

    Write-Output "smoke 工具测试：$passed 项通过，0 失败。夹具与日志：$root"
    exit 0
} catch {
    Write-Output $_.Exception.Message
    Write-Output "smoke 工具测试失败；已通过 $passed 项。夹具与日志：$root"
    exit 1
}
