using System.Security.Cryptography;
using Microsoft.UI.Xaml.Controls;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string ScrobblingCategory = "打格子";
    private ScrobblingService? _scrobbling;
    private Func<Task>? _doubanLogin;
    private Func<Task>? _doubanLogout;
    private Func<Task>? _verifyScrobbling;
    private Action<ScrobblingProvider>? _showScrobblingRecords;
    private bool? _doubanSessionAction;
    private bool _manualScrobblingCheck;
    private SettingSecretRow? _bangumiSecret;

    internal void UseScrobbling(ScrobblingService service, Func<Task> doubanLogin, Func<Task> doubanLogout, Func<Task>? verify = null)
    {
        _scrobbling = service;
        _doubanLogin = doubanLogin;
        _doubanLogout = doubanLogout;
        _verifyScrobbling = verify;
        if (IsReady) RefreshScrobblingRows();
    }

    internal void UseScrobblingRecords(Action<ScrobblingProvider> showRecords) => _showScrobblingRecords = showRecords;

    internal void ReportScrobblingRecordsOpenFailed() =>
        Notify(null, UiText.Translate("无法打开同步记录，请稍后重试。"), InfoBarSeverity.Error);

    private IReadOnlyList<SettingSection> ScrobblingSections()
    {
        var settings = Settings.Scrobbling;
        var bangumi = settings.Bangumi;
        var douban = settings.Douban;
        var bangumiRows = new List<SettingRow>
        {
            Toggle("启用 Bangumi 打格子", "看完一集后，自动匹配 Bangumi 条目并把对应集标记为看过。",
                () => bangumi.Enabled, value => bangumi.Enabled = value, after: ScrobblingConfigurationChanged)
        };
        if (bangumi.Enabled)
        {
            _bangumiSecret ??= new SettingSecretRow("Bangumi Access Token", value =>
            {
                try
                {
                    var protectedToken = string.IsNullOrWhiteSpace(value) ? "" : _secretProtector.Protect(value.Trim());
                    if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(protectedToken)) throw new CryptographicException();
                    Settings.Scrobbling.Bangumi.ProtectedAccessToken = protectedToken;
                }
                catch (Exception) { throw new CryptographicException("无法加密 Bangumi 令牌"); }
            }, () => Settings.Scrobbling.Bangumi.ProtectedAccessToken.Length > 0, TrySaveScrobblingToken, ScrobblingConfigurationChanged,
                canWrite: () => ScrobblingActive && ScrobblingEnabled(ScrobblingProvider.Bangumi));
            bangumiRows.Add(_bangumiSecret);
            bangumiRows.Add(ScrobblingConnectionRow(ScrobblingProvider.Bangumi, "Bangumi 连接状态",
                "自动验证已保存的密钥；所有媒体服务器账号的播放都参与同步。", ConnectionStatus(ScrobblingProvider.Bangumi)));
            bangumiRows.Add(Fact("获取 Bangumi Access Token", "在 Bangumi 官方页面创建具有收藏写入权限的个人令牌。",
                "", "打开令牌页面", OpenBangumiTokenPage));
        }
        AddScrobblingRecords(bangumiRows, ScrobblingProvider.Bangumi, bangumi.Enabled);

        var doubanRows = new List<SettingRow>
        {
            Toggle("启用豆瓣自动标记", "实际起播后标记在看；最后一集达到标记已看进度后，标记看过。",
                () => douban.Enabled, value => douban.Enabled = value, after: ScrobblingConfigurationChanged)
        };
        if (douban.Enabled)
        {
            var doubanConnection = _scrobbling?.DoubanConnection ?? ScrobblingConnectionState.Unchecked;
            var sessionActionsAvailable = _doubanSessionAction is null && !_manualScrobblingCheck;
            var doubanStatus = _doubanSessionAction switch
            {
                true => UiText.Translate("正在退出豆瓣…"),
                false => UiText.Translate("正在登录豆瓣…"),
                null => ConnectionStatus(ScrobblingProvider.Douban)
            };
            doubanRows.Add(ScrobblingConnectionRow(ScrobblingProvider.Douban, "豆瓣登录状态", "进入设置和完成登录后自动验证。",
                doubanStatus));
            if (sessionActionsAvailable && doubanConnection == ScrobblingConnectionState.Connected && _doubanLogout is not null)
                doubanRows.Add(Fact("退出豆瓣", "清除 Momoka 内的豆瓣登录状态。", "", "退出登录",
                    () => _ = RunDoubanSessionActionAsync(logout: true)));
            else if (sessionActionsAvailable && doubanConnection is not ScrobblingConnectionState.Connected and not ScrobblingConnectionState.Checking
                && _doubanLogin is not null)
                doubanRows.Add(Fact("登录豆瓣", "使用 Momoka 内的独立登录会话，登录状态只保存在本机。", "", "登录豆瓣",
                    () => _ = RunDoubanSessionActionAsync(logout: false)));
        }
        AddScrobblingRecords(doubanRows, ScrobblingProvider.Douban, douban.Enabled);

        IReadOnlyList<SettingRow> notificationRows = bangumi.Enabled || douban.Enabled
            ? [new SettingToggleRow("打格子失败或跳过时发送系统通知", "自动同步失败或跳过时通过 Windows 通知提醒，可到同步记录查看原因。",
                settings.NotifyOnFailure, value => settings.NotifyOnFailure = value, Save, after: null,
                canWrite: () => ScrobblingActive && ReferenceEquals(settings, Settings.Scrobbling)
                    && (settings.Bangumi.Enabled || settings.Douban.Enabled))]
            : [];
        return
        [
            new(ScrobblingCategory, "Bangumi 打格子", "完成判断沿用播放行为中的标记已看进度；国漫使用国漫进度。", bangumiRows),
            new(ScrobblingCategory, "豆瓣自动标记在看 / 已看", "自动匹配唯一作品；无法确认作品或季集时跳过同步。", doubanRows),
            new(ScrobblingCategory, "同步通知", "", notificationRows)
        ];
    }

    private string ConnectionStatus(ScrobblingProvider provider)
    {
        var state = provider == ScrobblingProvider.Bangumi ? _scrobbling?.BangumiConnection : _scrobbling?.DoubanConnection;
        var message = state switch
        {
            ScrobblingConnectionState.Checking => "正在验证…",
            ScrobblingConnectionState.Connected => provider == ScrobblingProvider.Bangumi ? "密钥有效" : "已登录",
            ScrobblingConnectionState.MissingCredential => "未配置密钥",
            ScrobblingConnectionState.SignedOut => provider == ScrobblingProvider.Bangumi ? "密钥无效或已过期，请重新填写" : "未登录，请登录豆瓣",
            ScrobblingConnectionState.Failed => "验证失败，请检查网络后重试",
            _ => "尚未验证"
        };
        return UiText.Translate(message);
    }

    private SettingFactRow ScrobblingConnectionRow(ScrobblingProvider provider, string label, string note, string status)
    {
        if (ScrobblingCheckBusy) note += "\n当前操作完成后可重新验证。";
        return Fact(label, note, status, "重新验证", () => _ = CheckScrobblingConnectionsAsync(provider));
    }

    private bool ScrobblingCheckBusy => _doubanSessionAction is not null || _manualScrobblingCheck
        || _scrobbling?.BangumiConnection == ScrobblingConnectionState.Checking
        || _scrobbling?.DoubanConnection == ScrobblingConnectionState.Checking;

    private void AddScrobblingRecords(List<SettingRow> rows, ScrobblingProvider provider, bool enabled)
    {
        var latest = _scrobbling?.GetLatestRecord(provider);
        if (!enabled && latest is null) return;
        var label = provider == ScrobblingProvider.Bangumi ? "Bangumi 同步记录" : "豆瓣同步记录";
        var summary = latest is null ? UiText.Translate("尚未同步观看记录") : UiText.Translate(latest.Detail);
        rows.Add(Fact(label, "查看本次运行中的自动匹配和标记结果。", summary, "查看记录",
            () => ShowScrobblingRecords(provider)));
    }

    private bool ScrobblingActive => !_disposed && _active && _settings is not null;

    private bool ScrobblingEnabled(ScrobblingProvider provider) => provider == ScrobblingProvider.Bangumi
        ? Settings.Scrobbling.Bangumi.Enabled
        : Settings.Scrobbling.Douban.Enabled;

    private void ShowScrobblingRecords(ScrobblingProvider provider)
    {
        if (!ScrobblingActive || !ScrobblingEnabled(provider) && _scrobbling?.GetLatestRecord(provider) is null) return;
        _showScrobblingRecords?.Invoke(provider);
    }

    private async Task CheckScrobblingConnectionsAsync(ScrobblingProvider? manualProvider = null)
    {
        if (!ScrobblingActive || _doubanSessionAction is not null || _verifyScrobbling is not { } verify) return;
        if (manualProvider is { } provider)
        {
            if (!ScrobblingEnabled(provider) || ScrobblingCheckBusy) return;
            _manualScrobblingCheck = true;
            RefreshScrobblingRows();
        }
        var service = _scrobbling;
        var token = BeginBackgroundLoad();
        try
        {
            await verify().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ScrobblingActive && IsCurrent(token) && ReferenceEquals(service, _scrobbling))
                Notify(null, UiText.Translate("验证失败，请检查网络后重试"), InfoBarSeverity.Error);
        }
        finally
        {
            if (manualProvider is not null)
            {
                _manualScrobblingCheck = false;
                if (ScrobblingActive && ReferenceEquals(service, _scrobbling)) RefreshScrobblingRows();
            }
        }
    }

    private async Task RunDoubanSessionActionAsync(bool logout)
    {
        var connection = _scrobbling?.DoubanConnection ?? ScrobblingConnectionState.Unchecked;
        var action = logout ? _doubanLogout : _doubanLogin;
        if (!ScrobblingActive || !ScrobblingEnabled(ScrobblingProvider.Douban) || _doubanSessionAction is not null || _manualScrobblingCheck
            || connection == ScrobblingConnectionState.Checking || logout != (connection == ScrobblingConnectionState.Connected)
            || action is null) return;

        var service = _scrobbling;
        var token = BeginBackgroundLoad();
        _doubanSessionAction = logout;
        RefreshScrobblingRows();
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ScrobblingActive && IsCurrent(token) && ReferenceEquals(service, _scrobbling)
                && ScrobblingEnabled(ScrobblingProvider.Douban))
                Notify(null, UiText.Translate("打格子操作未完成，请检查登录状态和配置后重试。"), InfoBarSeverity.Error);
        }
        finally
        {
            _doubanSessionAction = null;
            if (ScrobblingActive && ReferenceEquals(service, _scrobbling)) RefreshScrobblingRows();
        }
    }

    private bool TrySaveScrobblingToken()
    {
        if (_settings is null) return false;
        var saved = _settings.TrySave();
        SyncSaveState();
        return saved;
    }

    private void OpenBangumiTokenPage()
    {
        if (_disposed || !_active || _settings is null || !Settings.Scrobbling.Bangumi.Enabled) return;
        try { _launcher?.OpenUrl("https://next.bgm.tv/demo/access-token"); }
        catch (Exception) { Notify(null, UiText.Translate("无法打开 Bangumi 令牌页面，请稍后重试。"), InfoBarSeverity.Warning); }
    }

    private void ScrobblingConfigurationChanged()
    {
        _scrobbling?.ConfigurationChanged();
        RefreshScrobblingRows();
        _ = CheckScrobblingConnectionsAsync();
    }

    internal void RefreshScrobblingRows()
    {
        if (!ScrobblingActive) return;
        var current = Sections.Where(section => section.Category == ScrobblingCategory).ToArray();
        foreach (var desired in ScrobblingSections()) current.FirstOrDefault(section => section.Title == desired.Title)?.UpdateRows(desired.Rows);
    }
}
