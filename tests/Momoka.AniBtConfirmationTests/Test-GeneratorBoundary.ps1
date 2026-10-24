[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source = Join-Path $root 'src\Momoka.Shell\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\Views\AniBtScrapeDialog.g.cs'
$check = Join-Path $PSScriptRoot 'Test-GeneratedBindings.ps1'
$text = [IO.File]::ReadAllText($source)
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$evidence = Join-Path $root 'artifacts\r037-r039\generator-boundary'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$cases = @(
    @{ Name = 'empty'; Marker = ''; Replacement = '' },
    @{ Name = 'missing-preview'; Marker = 'private void Update_PreviewDetails('; Replacement = 'private void Removed_PreviewDetails(' },
    @{ Name = 'wrong-owner'; Marker = 'private global::Momoka.Shell.ViewModels.AniBtScrapeRowViewModel dataRoot;'; Replacement = 'private global::System.Object dataRoot;' },
    @{ Name = 'missing-notification'; Marker = 'case "PreviewVisibility":'; Replacement = 'case "NotPreviewVisibility":' },
    @{ Name = 'wrong-primary'; Marker = 'Set_Microsoft_UI_Xaml_Controls_ContentDialog_PrimaryButtonText('; Replacement = 'Set_OtherControl_Text(' },
    @{ Name = 'missing-cancel-handler'; Marker = '.Click += this.OnCancelPreview;'; Replacement = '.Click += this.OtherClick;' }
)
$pwsh = Join-Path $PSHOME 'pwsh.exe'
foreach ($case in $cases) {
    if ($case.Name -ne 'empty' -and -not $text.Contains($case.Marker)) { throw "Actual generator mutation marker missing: $($case.Name)" }
    $input = if ($case.Name -eq 'empty') { '' } else { $text.Replace($case.Marker, $case.Replacement) }
    $path = [IO.Path]::GetFullPath((Join-Path $evidence ($case.Name + '.cs')))
    if (-not $path.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Mutation escaped evidence directory' }
    [IO.File]::WriteAllText($path, $input, [Text.UTF8Encoding]::new($false))
    & $pwsh -NoProfile -File $check -Path $path 2>&1 | Out-File -LiteralPath (Join-Path $evidence ($case.Name + '.log')) -Encoding utf8
    if ($LASTEXITCODE -ne 1) { throw "Expected rejection exit 1: $($case.Name), actual $LASTEXITCODE" }
    Write-Output "通过 拒绝 $($case.Name)，预期子进程退出1"
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $hash) { throw 'Actual generated output was modified' }
Write-Output "生成器边界拒绝验证：$($cases.Count) 项通过，0 失败、0 跳过；原生成文件未改。"
