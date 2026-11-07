#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9-]+$')]
    [string]$Phase = 'unblock',
    [string]$SourcePath = ''
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $PSScriptRoot 'unblock-momoka.ps1'
if ($SourcePath) { $source = [IO.Path]::GetFullPath($SourcePath) }
$run = Join-Path $repo ('artifacts/review-batch9/' + $Phase + '-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$results = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
$zone = "[ZoneTransfer]`r`nZoneId=3`r`n"
Add-Type -AssemblyName System.IO.Compression

function Check([bool]$Condition, [string]$Name) {
    $results.Add([pscustomobject]@{ name = $Name; passed = $Condition })
    Write-Output ("[{0}] {1}" -f $(if ($Condition) { '通过' } else { '失败' }), $Name)
}

function New-Archive([string]$Directory, [string]$Name, [bool]$Marked = $true, [string]$Manifest = '') {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $file = Join-Path $Directory $Name
    $stream = [IO.File]::Open($file, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('fixture.txt').Open(), $utf8)
            try { $writer.Write('anonymous fixture bytes') } finally { $writer.Dispose() }
            if ($Manifest) {
                $writer = [IO.StreamWriter]::new($archive.CreateEntry('AppxManifest.xml').Open(), $utf8)
                try { $writer.Write($Manifest) } finally { $writer.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    if ($Marked) { [IO.File]::WriteAllText($file + ':Zone.Identifier', $zone, $utf8) }
    return [pscustomobject]@{ path = $file; hash = (Get-FileHash -LiteralPath $file).Hash }
}

function Has-Mark([string]$File) {
    return [bool](@(Get-Item -LiteralPath $File -Stream * -ErrorAction Stop) |
        Where-Object { $_.Stream -eq 'Zone.Identifier' })
}

function Run-Unblock([string]$HostPath, [string]$Directory, [string]$LogName, [bool]$Apply) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $source, '-Path', $Directory)
    if ($Apply) { $arguments += '-Apply' }
    $log = Join-Path $run ($LogName + '.log')
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $HostPath @arguments *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    return [pscustomobject]@{ code = $code; output = [IO.File]::ReadAllText($log) }
}

$hosts = @((Get-Command powershell.exe -ErrorAction Stop).Source,
    (Get-Command pwsh.exe -ErrorAction Stop).Source)

foreach ($hostPath in $hosts) {
    $hostName = [IO.Path]::GetFileNameWithoutExtension($hostPath)
    $prefix = Join-Path $run $hostName
    $empty = Join-Path $prefix 'empty'
    [IO.Directory]::CreateDirectory($empty) | Out-Null
    $result = Run-Unblock $hostPath $empty ($hostName + '-empty') $true
    Check ($result.code -eq 0 -and $result.output.Contains('没找到任何')) "$hostName 无候选成功退出"

    $plain = New-Archive (Join-Path $prefix 'plain') 'Momoka-plain.zip' $false
    $result = Run-Unblock $hostPath ([IO.Path]::GetDirectoryName($plain.path)) ($hostName + '-plain') $true
    Check ($result.code -eq 0 -and $result.output.Contains('什么都没动')) "$hostName 无标记成功退出"
    Check (-not (Has-Mark $plain.path) -and (Get-FileHash -LiteralPath $plain.path).Hash -eq $plain.hash) "$hostName 无标记文件字节不变"

    $inventory = New-Archive (Join-Path $prefix 'inventory') 'Momoka-inventory.zip'
    $result = Run-Unblock $hostPath ([IO.Path]::GetDirectoryName($inventory.path)) ($hostName + '-inventory') $false
    Check ($result.code -eq 0 -and $result.output.Contains('以上只是清点')) "$hostName 只清点成功退出"
    Check ((Has-Mark $inventory.path) -and (Get-FileHash -LiteralPath $inventory.path).Hash -eq $inventory.hash) "$hostName 清点保留实际标记和文件字节"

    foreach ($extension in @('msix', 'appx')) {
        foreach ($identity in @('local', 'store', 'wrong-name', 'wrong-publisher', 'missing', 'malformed', 'dtd')) {
            $name = if ($identity -eq 'store') { 'MOMOKA.Momoka' } elseif ($identity -eq 'wrong-name') { 'Other' } else { 'Momoka' }
            $publisher = if ($identity -eq 'store') { 'CN=1AA8C022-887D-4E5B-BB1D-970781A617A5' } elseif ($identity -eq 'wrong-publisher') { 'CN=Other' } else { 'CN=Momoka' }
            $manifest = '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="' + $name + '" Publisher="' + $publisher + '" Version="0.1.0.0" /></Package>'
            if ($identity -eq 'missing') { $manifest = '' }
            if ($identity -eq 'malformed') { $manifest = '<Package>' }
            if ($identity -eq 'dtd') { $manifest = '<!DOCTYPE Package [<!ENTITY name "Momoka">]><Package><Identity Name="&name;" Publisher="CN=Momoka" /></Package>' }
            $file = New-Archive (Join-Path $prefix ($extension + '-' + $identity)) ('Momoka-fixture.' + $extension) $true $manifest
            $result = Run-Unblock $hostPath ([IO.Path]::GetDirectoryName($file.path)) ($hostName + '-' + $extension + '-' + $identity) $true
            $accepted = $identity -in @('local', 'store')
            Check ($result.code -eq 0 -and ((Has-Mark $file.path) -ne $accepted)) "$hostName $extension $identity 只解除已核对身份的包标记"
            Check ((Get-FileHash -LiteralPath $file.path).Hash -eq $file.hash) "$hostName $extension $identity 主文件字节保持"
        }
        $file = Join-Path $prefix ('Momoka-broken.' + $extension)
        [IO.File]::WriteAllText($file, 'not a zip', $utf8)
        [IO.File]::WriteAllText($file + ':Zone.Identifier', $zone, $utf8)
        $result = Run-Unblock $hostPath $prefix ($hostName + '-' + $extension + '-broken') $true
        Check ($result.code -eq 0 -and (Has-Mark $file)) "$hostName $extension 损坏容器不解除标记"
    }

    foreach ($scenario in @('success', 'failed', 'partial')) {
        $directory = Join-Path $prefix $scenario
        $files = @(New-Archive $directory 'Momoka-a.zip'; New-Archive $directory 'Momoka-b.zip')
        $blocked = if ($scenario -eq 'failed') { @($files) } elseif ($scenario -eq 'partial') { @($files[0]) } else { @() }
        $handles = [Collections.Generic.List[IDisposable]]::new()
        try {
            foreach ($file in $blocked) {
                # This handle permits the production script to read the real ADS but prevents
                # deleting it. No command is mocked, and no ACL or user file is changed.
                $handles.Add([IO.File]::Open($file.path + ':Zone.Identifier', [IO.FileMode]::Open,
                    [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
                Check ([IO.File]::ReadAllText($file.path + ':Zone.Identifier') -eq $zone) "$hostName $scenario 拒绝文件的真实标记仍可读"
            }
            $result = Run-Unblock $hostPath $directory ($hostName + '-' + $scenario) $true
            $expectedCode = if ($scenario -eq 'success') { 0 } else { 1 }
            $expectedDone = $files.Count - $blocked.Count
            Check ($result.code -eq $expectedCode) "$hostName $scenario 退出码应为 $expectedCode，实际 $($result.code)"
            Check ($result.output.Contains("共处理 2 个文件，成功 $expectedDone 个。")) "$hostName $scenario 完整处理与成功计数"
            foreach ($file in $files) {
                $mustRemain = $blocked -contains $file
                Check ((Has-Mark $file.path) -eq $mustRemain) "$hostName $scenario $([IO.Path]::GetFileName($file.path)) 实际标记状态"
                Check ((Get-FileHash -LiteralPath $file.path).Hash -eq $file.hash) "$hostName $scenario $([IO.Path]::GetFileName($file.path)) 主文件字节不变"
                if ($mustRemain) {
                    Check ($result.output.Contains('失败：标记还在') -and
                        [IO.File]::ReadAllText($file.path + ':Zone.Identifier') -eq $zone) "$hostName $scenario 失败报告对应未改动的实际标记"
                }
            }
        } finally {
            foreach ($handle in $handles) { $handle.Dispose() }
        }
        if ($blocked.Count -gt 0) {
            $result = Run-Unblock $hostPath $directory ($hostName + '-' + $scenario + '-retry') $true
            Check ($result.code -eq 0 -and $result.output.Contains("共处理 $($blocked.Count) 个文件，成功 $($blocked.Count) 个。")) "$hostName $scenario 释放句柄后可成功重试"
            foreach ($file in $files) {
                Check (-not (Has-Mark $file.path) -and (Get-FileHash -LiteralPath $file.path).Hash -eq $file.hash) "$hostName $scenario 重试清除标记且主文件字节不变"
            }
        }
    }
}

$failed = @($results | Where-Object { -not $_.passed }).Count
$receipt = [pscustomobject]@{
    source = $source
    sourceSha256 = (Get-FileHash -LiteralPath $source).Hash
    hosts = $hosts
    passed = $results.Count - $failed
    failed = $failed
    skipped = 0
    results = @($results)
}
[IO.File]::WriteAllText((Join-Path $run 'results.json'), ($receipt | ConvertTo-Json -Depth 6) + "`n", $utf8)
Write-Output "解除标记回归：$($results.Count) 项，$failed 失败，0 跳过；证据：$run"
if ($failed -gt 0) { exit 1 }
