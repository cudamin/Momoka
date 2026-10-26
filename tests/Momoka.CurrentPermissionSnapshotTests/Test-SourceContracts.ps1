[CmdletBinding()]
param(
    [string]$RunName = 'source-contracts',
    [string]$Dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0 -or $gitRoot -ne $root) { throw '必须是本专项所属工作树' }
if ($RunName -notmatch '^[a-z0-9-]+$') { throw 'RunName仅允许小写字母/数字/连字符' }
$evidence = Join-Path $root 'artifacts\r011-current-permission'
$run = Join-Path $evidence ('contracts-' + $RunName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
[void][IO.Directory]::CreateDirectory($run)
$encoding = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$CheckName, [scriptblock]$Body) {
    try { & $Body; $checks.Add(@{ name = $CheckName; outcome = 'passed' }); Write-Output "通过 $CheckName" }
    catch { $checks.Add(@{ name = $CheckName; outcome = 'failed'; message = $_.Exception.Message }); Write-Output "失败 $CheckName —— $($_.Exception.Message)" }
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Between([string]$Text, [string]$Start, [string]$End) {
    $begin = $Text.IndexOf($Start, [StringComparison]::Ordinal)
    if ($begin -lt 0 -or $Text.IndexOf($Start, $begin + $Start.Length, [StringComparison]::Ordinal) -ge 0) { throw 'Missing/ambiguous start' }
    $finish = $Text.IndexOf($End, $begin, [StringComparison]::Ordinal)
    if ($finish -lt 0) { throw 'Missing end' }
    return $Text.Substring($begin, $finish - $begin)
}

Push-Location -LiteralPath $root
try {
    $sessionPath = Join-Path $root 'src\Momoka.Core\Emby\EmbySession.cs'
    $scopePath = Join-Path $root 'src\Momoka.Core\Emby\EmbySessionScope.cs'
    $pagePath = Join-Path $root 'src\Momoka.Shell\Views\SettingsPage.xaml.cs'
    $session = [IO.File]::ReadAllText($sessionPath)
    $scope = [IO.File]::ReadAllText($scopePath)
    $page = [IO.File]::ReadAllText($pagePath)
    Check 'Current Scope Connection仍是get-only捕获快照，权限走独立读数' {
        Require ($scope.Contains('Connection = identity.Client.Connection;') -and $scope.Contains('public EmbyConnection Connection { get; }')) 'Connection快照不能改成当前'
        Require ($scope.Contains('_session.CurrentPermissions(this).IsAdministrator') -and $scope.Contains('_session.CurrentPermissions(this).Revision')) '缺权限读数'
    }
    Check '匿名ReplaceClient缝只同身份客户端与权限代次，无登录/凭据/持久化/退出' {
        $seam = Between $session '    internal EmbyClient ReplaceClient(' '    internal bool IsCurrent('
        Require ($seam.Contains('IsAvailable(scope)') -and $seam.Contains('IsSameIdentityAs(connection)')) '缺身份守卫'
        Require ($seam.Contains('state.PermissionRevision++') -and $seam.Contains('new EmbyClient(_http, connection)')) '缺当前快照提交'
        Require ($seam -notmatch 'Authenticate|TryReauthenticate|SaveCredentials|Persist\(|_vault|DPAPI|SignOut|EndSource|Adopt\(') '匿名权限缝触及停止链'
    }
    Check '生产成功重认证替换接线只静态核验，权限通知在生命周期锁外且原checks保留' {
        $reauth = Between $session '    private async Task<EmbyClient?> TryReauthenticateAsync(' '    private async Task<EmbyConnection> ResolveRestoreConnectionAsync('
        Require ($reauth.Contains('replacement = ReplaceClient(scope, connection);') -and $reauth.Contains('NotifyPermissionsChanged(scope);')) '缺生产接线'
        foreach ($guard in @('CanCommit(scope.Key, generation)', '!IsAvailable(scope)', '!ReferenceEquals(scope.Client, stale)',
            '!HasAddress(state.Server, stale.Connection.ApiBase)', 'state.Account.ProtectedPassword != savedPassword', 'connection.IsSameIdentityAs(stale.Connection)')) {
            Require ($reauth.Contains($guard)) ('原身份/提交守卫丢失：' + $guard)
        }
        Require ($reauth -notmatch 'NotifySourcesChanged\(') '同身份权限不能重建Shell来源'
    }
    Check '身份/认证/恢复/改密安全链的非权限方法与基点逐字相同' {
        $old = (& git -C $root show '9f07bbddbc1795b9e426cb6a25d0650dd60c8d9c:src/Momoka.Core/Emby/EmbySession.cs') -join "`n"
        if ($LASTEXITCODE -ne 0) { throw '无法读取基点源码' }
        $current = $session.TrimEnd("`r", "`n")
        foreach ($pair in @(
            @('    private void SaveCredentials(', '    // 所有调用方都持有生命周期锁'),
            @('    private async Task<EmbyConnection> ResolveRestoreConnectionAsync(', '    private void SaveCredentials('),
            @('    public void ApplyPasswordChange(', '    public Task<bool> TryRestoreAsync('),
            @('    public async Task SignInAsync(', '    /// <summary>' + "`n" + '    /// 当前账号在服务器上改了密码'),
            @('    public Task<bool> TryRestoreAsync(', '    public List<EmbyItem>? TakeRestoredViews('),
            @('    private async Task<T> ExecuteWithSourceAsync', '    private async Task<EmbyClient?> TryReauthenticateAsync(')
        )) { Require ((Between $old $pair[0] $pair[1]) -ceq (Between $current $pair[0] $pair[1])) ('停止链非权限方法漂移：' + $pair[0]) }
    }
    Check 'SettingsPage仅来源权限订阅/退订/适配回调hunk，R010与其它页面业务逐字不改' {
        $old = ((& git -C $root show '5fa9c96aedf949e8aa5e28d79ab72e2dc4104dbf:src/Momoka.Shell/Views/SettingsPage.xaml.cs') -join "`n").TrimEnd("`r", "`n")
        if ($LASTEXITCODE -ne 0) { throw '无法读取R010已合入、R011前的精确主树页面' }
        $narrow = $page.Replace("        _session.PermissionsChanged += OnPermissionsChanged;`n", '')
        $detach = @('        if (_session is not null)', '        {', '            _session.SourcesChanged -= OnSourceListChanged;',
            '            _session.PermissionsChanged -= OnPermissionsChanged;', '        }') -join "`n"
        $narrow = $narrow.Replace($detach, '        if (_session is not null) _session.SourcesChanged -= OnSourceListChanged;')
        $callback = @('    // 只刷新管理目录与已打开页；同身份权限通知不重建Shell来源、也不改设置保存状态。',
            '    private void OnPermissionsChanged(object? sender, EmbySessionScope source) => OnSourceListChanged(sender, EventArgs.Empty);', '', '') -join "`n"
        $narrow = $narrow.Replace($callback, '')
        Require ($narrow.TrimEnd("`r", "`n") -ceq $old) 'SettingsPage存在权限hunk外改动'
    }
    foreach ($name in @('Libraries', 'Users', 'Settings', 'Tasks')) {
        Check "真实XAML生成 $name ContentVisibility有通知分支/整区域设置、命令绑定" {
            $generated = Join-Path $root "src\Momoka.Shell\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\Views\FnNas${name}Page.g.cs"
            Require (Test-Path -LiteralPath $generated -PathType Leaf) '必须先成功编译当前Shell生成器'
            $binding = [IO.File]::ReadAllText($generated)
            Require ($binding.Contains('case "ContentVisibility":') -and $binding.Contains('Update_ViewModel_ContentVisibility(obj.ContentVisibility, DATA_CHANGED)')) '缺权限变化XAML通知更新'
            Require ($binding.Contains('Update_ViewModel_SaveCommand') -and $binding.Contains('Update_ViewModel_RefreshCommand')) '缺命令绑定'
        }
        Check "真实Toolkit生成 $name SaveCommand实际委托/CanSave生成" {
            $generated = Join-Path $PSScriptRoot "obj\Release\net10.0\generated\CommunityToolkit.Mvvm.SourceGenerators\CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator\Momoka.Shell.ViewModels.FnNas${name}ViewModel.SaveAsync.g.cs"
            Require (Test-Path -LiteralPath $generated -PathType Leaf) '必须先成功编译当前专项'
            $command = [IO.File]::ReadAllText($generated)
            Require ($command.Contains('SaveAsync') -and $command.Contains('CanSave') -and $command.Contains('AsyncRelayCommand')) '未执行真实MVVM生成'
        }
    }
    Check '主树R010保存/密码实现与共享规则相对R011前提交不变，caller仅两份经复核pin' {
        # The integrator already completed R010/R008. Compare forbidden product edits to that
        # exact parent, not the worker's older base; integration-only solution/fake/pin changes
        # are checked separately rather than blindly accepting a new baseline.
        $parent = '5fa9c96aedf949e8aa5e28d79ab72e2dc4104dbf'
        $gitArgs = @('-C', $root, 'diff', '--name-only', $parent, '--', 'Directory.Build.props',
            'tests/Momoka.Tests/Program.cs', 'src/Momoka.Core/Configuration/SettingsStore.cs',
            'src/Momoka.Core/Services/SettingsService.cs', 'src/Momoka.Core/Services/ISettingsPersistence.cs',
            'src/Momoka.Shell/Views/FnNasPasswordPage.xaml.cs')
        $changed = @(& git @gitArgs)
        Require ($LASTEXITCODE -eq 0 -and $changed.Count -eq 0) 'R010或共享规则发生未授权修改'
        $beforePassword = (& git -C $root show ($parent + ':src/Momoka.Shell/ViewModels/FnNasPasswordViewModel.cs')) -join "`n"
        Require ($LASTEXITCODE -eq 0) '无法读取R010密码页基点'
        $currentPassword = [IO.File]::ReadAllText((Join-Path $root 'src/Momoka.Shell/ViewModels/FnNasPasswordViewModel.cs'))
        $guard = "        if (IsDisposed) return;`n"
        Require ($currentPassword.Split($guard, [StringSplitOptions]::None).Length -eq 2) '派生Attach释放守卫必须唯一'
        Require ($currentPassword.Replace($guard, '').TrimEnd("`r", "`n") -ceq $beforePassword.TrimEnd("`r", "`n")) '密码页除已复现的Dispose附加守卫外不得修改'
        $beforeText = (& git -C $root show ($parent + ':tests/Momoka.NaturalContinuationCallerTests/SourcePins.json')) -join "`n"
        Require ($LASTEXITCODE -eq 0) '无法读取caller复核前pin'
        $before = ConvertFrom-Json -InputObject $beforeText
        $after = Get-Content -LiteralPath (Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/SourcePins.json') -Raw | ConvertFrom-Json
        Require ($before.sources.Count -eq 19 -and $after.sources.Count -eq 19) 'caller源码边界不能扩缩'
        foreach ($pin in $after.sources) {
            $prior = @($before.sources | Where-Object path -ceq $pin.path)
            Require ($prior.Count -eq 1) 'caller边界项必须唯一且保留'
            if ($pin.path -notin @('src/Momoka.Core/Emby/EmbySession.cs', 'src/Momoka.Core/Emby/EmbySessionScope.cs')) {
                Require ($pin.sha256 -ceq $prior[0].sha256) ('未复核caller pin漂移：' + $pin.path)
            }
            Require ((Get-FileHash -LiteralPath (Join-Path $root $pin.path)).Hash -ieq $pin.sha256) ('caller当前输入hash不符：' + $pin.path)
        }
    }
    Check '所有任务写入只在允许生产文件/独立专项/PROGRESS，本树UTF8/LF' {
        $paths = @(& git -C $root -c core.quotepath=false diff --name-only) + @(& git -C $root -c core.quotepath=false ls-files --others --exclude-standard)
        foreach ($relative in ($paths | Sort-Object -Unique)) {
            Require ($relative -match '^(PROGRESS.md|Momoka.sln|docs/(开发与验证|代码审查-分阶段修复-2026-10-23)\.md|\.claude/skills/(momoka-winui-shell|momoka-fnnas-media-api)/SKILL\.md|src/Momoka.Core/(Emby/EmbySession(Scope)?\.cs|Services/ServerCapabilities\.cs)|src/Momoka.Shell/(ViewModels/(FnNas(Admin|Session|Password)ViewModel|SettingsViewModel)\.cs|Views/SettingsPage.xaml.cs)|tests/Momoka.FnNasAccountEditTests/Boundaries\.cs|tests/Momoka.NaturalContinuationCallerTests/SourcePins\.json|tests/Momoka.SettingsPersistenceTests/(Program\.cs|README\.md)|tests/Momoka.CurrentPermissionSnapshotTests/([^/]+|AccountCompatibility/[^/]+))$') ('越界写入：' + $relative)
            $path = Join-Path $root $relative
            $bytes = [IO.File]::ReadAllBytes($path)
            $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
            Require (-not $text.Contains("`r") -and $text.EndsWith("`n")) ('LF/末尾换行：' + $relative)
            if ($relative.EndsWith('.cs') -or $relative.EndsWith('.md')) { Require (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) ('C#/md不加BOM：' + $relative) }
        }
    }
    $project = Join-Path $PSScriptRoot 'Momoka.CurrentPermissionSnapshotTests.csproj'
    $env:DOTNET_CLI_HOME = Join-Path $evidence 'cli-home'
    $env:DOTNET_ROOT = Split-Path -Parent $Dotnet
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
    $env:NUGET_PACKAGES = Join-Path $evidence 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'nuget-http'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $evidence 'nuget-plugins'
    $env:TEMP = Join-Path $evidence 'temp'
    $env:TMP = $env:TEMP
    $env:MSBUILDDISABLENODEREUSE = '1'
    foreach ($variant in @('missing', 'ambiguous')) {
        Check "原样Page提取器拒绝${variant}边界，不执行旧测试DLL" {
            $sourceRoot = Join-Path $run $variant
            $sourceDir = Join-Path $sourceRoot 'src\Momoka.Shell\Views'
            [void][IO.Directory]::CreateDirectory($sourceDir)
            $modified = if ($variant -eq 'missing') { $page.Replace('    private void OnSourceListChanged(', '    private void FixtureMissingCallback(') }
                else { $page + "`n    private void OnSourceListChanged( // duplicate extraction boundary`n" }
            [IO.File]::WriteAllText((Join-Path $sourceDir 'SettingsPage.xaml.cs'), $modified, $encoding)
            Copy-Item -LiteralPath (Join-Path $root 'src\Momoka.Shell\Views\FnNasPlaylistFile.cs') -Destination (Join-Path $sourceDir 'FnNasPlaylistFile.cs')
            $log = Join-Path $run ($variant + '-extraction.log')
            $args = @('msbuild', $project, '-t:GeneratePermissionPageSources', '-p:Configuration=Release', ('-p:PermissionSourceRoot=' + $sourceRoot),
                '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:MSBuildNodeReuse=false')
            & $Dotnet @args > $log 2>&1
            $code = $LASTEXITCODE
            [IO.File]::WriteAllText((Join-Path $run ($variant + '-extraction.exit.txt')), "$code`n", $encoding)
            $output = [IO.File]::ReadAllText($log)
            Require ($code -ne 0 -and ($output.Contains('Missing or ambiguous production start') -or $output.Contains('Missing production end:     private void OnSourceListChanged('))) '反例未在真实提取器明确拒绝'
        }
    }
    $failed = @($checks | Where-Object outcome -eq 'failed').Count
    [IO.File]::WriteAllText((Join-Path $run 'results.json'), (@{ checks = $checks; passed = $checks.Count - $failed; failed = $failed; skipped = 0 } | ConvertTo-Json -Depth 5) + "`n", $encoding)
    Write-Output "源码/绑定边界 $($checks.Count)项，失败$failed；证据$run。反例之后必须重新编译当前专项再运行。"
    if ($failed -gt 0) { throw '源码/绑定/提取检查失败；保留日志' }
}
finally { Pop-Location }
# Expected extraction failures leave the last native process exit code at 1.
# The contract runner's success is the complete check ledger above, not that last child code.
exit 0
