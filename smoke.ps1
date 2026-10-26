# Launches a build, checks whether it survives the observation window, then prints only new log data.
# Process survival is not page, login or playback verification. Ordinary startup may read real data.
# A failed launch, early exit (even code 0), or unreadable new log returns nonzero.
# Leave the launched process alone by default; -StopAfterCheck is an explicit cleanup opt-in.
#
#   .\smoke.ps1                  WinUI 3 调试版
#   .\smoke.ps1 -Release         artifacts\publish\win-x64 里的发行版
param(
    [ValidateRange(0, 3600)]
    [int]$Seconds = 12,
    [switch]$Release,
    [string]$Exe,
    [switch]$StopAfterCheck
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
if (-not $Exe) {
    # x64 rather than AnyCPU: WinUI 3 has no AnyCPU, and the csproj pins Platform.
    $Exe = if ($Release) {
        Join-Path $PSScriptRoot 'artifacts\publish\win-x64\Momoka.exe'
    } else {
        Join-Path $PSScriptRoot 'src\Momoka.Shell\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Momoka.exe'
    }
}

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Output ("NOT FOUND {0}" -f $Exe)
    exit 1
}

$code = 1
$process = $null
try {
    $log = Join-Path $env:LOCALAPPDATA ("Momoka\logs\app-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
    $before = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
    Write-Output ("启动 {0}" -f $Exe)
    $process = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden

    # WaitForExit pins the process handle and makes its exit code available even for an immediate exit.
    if ($process.WaitForExit($Seconds * 1000)) {
        $childCode = $process.ExitCode
        Write-Output ("EXITED code={0}" -f $childCode)
        $code = if ($childCode -eq 0) { 1 } else { $childCode }
    } else {
        Write-Output ("RUNNING pid={0}" -f $process.Id)
        $code = 0
        if ($StopAfterCheck -and -not $process.HasExited) {
            # Explicit opt-in only. Use our retained process handle, not an arbitrary/reused PID.
            $process.Kill()
            if (-not $process.WaitForExit(5000)) { throw '本次启动的进程没有按期退出。' }
            Write-Output ("STOPPED pid={0}" -f $process.Id)
        }
    }

    if (Test-Path -LiteralPath $log) {
        $stream = $null
        $reader = $null
        try {
            $stream = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
            $stream.Seek($before, 'Begin') | Out-Null
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
            $text = $reader.ReadToEnd()
            Write-Output '--- 本次运行的日志 ---'
            Write-Output $text
        } catch {
            Write-Output ("LOG READ FAILED {0}" -f $_.Exception.GetType().Name)
            if ($code -eq 0) { $code = 1 }
        } finally {
            if ($reader) { $reader.Dispose() }
            elseif ($stream) { $stream.Dispose() }
        }
    }
} catch {
    Write-Output ("SMOKE FAILED {0}" -f $_.Exception.Message)
    $code = 1
} finally {
    if ($process) { $process.Dispose() }
}
exit $code
