[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/release-maintenance-tests/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$bom = [Text.UTF8Encoding]::new($true)
$results = [Collections.Generic.List[object]]::new()

function Save([string]$Path, [string]$Value) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    $encoding = if ($Path.EndsWith('.ps1')) { $bom } else { $utf8 }
    [IO.File]::WriteAllText($Path, $Value.Replace("`r`n", "`n"), $encoding)
}
function Check([bool]$Condition, [string]$Name) {
    $results.Add([pscustomobject]@{ name = $Name; passed = $Condition })
    Write-Output ("{0}: {1}" -f $Name, $Condition)
}
function Invoke-Fixture([string]$Name, [string]$Script, [string[]]$Arguments = @()) {
    $log = Join-Path $run ($Name + '.log')
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments *> $log
        return $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
}

# Full production backup script; a fixed clock proves same-minute collisions deterministically.
# Only the anonymous source file is locked. User backups, repository settings and .git are untouched.
$backupRoot = Join-Path $run 'backup-source'
$backupScript = Join-Path $backupRoot 'tools/backup.ps1'
Save $backupScript ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'backup.ps1')))
$source = Join-Path $backupRoot 'source.txt'
Save $source 'recoverable source'
$backupOutput = Join-Path $run 'backups'
$backupDriver = Join-Path $run 'backup-driver.ps1'
Save $backupDriver @'
param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
function Get-Date { [datetime]'2026-11-05T12:34:00' }
function git { if ($args -contains 'rev-parse') { 'anonymous' } }
& $Source -OutputRoot $Output -Label same-minute
'@
$first = Invoke-Fixture 'backup-first' $backupDriver @('-Source', $backupScript, '-Output', $backupOutput)
$archives = @(Get-ChildItem -LiteralPath $backupOutput -Filter '*.zip')
Check ($first -eq 0 -and $archives.Count -eq 1) 'first backup is published'
$firstPath = $archives[0].FullName
$firstHash = (Get-FileHash -LiteralPath $firstPath).Hash
$second = Invoke-Fixture 'backup-second' $backupDriver @('-Source', $backupScript, '-Output', $backupOutput)
Check ($second -eq 0 -and @(Get-ChildItem -LiteralPath $backupOutput -Filter '*.zip').Count -eq 2) 'same-minute backup keeps both archives'
$lock = [IO.File]::Open($source, 'Open', 'ReadWrite', 'None')
try { $failed = Invoke-Fixture 'backup-locked' $backupDriver @('-Source', $backupScript, '-Output', $backupOutput) }
finally { $lock.Dispose() }
Check ($failed -ne 0) 'locked source fails backup'
Check ((Test-Path -LiteralPath $firstPath) -and (Get-FileHash -LiteralPath $firstPath).Hash -eq $firstHash) 'failed backup preserves first archive bytes'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($firstPath)
try { Check ($null -ne $zip.GetEntry('source.txt') -and $null -ne $zip.GetEntry('备份说明.txt')) 'original archive stays readable and complete' }
finally { $zip.Dispose() }
Check (@(Get-ChildItem -LiteralPath $backupOutput -Filter '*.partial').Count -eq 0) 'failed temporary archive is removed'
Check (@(Get-ChildItem -LiteralPath $backupOutput -Filter '*.zip').Count -eq 2) 'failed backup does not publish incomplete archive'

# Replace certificate discovery and native tool lookup only; the entire signing/verification tail
# must remain byte-identical. No certificate store, real signing tool or trust settings are used.
$signRoot = Join-Path $run 'sign-source'
$original = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'sign-release.ps1'))
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($original, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Signing source does not parse.' }
$functions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Get-MomokaCert', 'Get-SignTool') }, $false))
if ($functions.Count -ne 2) { throw 'Signing discovery boundaries changed.' }
$adapted = $original
foreach ($fn in @($functions | Sort-Object { $_.Extent.StartOffset } -Descending)) {
    $replacement = if ($fn.Name -eq 'Get-MomokaCert') {
        'function Get-MomokaCert { param([string]$WantThumbprint) [pscustomobject]@{ Thumbprint="FIXTURE"; NotAfter=[datetime]"2030-01-01" } }'
    } else { 'function Get-SignTool { Join-Path (Split-Path -Parent $PSScriptRoot) "signer.cmd" }' }
    $adapted = $adapted.Remove($fn.Extent.StartOffset, $fn.Extent.EndOffset - $fn.Extent.StartOffset).Insert($fn.Extent.StartOffset, $replacement)
}
$signatureBoundary = @'
function Get-AuthenticodeSignature {
    param([string]$LiteralPath)
    $mode = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'signature.txt'))
    [pscustomobject]@{ Status=$(if ($mode -eq 'invalid') { 'NotSigned' } else { 'Valid' }); SignerCertificate=[pscustomobject]@{ Subject='CN=Fixture'; Thumbprint=$(if ($mode -eq 'wrong') { 'OTHER' } else { 'FIXTURE' }) } }
}
$ErrorActionPreference = 'Stop'
'@
$adapted = $adapted.Replace("`$ErrorActionPreference = 'Stop'", $signatureBoundary)
$tail = '# 收集要签的文件'
if ($original.Substring($original.IndexOf($tail)) -cne $adapted.Substring($adapted.IndexOf($tail))) { throw 'Signing tail was modified by fixture.' }
$signScript = Join-Path $signRoot 'tools/sign-release.ps1'
Save $signScript $adapted
Save (Join-Path $signRoot 'input.exe') 'anonymous non-executable payload'
foreach ($case in @(@{ name='native-failure'; code=23; mode='valid'; expected=1 }, @{ name='invalid-signature'; code=0; mode='invalid'; expected=1 }, @{ name='wrong-signer'; code=0; mode='wrong'; expected=1 }, @{ name='sign-success'; code=0; mode='valid'; expected=0 })) {
    Save (Join-Path $signRoot 'signature.txt') $case.mode
    [IO.File]::WriteAllText((Join-Path $signRoot 'signer.cmd'), "@echo off`r`nexit /b $($case.code)`r`n", [Text.Encoding]::ASCII)
    $code = Invoke-Fixture $case.name $signScript @('-Path', (Join-Path $signRoot 'input.exe'), '-Thumbprint', 'FIXTURE')
    Check (($case.expected -eq 0 -and $code -eq 0) -or ($case.expected -ne 0 -and $code -ne 0)) $case.name
}

