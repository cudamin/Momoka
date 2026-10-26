[CmdletBinding()]
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $Path) {
    $Path = Join-Path $root 'src\Momoka.Shell\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\Views\AniBtScrapeDialog.g.cs'
}
$resolved = [IO.Path]::GetFullPath($Path)
if (-not $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Generator evidence must remain inside the current worktree'
}
$text = [IO.File]::ReadAllText($resolved, [Text.UTF8Encoding]::new($false, $true))
$count = 0
function Assert-Contract([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "实际 XAML 生成器契约失败：$Name" }
    $script:count++
    Write-Output "通过 $Name"
}
function Get-ActualMethod([string]$Name) {
    $marker = 'private void ' + $Name + '('
    $start = $text.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $text.IndexOf($marker, $start + $marker.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Missing or ambiguous actual generated method: $Name"
    }
    $open = $text.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $text.Length; $index++) {
        if ($text[$index] -eq '{') { $depth++ }
        if ($text[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $text.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unterminated actual generated method: $Name"
}
Assert-Contract ($text.Contains('GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"')) '真实WinUI生成器来源'
Assert-Contract ($text.Contains('private global::Momoka.Shell.ViewModels.AniBtScrapeRowViewModel dataRoot;')) '行模板绑定真实行VM'
foreach ($member in @('PreviewHeading', 'PreviewDetails')) {
    $body = Get-ActualMethod ('Update_' + $member)
    Assert-Contract ($body.Contains('Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(') -and $body.Contains('DATA_CHANGED')) "$member 绑定实际TextBlock动态更新"
}
Assert-Contract ((Get-ActualMethod 'Update_PreviewVisibility').Contains('Set_Microsoft_UI_Xaml_UIElement_Visibility(')) '实际内联计划Visibility'
Assert-Contract ((Get-ActualMethod 'Update_ViewModel_PrimaryButtonText').Contains('Set_Microsoft_UI_Xaml_Controls_ContentDialog_PrimaryButtonText(')) '实际主按钮预览/确认文本'
Assert-Contract ((Get-ActualMethod 'Update_ViewModel_CanAct').Contains('Set_Microsoft_UI_Xaml_Controls_ContentDialog_IsPrimaryButtonEnabled(')) '实际主按钮CanAct'
Assert-Contract ((Get-ActualMethod 'Update_ViewModel_CancelPreviewVisibility').Contains('Set_Microsoft_UI_Xaml_UIElement_Visibility(')) '实际取消预览按钮显隐'
$notifications = @('PreviewHeading', 'PreviewDetails', 'PreviewVisibility', 'PrimaryButtonText', 'CanAct', 'CancelPreviewVisibility')
Assert-Contract (@($notifications | Where-Object { -not $text.Contains('case "' + $_ + '":') }).Count -eq 0) '真实生成器订阅全部动态成员通知'
Assert-Contract ($text -match 'this\.dataRoot\.Selected\s*=\s*\(global::Momoka\.AniBt\.AniBtAnime\)this\.obj\d+\.SelectedItem;') '实际SelectedItem TwoWay进入生成setter'
Assert-Contract ($text.Contains('.Click += this.OnCancelPreview;')) '实际取消控件事件连到生产处理器'
$hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
Write-Output "实际生成绑定 $count 项通过；0 失败、0 跳过；SHA256=$hash"
