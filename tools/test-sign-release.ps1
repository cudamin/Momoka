#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^[a-z0-9-]+$')][string]$Phase = 'green')

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/review-batch10/sign-' + $Phase + '-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($true)
$source = Join-Path $PSScriptRoot 'sign-release.ps1'
$text = [IO.File]::ReadAllText($source)
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw '生产脚本解析失败。' }
# Only external endpoints are substituted. All parameter/branch/target collection/signature
# validation code, including Show-CertStatus, executes unchanged in a child host.
$replacements = @{
    'Get-SignTool' = 'function Get-SignTool { return $env:MOMOKA_FIXTURE_SIGNER }'
    'Get-MomokaCert' = 'function Get-MomokaCert { param([string]$WantThumbprint); if ($env:MOMOKA_FIXTURE_NO_CERT -eq "1") { throw "anonymous no certificate" }; return [pscustomobject]@{Thumbprint="ANONYMOUS";NotAfter=[datetime]"2030-01-01"} }'
    'Test-Admin' = 'function Test-Admin { return $true }'
}
$functions = @($ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $replacements.ContainsKey($n.Name) }, $true))
foreach ($name in $replacements.Keys) {
    if (@($functions | Where-Object Name -eq $name).Count -ne 1) { throw "边界函数缺失或重复：$name" }
}
foreach ($function in ($functions | Sort-Object { $_.Extent.StartOffset } -Descending)) {
    $text = $text.Remove($function.Extent.StartOffset, $function.Extent.EndOffset - $function.Extent.StartOffset).Insert($function.Extent.StartOffset, $replacements[$function.Name])
}
$fixtureTools = Join-Path $run 'tools'
[IO.Directory]::CreateDirectory($fixtureTools) | Out-Null
$fixtureSource = Join-Path $fixtureTools 'sign-release.ps1'
[IO.File]::WriteAllText($fixtureSource, $text, $utf8)
$signer = Join-Path $run 'anonymous-signer.ps1'
[IO.File]::WriteAllText($signer, @'
[IO.File]::AppendAllText($env:MOMOKA_FIXTURE_EVENTS, "sign`n")
[IO.File]::AppendAllText([string]$args[-1], ' signed by anonymous fixture')
$global:LASTEXITCODE = 0
Write-Output 'Successfully signed'
'@ + "`n", $utf8)
$runner = Join-Path $run 'runner.ps1'
[IO.File]::WriteAllText($runner, @'
param([string]$Scenario)
$ErrorActionPreference = 'Stop'
$case = Get-Content -LiteralPath $Scenario -Raw | ConvertFrom-Json
$env:MOMOKA_FIXTURE_EVENTS = $case.events
$env:MOMOKA_FIXTURE_SIGNER = $case.signer
$env:MOMOKA_FIXTURE_NO_CERT = $case.noCert
$env:TEMP = $case.temp
function Record-Write([string]$Operation) { [IO.File]::AppendAllText($case.events, $Operation + "`n") }
function Get-ChildItem {
    [CmdletBinding()]param([string[]]$Path,[string]$LiteralPath,[switch]$Recurse,[switch]$File)
    if ($Path -and $Path[0].StartsWith('Cert:')) { return }
    Microsoft.PowerShell.Management\Get-ChildItem @PSBoundParameters
}
function Get-ItemProperty { [CmdletBinding()]param([string]$LiteralPath); return [pscustomobject]@{} }
function New-SelfSignedCertificate { Record-Write 'certificate-create'; return [pscustomobject]@{Thumbprint='ANONYMOUS';NotAfter=[datetime]'2030-01-01'} }
function Export-PfxCertificate { Record-Write 'private-key-export' }
function Export-Certificate { Record-Write 'certificate-export' }
function Import-Certificate { Record-Write 'certificate-import' }
function New-Item { Record-Write 'item-write' }
function New-ItemProperty { Record-Write 'registry-write' }
function Remove-ItemProperty { Record-Write 'registry-delete' }
function Remove-Item { Record-Write 'item-delete' }
function Join-Path {
    [CmdletBinding()]param([string]$Path,[string]$ChildPath)
    if ($Path -match '^(HKLM|HKCU|Registry):') { return $Path.TrimEnd('\') + '\' + $ChildPath }
    Microsoft.PowerShell.Management\Join-Path @PSBoundParameters
}
function Get-AuthenticodeSignature {
    [CmdletBinding()]param([string]$LiteralPath)
    [IO.File]::AppendAllText($case.reads, $LiteralPath + "`n")
    if ([IO.File]::ReadAllText($LiteralPath).Contains('signed by anonymous fixture')) {
        return [pscustomobject]@{Status='Valid';SignerCertificate=[pscustomobject]@{Subject='CN=Anonymous';Thumbprint='ANONYMOUS'}}
    }
    return [pscustomobject]@{Status='NotSigned';SignerCertificate=$null}
}
$flags = @{}
foreach ($property in $case.flags.PSObject.Properties) { $flags[$property.Name] = $property.Value }
try { & $case.source @flags; exit 0 } catch { Write-Output $_; exit 1 }
'@ + "`n", $utf8)
$results = [Collections.Generic.List[object]]::new()
function Check([bool]$Condition, [string]$Name) {
    $results.Add([pscustomobject]@{name=$Name;passed=$Condition})
    Write-Output ("[{0}] {1}" -f $(if ($Condition) { '通过' } else { '失败' }), $Name)
}
$cases = @(
    @{name='status-only';flags=@{Status=$true};exit=0;writes=0;reads=0},
    @{name='status-file';flags=@{Status=$true};path='file';exit=0;writes=0;reads=1},
    @{name='status-directory';flags=@{Status=$true};path='directory';exit=0;writes=0;reads=2},
    @{name='status-no-cert';flags=@{Status=$true};path='file';noCert='1';exit=0;writes=0;reads=1},
    @{name='status-missing';flags=@{Status=$true};path='missing';exit=1;writes=0;reads=0},
    @{name='status-empty';flags=@{Status=$true};path='empty';exit=1;writes=0;reads=0},
    @{name='conflict-setup';flags=@{Status=$true;Setup=$true};exit=1;writes=0;reads=0},
    @{name='conflict-trust';flags=@{Status=$true;TrustPublisher=$true};exit=1;writes=0;reads=0},
    @{name='conflict-appcheck';flags=@{Status=$true;SetAppCheck='Off'};exit=1;writes=0;reads=0},
    @{name='conflict-shell-off';flags=@{Status=$true;SetShellSmartScreen='Off'};exit=1;writes=0;reads=0},
    @{name='conflict-shell-on';flags=@{Status=$true;SetShellSmartScreen='On'};exit=1;writes=0;reads=0},
    @{name='normal-sign';flags=@{};path='file';exit=0;writes=1;reads=1},
    @{name='normal-setup';flags=@{Setup=$true};exit=0;writes=1;reads=0},
    @{name='normal-shell';flags=@{SetShellSmartScreen='Off'};exit=0;writes=1;reads=0}
)
foreach ($hostPath in @((Get-Command powershell.exe).Source, (Get-Command pwsh.exe).Source)) {
    $hostName = [IO.Path]::GetFileNameWithoutExtension($hostPath)
    foreach ($scenario in $cases) {
        $directory = Join-Path $run ($hostName + '-' + $scenario.name)
        [IO.Directory]::CreateDirectory((Join-Path $directory 'nested')) | Out-Null
        $files = @((Join-Path $directory 'fixture.exe'), (Join-Path $directory 'nested/fixture.dll'))
        foreach ($file in $files) { [IO.File]::WriteAllText($file, 'anonymous non-PE bytes') }
        $hashes = @($files | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })
        $flags = $scenario.flags.Clone()
        if ($scenario.path) {
            $flags.Path = switch ($scenario.path) {
                file { $files[0] }; directory { $directory }; missing { Join-Path $directory 'missing.exe' }; empty { Join-Path $directory 'empty' }
            }
            if ($scenario.path -eq 'empty') { [IO.Directory]::CreateDirectory($flags.Path) | Out-Null }
        }
        $events = Join-Path $directory 'events.txt'; $reads = Join-Path $directory 'reads.txt'
        [IO.File]::WriteAllText($events, ''); [IO.File]::WriteAllText($reads, '')
        $data = @{flags=$flags;source=$fixtureSource;signer=$signer;events=$events;reads=$reads;temp=$directory;noCert=$scenario.noCert}
        $casePath = Join-Path $directory 'case.json'
        [IO.File]::WriteAllText($casePath, ($data | ConvertTo-Json -Depth 6), $utf8)
        $saved = $ErrorActionPreference
        try { $ErrorActionPreference = 'Continue'; & $hostPath -NoProfile -ExecutionPolicy Bypass -File $runner -Scenario $casePath *> (Join-Path $directory 'output.log'); $code = $LASTEXITCODE }
        finally { $ErrorActionPreference = $saved }
        $writeCount = @([IO.File]::ReadAllLines($events)).Count
        $readCount = @([IO.File]::ReadAllLines($reads)).Count
        $prefix = "$hostName $($scenario.name)"
        Check ($code -eq $scenario.exit) "$prefix 退出码预期 $($scenario.exit) 实际 $code"
        Check ($(if ($scenario.writes -eq 0) { $writeCount -eq 0 } else { $writeCount -gt 0 })) "$prefix 外部写入边界"
        Check ($readCount -eq $scenario.reads) "$prefix 目标签名读取数"
        $unchanged = (Get-FileHash -LiteralPath $files[0]).Hash -eq $hashes[0] -and (Get-FileHash -LiteralPath $files[1]).Hash -eq $hashes[1]
        Check ($(if ($scenario.name -eq 'normal-sign') { -not $unchanged } else { $unchanged })) "$prefix 匿名文件字节"
    }
}
$failed = @($results | Where-Object { -not $_.passed }).Count
$receipt = @{source=$source;sourceSha256=(Get-FileHash -LiteralPath $source).Hash;phase=$Phase;total=$results.Count;failed=$failed;results=@($results)}
[IO.File]::WriteAllText((Join-Path $run 'results.json'), ($receipt | ConvertTo-Json -Depth 8), $utf8)
Write-Output "$($results.Count - $failed)/$($results.Count)，失败 $failed；证据 $run"
if ($failed) { exit 1 }
