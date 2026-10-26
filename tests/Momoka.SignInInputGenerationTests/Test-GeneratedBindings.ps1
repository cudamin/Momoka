[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BuildReceipt,
    [switch]$Counterexamples
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$receiptPath = (Resolve-Path -LiteralPath $BuildReceipt).Path
if (-not $receiptPath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '生成证据必须来自本树成功构建回执'
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($receipt.workingTree -ne $root -or $receipt.exitCode -ne 0) { throw '不是本树当前成功Shell构建回执' }
foreach ($input in $receipt.inputs) {
    if (-not (Test-Path -LiteralPath $input.path -PathType Leaf) -or (Get-FileHash -LiteralPath $input.path).Hash -ne $input.sha256) {
        throw '成功构建后源码已变；必须先重新成功编译当前代码'
    }
}
$expected = Join-Path $root 'src\Momoka.Shell\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\Views\SignInPage.g.cs'
if ($receipt.generated.path -ne $expected -or (Get-FileHash -LiteralPath $expected).Hash -ne $receipt.generated.sha256) {
    throw '只读本树当前实际SignInPage.g.cs，不接受backup/g.i或别树输入'
}
if ((Get-FileHash -LiteralPath $receipt.assembly.path).Hash -ne $receipt.assembly.sha256) {
    throw '当前Shell程序集与构建回执不同'
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$page = [IO.File]::ReadAllText($expected, $utf8)
$propertiesPath = Join-Path $root 'artifacts\r008-signin-input-generation\build-current\obj\generated\CommunityToolkit.Mvvm.SourceGenerators\CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator\Momoka.Shell.ViewModels.SignInViewModel.g.cs'
$commandPath = Join-Path $root 'artifacts\r008-signin-input-generation\build-current\obj\generated\CommunityToolkit.Mvvm.SourceGenerators\CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator\Momoka.Shell.ViewModels.SignInViewModel.SignInAsync.g.cs'
$properties = [IO.File]::ReadAllText($propertiesPath, $utf8)
$command = [IO.File]::ReadAllText($commandPath, $utf8)

function Assert-Contract([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "实际生成器契约失败：$Name" }
    $script:count++
    if (-not $script:quiet) { Write-Output "通过 $Name" }
}

function Get-ActualMethod([string]$Text, [string]$Name) {
    $marker = 'private void ' + $Name + '('
    $start = $Text.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $Text.IndexOf($marker, $start + $marker.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "缺失或有歧义的实际生成方法：$Name"
    }
    $open = $Text.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $Text.Length; $index++) {
        if ($Text[$index] -eq '{') { $depth++ }
        if ($Text[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($start, $index - $start + 1) }
        }
    }
    throw "实际生成方法未闭合：$Name"
}

function Assert-GeneratedBindings([string]$PageText, [string]$PropertyText, [string]$CommandText) {
    Assert-Contract ($PageText.Contains('GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"')) '真正WinUI生成器标记'
    foreach ($binding in @(
        @('Username', 'TextBox_Text'), @('Address', 'TextBox_Text'),
        @('Password', 'PasswordBox_Password'), @('SelectedUser', 'Primitives_Selector_SelectedItem'))) {
        $body = Get-ActualMethod $PageText ('Update_ViewModel_' + $binding[0])
        Assert-Contract ($body.Contains('Set_Microsoft_UI_Xaml_Controls_' + $binding[1] + '(') -and $body.Contains('DATA_CHANGED')) ($binding[0] + '实际控件双向动态目标')
    }
    foreach ($binding in @(@('Username', 'Text'), @('Address', 'Text'), @('Password', 'Password'), @('SelectedUser', 'SelectedItem'))) {
        $member = [Regex]::Escape($binding[0])
        $property = [Regex]::Escape($binding[1])
        Assert-Contract ($PageText -match ('this\.dataRoot\.ViewModel\.' + $member + '\s*=\s*(?:\(global::Momoka\.Emby\.EmbyUser\))?this\.obj\d+\.' + $property + ';')) ($binding[0] + '实际控件写回生产VM')
    }
    foreach ($property in @('TextBox.TextProperty', 'PasswordBox.PasswordProperty', 'Primitives.Selector.SelectedItemProperty')) {
        Assert-Contract ($PageText.Contains('RegisterPropertyChangedCallback(global::Microsoft.UI.Xaml.Controls.' + $property)) ($property + '即时属性通知而非失焦写回')
    }
    Assert-Contract ((Get-ActualMethod $PageText 'Update_ViewModel_SignInCommand').Contains('Set_Microsoft_UI_Xaml_Controls_Primitives_ButtonBase_Command(')) '真实提交按钮连接生成命令'
    Assert-Contract ((Get-ActualMethod $PageText 'Update_ViewModel_BackToAddressCommand').Contains('Set_Microsoft_UI_Xaml_Controls_Primitives_ButtonBase_Command(')) '真实返回按钮连接原取消命令'
    Assert-Contract (@(@('Username', 'Address', 'Password', 'SelectedUser', 'SignInText') | Where-Object { -not $PageText.Contains('case "' + $_ + '":') }).Count -eq 0) '实际WinUI绑定订阅身份与Busy文案通知'
    Assert-Contract ($PropertyText.Contains('OnSelectedUserChanged(value);') -and $PropertyText.Contains('OnUsernameChanged(__oldValue, value);')) '真实Toolkit调用生产选中行及用户名身份回调'
    Assert-Contract ($CommandText.Contains('CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator') -and
        $CommandText.Contains('() => CanRun, global::CommunityToolkit.Mvvm.Input.AsyncRelayCommandOptions.AllowConcurrentExecutions')) '真实生成命令使用Busy门控并放行旧票尚未返回的新提交'
}

$count = 0
$quiet = $false
if (-not $Counterexamples) {
    Assert-GeneratedBindings $page $properties $command
    Write-Output "当前实际生成绑定 $count 项通过；0 失败、0 跳过；WinUI SHA256=$($receipt.generated.sha256)"
    Write-Output "Toolkit属性 SHA256=$((Get-FileHash -LiteralPath $propertiesPath).Hash)"
    Write-Output "Toolkit命令 SHA256=$((Get-FileHash -LiteralPath $commandPath).Hash)"
}
else {
    $quiet = $true
    $rejected = 0
    $cases = @(
        @{ name = '空/format清空生成文件'; page = ''; properties = $properties; command = $command },
        @{ name = '缺真正XAML生成器'; page = $page.Replace('Microsoft.UI.Xaml.Markup.Compiler', 'NotACompiler'); properties = $properties; command = $command },
        @{ name = '重复用户名生成方法'; page = $page + "`nprivate void Update_ViewModel_Username() { }"; properties = $properties; command = $command },
        @{ name = '缺实际SelectedItem写回'; page = $page.Replace('this.dataRoot.ViewModel.SelectedUser =', 'this.dataRoot.ViewModel.NotSelectedUser ='); properties = $properties; command = $command },
        @{ name = '缺即时文本属性回调'; page = $page.Replace('RegisterPropertyChangedCallback(global::Microsoft.UI.Xaml.Controls.TextBox.TextProperty', 'RegisterPropertyChangedCallback(global::Microsoft.UI.Xaml.Controls.TextBox.NotTextProperty'); properties = $properties; command = $command },
        @{ name = '缺动态文案通知'; page = $page.Replace('case "SignInText":', 'case "NotSignInText":'); properties = $properties; command = $command },
        @{ name = '缺真实Toolkit身份回调'; page = $page; properties = $properties.Replace('OnSelectedUserChanged(value);', ''); command = $command },
        @{ name = '生成命令仍拒新票并发'; page = $page; properties = $properties; command = $command.Replace('AsyncRelayCommandOptions.AllowConcurrentExecutions', 'AsyncRelayCommandOptions.None') }
    )
    foreach ($case in $cases) {
        $failed = $false
        try { Assert-GeneratedBindings $case.page $case.properties $case.command }
        catch { $failed = $true; Write-Output "预期拒绝 $($case.name)：$($_.Exception.Message)" }
        if (-not $failed) { throw "生成验证器未拒绝反例：$($case.name)" }
        $rejected++
    }
    Write-Output "生成器验证反例 $rejected 项全部预期拒绝；0 意外通过、0 跳过。仅内存破坏副本，未改真实g.cs。"
}
