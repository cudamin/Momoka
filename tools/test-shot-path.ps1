[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'shot.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'shot.ps1 syntax failed.' }
$functions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Resolve-ShotExecutable' }, $true))
if ($functions.Count -ne 1) { throw 'Missing or ambiguous executable resolver.' }
. ([scriptblock]::Create($functions[0].Extent.Text))
$root = Join-Path $repo ('artifacts/shot-path-tests/' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $root 'artifacts/publish/win-x64/Momoka.exe'
[IO.Directory]::CreateDirectory((Split-Path -Parent $publish)) | Out-Null
[IO.File]::WriteAllText($publish, 'anonymous fixture, never launched')
$passed = 0
function Check([bool]$Condition) { if (-not $Condition) { throw 'Executable resolution contract failed.' }; $script:passed++ }
Check ((Resolve-ShotExecutable '' $root) -eq $publish)
$explicit = Join-Path $root 'explicit [fixture].exe'
[IO.File]::WriteAllText($explicit, 'anonymous fixture, never launched')
Check ((Resolve-ShotExecutable $explicit $root) -eq $explicit)
Push-Location $root
try { Check ((Resolve-ShotExecutable 'explicit [fixture].exe' 'unrelated') -eq $explicit) } finally { Pop-Location }
foreach ($missing in @('', (Join-Path $root 'missing.exe'))) {
    $rejected = $false
    try { Resolve-ShotExecutable $missing (Join-Path $root 'missing-root') | Out-Null } catch { $rejected = $true }
    Check $rejected
}
Check ($null -eq (Resolve-ShotExecutable 'missing.exe' 'missing-root' -Attach))
Write-Output "共 $passed 项，通过 $passed，失败 0，跳过 0；只执行生产路径选择，不启动或附着窗口。"
