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
    private bool _scrobblingActionBusy;
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
            }, () => Settings.Scrobbling.Bangumi.ProtectedAccessToken.Length > 0, TrySaveScrobblingToken, ScrobblingConfigurationChanged);
            bangumiRows.Add(_bangumiSecret);
            bangumiRows.Add(Fact("Bangumi 连接状态", "自动验证已保存的密钥；所有媒体服务器账号的播放都参与同步。",
                ConnectionStatus(ScrobblingProvider.Bangumi), "重新验证", () => _ = _verifyScrobbling?.Invoke()));
            bangumiRows.Add(Fact("获取 Bangumi Access Token", "在 Bangumi 官方页面创建具有收藏写入权限的个人令牌。",
                "", "打开令牌页面", OpenBangumiTokenPage));
            AddScrobblingStatus(bangumiRows);
            bangumiRows.Add(Fact("Bangumi 同步记录", "查看本次运行中的自动匹配和标记结果。", "", "查看记录",
                () => _showScrobblingRecords?.Invoke(ScrobblingProvider.Bangumi)));
        }
        var doubanRows = new List<SettingRow>
        {
            Toggle("启用豆瓣自动标记", "实际起播后标记在看；最后一集达到标记已看进度后，标记看过。",
                () => douban.Enabled, value => douban.Enabled = value, after: ScrobblingConfigurationChanged)
        };
        if (douban.Enabled)
        {
            doubanRows.Add(Fact("豆瓣登录状态", "进入设置和完成登录后自动验证。",
                ConnectionStatus(ScrobblingProvider.Douban), "重新验证", () => _ = _verifyScrobbling?.Invoke()));
            if (_doubanLogin is not null)
                doubanRows.Add(Fact("登录豆瓣", "使用 Momoka 内的独立登录会话，登录状态只保存在本机。", "", "登录豆瓣",
                    () => _ = RunScrobblingActionAsync(ScrobblingProvider.Douban, _doubanLogin)));
            if (_doubanLogout is not null)
                doubanRows.Add(Fact("退出豆瓣", "清除 Momoka 内的豆瓣登录状态。", "", "退出登录",
                    () => _ = RunScrobblingActionAsync(ScrobblingProvider.Douban, _doubanLogout)));
            AddScrobblingStatus(doubanRows);
            doubanRows.Add(Fact("豆瓣同步记录", "查看本次运行中的自动匹配和标记结果。", "", "查看记录",
                () => _showScrobblingRecords?.Invoke(ScrobblingProvider.Douban)));
        }
        return
        [
            new(ScrobblingCategory, "Bangumi 打格子", "完成判断沿用播放行为中的标记已看进度；国漫使用国漫进度。", bangumiRows),
            new(ScrobblingCategory, "豆瓣自动标记在看 / 已看", "自动匹配唯一作品；无法确认作品或季集时跳过同步。", doubanRows)
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

    private void AddScrobblingStatus(List<SettingRow> rows)
    {
        if (_scrobbling is { } service) rows.Add(Fact("自动标记状态", null, UiText.Translate(service.LastStatus)));
    }

    private async Task RunScrobblingActionAsync(ScrobblingProvider provider, Func<Task>? action)
    {
        if (_disposed || !_active || _scrobblingActionBusy || action is null) return;
        if (provider == ScrobblingProvider.Bangumi ? !Settings.Scrobbling.Bangumi.Enabled : !Settings.Scrobbling.Douban.Enabled) return;
        var token = BeginBackgroundLoad();
        _scrobblingActionBusy = true;
        try
        {
            await action().ConfigureAwait(true);
            if (IsCurrent(token) && _active) { RefreshScrobblingRows(); SyncSaveState(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (IsCurrent(token) && _active)
                Notify(null, UiText.Translate("打格子操作未完成，请检查登录状态和配置后重试。"), InfoBarSeverity.Error);
        }
        finally { _scrobblingActionBusy = false; }
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
        _ = _verifyScrobbling?.Invoke();
    }

    internal void RefreshScrobblingRows()
    {
        if (_disposed || !_active || _settings is null) return;
        var current = Sections.Where(section => section.Category == ScrobblingCategory).ToArray();
        foreach (var desired in ScrobblingSections()) current.FirstOrDefault(section => section.Title == desired.Title)?.UpdateRows(desired.Rows);
    }
}