# Execute the full publish script against anonymous build/packaging endpoints. The test checks
# parameter delivery, not MSIX validity, certificates, installation or publication.
$packageRoot = Join-Path $run 'package-source'
foreach ($directory in @('tools', 'src/Momoka.Shell', 'assets/shaders', 'assets/mpv-runtime', 'artifacts/publish/win-x64')) {
    [IO.Directory]::CreateDirectory((Join-Path $packageRoot $directory)) | Out-Null
}
Save (Join-Path $packageRoot 'tools/publish.ps1') ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'publish.ps1')))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mpv-payload-common.ps1') -Destination (Join-Path $packageRoot 'tools/mpv-payload-common.ps1')
Copy-Item -LiteralPath (Join-Path $repo 'src/Momoka.Shell/Package.appxmanifest') -Destination (Join-Path $packageRoot 'src/Momoka.Shell/Package.appxmanifest')
Copy-Item -LiteralPath (Join-Path $repo 'app.ico') -Destination (Join-Path $packageRoot 'app.ico')
foreach ($file in @('src/Momoka.Shell/Momoka.Shell.csproj', 'libmpv-2.dll', 'assets/mpv-runtime/vulkan-1.dll', 'artifacts/publish/win-x64/Momoka.exe', 'requested.pfx')) { Save (Join-Path $packageRoot $file) 'anonymous placeholder' }
Save (Join-Path $packageRoot 'Directory.Build.props') '<Project><PropertyGroup><Version>0.0.0</Version></PropertyGroup></Project>'
Save (Join-Path $packageRoot 'tools/verify-publish.ps1') '$global:LASTEXITCODE = 0'
Save (Join-Path $packageRoot 'fake-dotnet.ps1') "Write-Output '10.0.400'`n`$global:LASTEXITCODE = 0"
Save (Join-Path $packageRoot 'fake-winapp.ps1') @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Captured)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'captured.json'), (ConvertTo-Json -InputObject @($Captured)))
$global:LASTEXITCODE = 0
'@
$packageDriver = Join-Path $packageRoot 'invoke.ps1'
Save $packageDriver @'
param([switch]$WithSign, [switch]$Store)
$ErrorActionPreference = 'Stop'
function Get-Command {
    param([string]$Name, [string]$ErrorAction)
    if ($Name -eq 'winapp') { return [pscustomobject]@{ Path=(Join-Path $PSScriptRoot 'fake-winapp.ps1') } }
    if ($Name -eq 'dotnet') { return [pscustomobject]@{ Source=(Join-Path $PSScriptRoot 'fake-dotnet.ps1') } }
    throw 'Unexpected command lookup'
}
$options = @{ SkipPublish=$true; NoArchive=$true; Msix=$true; DotnetPath=(Join-Path $PSScriptRoot 'fake-dotnet.ps1'); CertPath=(Join-Path $PSScriptRoot 'requested.pfx'); OutputRoot=(Join-Path $PSScriptRoot 'artifacts'); Sign=$WithSign; Store=$Store }
& (Join-Path $PSScriptRoot 'tools/publish.ps1') @options
'@
foreach ($explicit in @($false, $true)) {
    $flags = if ($explicit) { @('-WithSign') } else { @() }
    $code = Invoke-Fixture ("certificate-explicit-$explicit") $packageDriver $flags
    $captured = Get-Content -LiteralPath (Join-Path $packageRoot 'captured.json') -Raw | ConvertFrom-Json
    Check ($code -eq 0 -and ($captured -contains '--cert') -and ($captured -contains (Join-Path $packageRoot 'requested.pfx'))) "certificate delivered with explicit Sign=$explicit (exit=$code)"
}
$code = Invoke-Fixture 'store-certificate-rejected' $packageDriver @('-Store')
Check ($code -ne 0) 'Store rejects certificate before packaging'

$compose = [IO.File]::ReadAllText((Join-Path $repo 'website/docker-compose.yml'))
foreach ($binding in @('./nginx.conf:/etc/nginx/conf.d/default.conf:ro', './dist:/usr/share/nginx/html:ro')) {
    Check ($compose.Contains($binding)) ("Compose uses uploaded project: $binding")
}
[IO.File]::WriteAllText((Join-Path $run 'results.json'), ($results.ToArray() | ConvertTo-Json) + "`n", $utf8)
$failed = @($results | Where-Object { -not $_.passed }).Count
Write-Output "共 $($results.Count) 项，通过 $($results.Count - $failed)，失败 $failed，跳过 0。匿名脚本验证：$run"
if ($failed) { exit 1 }
