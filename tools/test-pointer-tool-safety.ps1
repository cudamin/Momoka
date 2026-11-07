#requires -Version 7.0
[CmdletBinding()]
param([string]$Phase = 'pointer-safety')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $root ('artifacts/pointer-safety/' + $Phase + '-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $root 'tools/poke.ps1'))
$start = $source.IndexOf('# 注入一次绝对移动', [StringComparison]::Ordinal)
if ($start -lt 0) { throw '生产指针脚本边界缺失' }
$body = $source.Substring($start)
$fake = @'
using System;
using System.Runtime.InteropServices;
public static class Poke {
 public struct POINT { public int X,Y; } public struct RECT { public int L,T,R,B; }
 public struct MOUSEINPUT { public int dx,dy; public uint data,flags,time; public IntPtr extra; }
 public struct INPUT { public uint type; public MOUSEINPUT mi; }
 public const uint Move=1,LeftDown=2,LeftUp=4,Wheel=2048,Absolute=32768,VirtualDesk=16384,GaRoot=2;
 public const int NotchDelta=120,DeskX=76,DeskY=77,DeskWidth=78,DeskHeight=79;
 public static string Mode; public static int Actions,Restores; public static POINT Cursor = new POINT {X=80,Y=80};
 public static bool GetClientRect(IntPtr h,out RECT r) {r=new RECT{R=600,B=400};return true;}
 public static bool ClientToScreen(IntPtr h,ref POINT p) {return true;}
 public static bool GetCursorPos(out POINT p) {p=Cursor;return Mode!="sample";}
 public static bool SetForegroundWindow(IntPtr h) {return Mode!="foreground";}
 public static IntPtr GetForegroundWindow() {return new IntPtr(1);}
 public static bool SetCursorPos(int x,int y) {Restores++;Cursor=new POINT{X=x,Y=y};return true;}
 public static bool IsWindow(IntPtr h) {return true;}
 public static uint GetWindowThreadProcessId(IntPtr h,out uint pid) {pid=Mode=="owner"?456u:123u;return 1;}
 public static IntPtr WindowFromPoint(POINT p) {return new IntPtr(Mode=="foreign"?3:1);}
 public static IntPtr GetAncestor(IntPtr h,uint f) {return h;}
 public static int GetSystemMetrics(int i) {return i==78?1920:i==79?1080:0;}
 public static uint SendInput(uint count,INPUT[] events,int size) {
  if(Mode=="send")return 0;
  if(Mode!="landing")Cursor=new POINT{X=(int)Math.Round(events[0].mi.dx*1919.0/65535),Y=(int)Math.Round(events[0].mi.dy*1079.0/65535)};
  return count;
 }
 public static void mouse_event(uint flags,int x,int y,int data,IntPtr extra) {Actions++;}
}
'@
$checks = [Collections.Generic.List[object]]::new()
foreach ($engine in @((Get-Command powershell.exe).Source, (Get-Command pwsh.exe).Source)) {
 foreach ($mode in @('good','owner','foreign','send','sample','foreground','landing')) {
  $script = "`$ErrorActionPreference='Stop'`nAdd-Type @'`n" + $fake + "`n'@`n" +
   "[Poke]::Mode='$mode'`nfunction Get-Process { [pscustomobject]@{ Id=123; ProcessName='Momoka'; MainWindowHandle=[IntPtr]1 } }`nfunction Start-Sleep { }`n" +
   "`$ProcessId=123; `$WindowHandle=0; `$X=100; `$Y=100; `$Fraction=`$false; `$Click='left'; `$Wheel=1; `$ScreenX=-1; `$ScreenY=-1; `$SettleMs=0`n" +
   "`$failed=`$false`ntry {`n" + $body + "`n} catch { `$failed=`$true }`n" +
   "[pscustomobject]@{failed=`$failed;actions=[Poke]::Actions;restores=[Poke]::Restores} | ConvertTo-Json -Compress`n"
  $name = [IO.Path]::GetFileNameWithoutExtension($engine) + '-' + $mode
  $file = Join-Path $output ($name + '.ps1')
  [IO.File]::WriteAllText($file, $script, [Text.UTF8Encoding]::new($true))
  $log = Join-Path $output ($name + '.log')
  & $engine -NoProfile -ExecutionPolicy Bypass -File $file *> $log
  $lines = @(Get-Content -LiteralPath $log)
  $data = $lines[-1] | ConvertFrom-Json
  $passed = if ($mode -eq 'good') { -not $data.failed -and $data.actions -eq 3 -and $data.restores -eq 1 }
   else { $data.failed -and $data.actions -eq 0 }
  $checks.Add([pscustomobject]@{name=$name;passed=$passed;result=$data})
  Write-Output ("[{0}] {1}" -f $(if($passed){'通过'}else{'失败'}),$name)
 }
}
$cursor = [IO.File]::ReadAllText((Join-Path $root 'tools/cursor-watch.ps1'))
$noteStart = $cursor.IndexOf('        $note = "t={0:0.00}s', [StringComparison]::Ordinal)
$noteEnd = $cursor.IndexOf('        $font = ', $noteStart, [StringComparison]::Ordinal)
if ($noteStart -lt 0 -or $noteEnd -lt 0) { throw '生产采样标签边界缺失' }
$noteBlock = [scriptblock]::Create($cursor.Substring($noteStart,$noteEnd-$noteStart))
$ok=$false; $t=0; $info=[pscustomobject]@{flags=0;hCursor=[IntPtr]::Zero}; $note=''
. $noteBlock
$passed = $note.Contains('未知') -and -not $note.Contains('屏幕上没有光标')
$checks.Add([pscustomobject]@{name='sample-failure-is-unknown';passed=$passed;result=$note})
Write-Output ("[{0}] sample-failure-is-unknown" -f $(if($passed){'通过'}else{'失败'}))
[IO.File]::WriteAllText((Join-Path $output 'summary.json'),($checks | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$failures=@($checks | Where-Object { -not $_.passed }).Count
Write-Output "共 $($checks.Count) 项，通过 $($checks.Count-$failures)，失败 $failures，跳过 0；$output"
if($failures){exit 1}
