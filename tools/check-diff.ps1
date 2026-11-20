[CmdletBinding()]
param(
    [string]$Repo = '',
    [switch]$All,
    [string[]]$Paths,
    [ValidateNotNullOrEmpty()]
    [string]$Base
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Base -and ($All -or $PSBoundParameters.ContainsKey('Paths'))) {
    throw '-Base 不能和 -All 或 -Paths 一起使用。'
}
# 参数的默认值在脚本根路径就绪前求值（-File 调用时 $PSScriptRoot 可能还是空串），
# 所以仓库路径放到正文里补，不在 param 块里 Join-Path。
if (-not $Repo) { $Repo = Join-Path $PSScriptRoot '..' }

# 检查的是「这次改动会不会把上一轮的 CRLF 事故再带回来」。2026-09-27 有 167 行的文件整份变成 CRLF，
# 而 git 的行尾归一化让 diff 看起来完全干净，闸门 1 的 dotnet format whitespace 才把它抓住。
# 这个脚本把同一类问题提前到提交前：行尾、末尾换行、非 .ps1 文件的 BOM、非法 UTF-8、空白错误。

$script:violations = 0
$script:gitExit = 0

# git 默认按 UTF-8 输出，并把非 ASCII 路径写成带引号的八进制转义（core.quotepath）。
# 那样拿到的 "docs/\346..." 含引号，GetFullPath 会直接报「路径中具有非法字符」。
# 统一按 UTF-8 解码，并让 git 原样输出路径。
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Fail([string]$Message) {
    $script:violations++
    Write-Output "[失败] $Message"
}

# 在 ErrorActionPreference=Stop 下，原生命令写到 stderr 的一行（比如没有 HEAD 的仓库里
# git diff HEAD 的 fatal）会被当成终止错误。这里把那一段临时降为 Continue，退出码照常交回。
function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& git -c core.quotepath=false -C $RepoPath @Arguments 2>$null)
        $script:gitExit = $LASTEXITCODE
        return $output
    } finally {
        $ErrorActionPreference = $saved
    }
}

function Resolve-Commit([string]$Ref) {
    $commit = @(Invoke-Git $root @('rev-parse', '--verify', '--end-of-options', "${Ref}^{commit}"))
    if ($gitExit -ne 0 -or $commit.Count -ne 1 -or $commit[0] -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw "找不到提交：$Ref"
    }
    return $commit[0]
}

# Windows PowerShell 的原生命令管道会转码；读取 Git 对象时必须保留原始字节。
# 命令行仅使用固定选项和已解析的 Git 对象 ID，不传文件名或用户输入的引用。
function Read-GitBytes([string]$Command, [string]$Failure) {
    $start = [Diagnostics.ProcessStartInfo]::new($gitPath, $Command)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    $buffer = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw $Failure }
        return ,$buffer.ToArray()
    } finally {
        $buffer.Dispose()
        $process.Dispose()
    }
}

$rootLines = @(Invoke-Git $Repo @('rev-parse', '--show-toplevel'))
if ($gitExit -ne 0 -or $rootLines.Count -eq 0 -or -not $rootLines[0]) { throw "不是 Git 工作树：$Repo" }
$root = [IO.Path]::GetFullPath(([string]$rootLines[0]).Trim())

$files = [Collections.Generic.List[string]]::new()
$utf8Strict = [Text.UTF8Encoding]::new($false, $true)
$blobs = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
if ($Base) {
    $gitPath = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    $baseCommit = Resolve-Commit $Base
    $headCommit = Resolve-Commit 'HEAD'
    Write-Output "检查已提交范围：$baseCommit → $headCommit"
    # 不检测重命名：目标按新增处理。用 NUL 分隔，按目标文件模式排除符号链接和子模块。
    $raw = Read-GitBytes "diff --raw -z --no-abbrev --no-renames --no-relative --ignore-submodules=all --diff-filter=AMT $baseCommit $headCommit --" '无法读取提交范围的变更文件。'
    $entries = $utf8Strict.GetString($raw).Split([char]0)
    for ($i = 0; $i -lt $entries.Length - 1; $i += 2) {
        $fields = $entries[$i].Split(' ')
        if ($fields.Length -ne 5 -or $i + 1 -ge $entries.Length - 1 -or
            $fields[3] -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
            throw '无法解析 Git 变更文件记录。'
        }
        if ($fields[1] -notin @('100644', '100755')) { continue }
        $path = $entries[$i + 1]
        $files.Add($path)
        $blobs.Add($path, $fields[3])
    }
} elseif ($Paths) {
    foreach ($path in $Paths) { $files.Add($path) }
} elseif ($All) {
    foreach ($path in @(Invoke-Git $root @('ls-files'))) { if ($path) { $files.Add([string]$path) } }
} else {
    # 已跟踪的改动（含暂存）加上未跟踪的新文件；删除的文件不在磁盘上，后面按不存在跳过。
    $tracked = @(Invoke-Git $root @('diff', '--name-only', 'HEAD'))
    if ($gitExit -ne 0) { $tracked = @(Invoke-Git $root @('diff', '--name-only')) }
    foreach ($path in $tracked) { if ($path) { $files.Add([string]$path) } }
    foreach ($path in @(Invoke-Git $root @('ls-files', '--others', '--exclude-standard'))) {
        if ($path) { $files.Add([string]$path) }
    }
}

$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$checked = 0

if (-not $Base) { $files = @($files | Sort-Object -Unique) }
foreach ($relative in $files) {
    if ($Base) {
        $bytes = Read-GitBytes "cat-file blob $($blobs[$relative])" "无法读取提交中的文件：$relative"
    } else {
        $relative = $relative.Trim()
        if (-not $relative) { continue }
        if (-not $seen.Add($relative)) { continue }
        $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if ($full -ne $root -and
            -not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Fail "$relative 越过工作树"
            continue
        }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

        $bytes = [IO.File]::ReadAllBytes($full)
    }
    if ($bytes.Length -eq 0) { continue }
    $checked++
    $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()

    # 二进制文件（前 8 KB 有 NUL）不按文本规则检查。图片、dll 不该被当作文本改坏。
    $probe = [Math]::Min($bytes.Length, 8192)
    $isBinary = $false
    for ($i = 0; $i -lt $probe; $i++) { if ($bytes[$i] -eq 0) { $isBinary = $true; break } }
    if ($isBinary) { continue }

    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Contains("`r`n")) { Fail "$relative 使用了 CRLF（仓库约定 LF）" }
    if ($bytes[$bytes.Length - 1] -ne 10) { Fail "$relative 末尾没有换行" }

    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    # .ps1 必须带 BOM（5.1 读中文注释靠它）；其余文本文件不要 BOM。
    if ($extension -eq '.ps1') {
        if (-not $hasBom) { Fail "$relative 是 .ps1 但没有 UTF-8 BOM" }
    } elseif ($hasBom -and $extension -in @('.cs', '.md', '.json', '.xml', '.xaml', '.csproj', '.props', '.targets', '.sln', '.txt', '.yml', '.yaml')) {
        Fail "$relative 带 UTF-8 BOM（.editorconfig 约定 charset=utf-8）"
    }

    # 非法 UTF-8 说明文件被别的编码写过，继续往下看只会得到乱码结论。
    try { $null = $utf8Strict.GetString($bytes) } catch { Fail "$relative 不是合法 UTF-8" }
}

# 没有 HEAD 的新仓库仅做文件字节检查；提交模式使用上面固定的同一范围。
$diffCheck = @()
if ($Base) {
    $diffCheck = @(Invoke-Git $root @("--attr-source=$headCommit", 'diff', '--check', '--no-ext-diff', '--no-textconv', $baseCommit, $headCommit, '--'))
} else {
    $null = @(Invoke-Git $root @('rev-parse', '--verify', 'HEAD'))
    if ($gitExit -eq 0) {
        $diffCheck = @(Invoke-Git $root @('diff', '--check', 'HEAD'))
    }
}
if ($gitExit -ne 0) {
    $diagnostics = @($diffCheck | Where-Object { $_ -match '^(?!\+).+:\d+: ' })
    foreach ($line in $diagnostics) { Fail "git diff --check：$line" }
    if ($Base -and $diagnostics.Count -eq 0) {
        throw '无法完成提交范围的 Git 空白检查（-Base 需要 Git 2.41 或更新版本）。'
    }
}

Write-Output "检查了 $checked 个文本文件（$($files.Count) 个变更路径）"
if ($violations -gt 0) {
    Write-Output "共 $violations 处问题。"
    [Console]::OutputEncoding = $previousEncoding
    exit 1
}
Write-Output '行尾、末尾换行、BOM 与空白检查通过。'
[Console]::OutputEncoding = $previousEncoding
exit 0
