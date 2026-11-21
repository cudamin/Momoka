using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Playback;
using Momoka.Shell.Localization;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    // 全部可用行与搜索结果分开保管；过滤不能解绑、丢失行回调，或让恢复默认只作用于屏上的几行。
    private readonly List<SettingShortcutRow> _shortcutRows = [];
    private SettingFactRow? _shortcutResetRow;
    private readonly SettingFactRow _shortcutEmptyRow = Fact("没有找到匹配的快捷键", "试试操作名称或按键，如「字幕」「Ctrl + ↑」；清空搜索可查看全部。", "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClearShortcutSearchVisibility))]
    public partial string ShortcutSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string ShortcutSearchSummary { get; set; } = "";

    public Visibility ShortcutSearchVisibility => Show(SelectedCategory == "快捷键");
    public Visibility ClearShortcutSearchVisibility => Show(ShortcutSearchQuery.Length > 0);

    partial void OnShortcutSearchQueryChanged(string value) => RefreshShortcutFilter();

    [RelayCommand]
    private void ClearShortcutSearch() => ShortcutSearchQuery = "";

    // 弹幕那四条的显隐判据在 Core（自检也读同一份），别在这里另写一遍 —— 两处各写一份时，
    // 自检那条「应当几行」就会和卡片各说各话。
    private IEnumerable<ShortcutAction> AvailableShortcutActions() => ShortcutCatalog.Available(DanmakuReady);

    private SettingShortcutRow ShortcutRow(ShortcutAction action, KeyStroke stroke) => new(action.Label, action.Id,
        ShortcutCatalog.Format(stroke), token => ApplyShortcut(action.Id, token));

    private SettingSection ShortcutsCard()
    {
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        _shortcutRows.Clear();
        _shortcutRows.AddRange(AvailableShortcutActions().Select(action => ShortcutRow(action, effective[action.Id])));
        _shortcutResetRow = Fact("恢复默认快捷键", "恢复全部快捷键，包括当前搜索未显示的项目；其他设置不受影响",
            UiText.Format("{0} 个可改快捷键", _shortcutRows.Count), "恢复默认", ClearAllShortcuts);
        return new SettingSection("快捷键", "快捷键",
            "播放时的键盘快捷键。点右边方框后按键即可改绑，可带 Ctrl / Alt / Shift；冲突时可确认替换。× 清除。只在播放窗口生效。",
            ShortcutFilteredRows(effective));
    }

    private IReadOnlyList<SettingRow> ShortcutFilteredRows(IReadOnlyDictionary<string, KeyStroke> effective)
    {
        var rows = _shortcutRows.Where(row => ShortcutCatalog.MatchesSearch(ShortcutSearchQuery, row.Label,
            effective[row.Id]) || ShortcutCatalog.MatchesSearch(ShortcutSearchQuery, row.DisplayLabel, effective[row.Id]))
            .Cast<SettingRow>().ToList();
        var matched = rows.Count;
        var searching = !string.IsNullOrWhiteSpace(ShortcutSearchQuery);
        ShortcutSearchSummary = searching
            ? UiText.Format("找到 {0} 项 · 共 {1} 个可改快捷键", matched, _shortcutRows.Count)
            : UiText.Format("{0} 个可改快捷键 · 支持按名称或当前按键搜索", _shortcutRows.Count);
        if (searching && matched == 0) rows.Add(_shortcutEmptyRow);
        if (_shortcutResetRow is not null) rows.Add(_shortcutResetRow);
        return rows;
    }

    private void RefreshShortcutFilter()
    {
        if (_settings is null) return;
        var card = Sections.FirstOrDefault(section => section.Category == "快捷键");
        card?.UpdateRows(ShortcutFilteredRows(ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings)));
    }

    /// <summary>服务开关变化时保留仍可用的行实例和搜索词；被隐藏的弹幕绑定仍保存在偏好中。</summary>
    private void RefreshShortcutAvailability()
    {
        if (_settings is null || _shortcutResetRow is null) return;
        var previous = _shortcutRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        _shortcutRows.Clear();
        foreach (var action in AvailableShortcutActions())
            _shortcutRows.Add(previous.GetValueOrDefault(action.Id) ?? ShortcutRow(action, effective[action.Id]));
        _shortcutResetRow.Value = UiText.Format("{0} 个可改快捷键", _shortcutRows.Count);
        RefreshShortcutRows();
    }

    /// <summary>
    /// 一次重绑（方框敲定了一个键 token，空串=按了 ×）。判断在 <see cref="ShortcutCatalog"/>：冲突就拦下、提示，
    /// 一个绑定都不改（那一行方框还显示着原来的，因为 <c>ComboText</c> 没动）；否则写回、落盘、逐行刷新显示。
    /// 写回一份新字典即可，播放器那头每次按键现读同一个单例设置，所以已经开着的播放器也当场跟上。
    /// </summary>
    private void ApplyShortcut(string id, string token) => _ = ApplyShortcutAsync(id, token);

    private async Task ApplyShortcutAsync(string id, string token)
    {
        var bindings = Settings.Shortcuts.Bindings;

        if (token.Length == 0)
        {
            Settings.Shortcuts.Bindings = new Dictionary<string, string>(ShortcutCatalog.Clear(bindings, id), StringComparer.Ordinal);
        }
        else
        {
            if (!ShortcutCatalog.TryParse(token, out var stroke)) return;

            var result = ShortcutCatalog.Rebind(bindings, id, stroke);
            if (result.Conflict is { } occupant)
            {
                var beforeConfirmation = ShortcutCatalog.Resolve(bindings);
                var previous = beforeConfirmation[id];
                var occupied = beforeConfirmation[occupant];
                var replacement = await ConfirmAsync(UiText.Translate("替换快捷键"),
                    UiText.Format("「{0}」已绑定到「{1}」。确认后，新动作会使用这个键；",
                        ShortcutCatalog.Format(stroke), UiText.Translate(ShortcutCatalog.Label(occupant)))
                    + (previous.IsEmpty
                        ? UiText.Format("「{0}」将解绑。", UiText.Translate(ShortcutCatalog.Label(occupant)))
                        : UiText.Format("「{0}」将改用「{1}」。", UiText.Translate(ShortcutCatalog.Label(occupant)), ShortcutCatalog.Format(previous))),
                    UiText.Translate("替换"));
                if (!replacement) return;

                // 等待确认期间设置可能已改变；若冲突对象变了，保留当前绑定并让用户重新录制确认。
                var currentBindings = Settings.Shortcuts.Bindings;
                var currentEffective = ShortcutCatalog.Resolve(currentBindings);
                var current = ShortcutCatalog.Rebind(currentBindings, id, stroke);
                if (current.Conflict != occupant || currentEffective[id] != previous || currentEffective[occupant] != occupied)
                {
                    Notify(UiText.Translate("快捷键已变化"), UiText.Translate("绑定状态已更新，请重新录制后再确认替换。"), InfoBarSeverity.Warning);
                    return;
                }
                var replaced = ShortcutCatalog.Rebind(Settings.Shortcuts.Bindings, id, stroke, replaceConflict: true);
                Settings.Shortcuts.Bindings = new Dictionary<string, string>(replaced.Bindings, StringComparer.Ordinal);
            }
            else
            {
                Settings.Shortcuts.Bindings = new Dictionary<string, string>(result.Bindings, StringComparer.Ordinal);
            }
        }

        Save();
        RefreshShortcutRows();
    }

    /// <summary>「恢复默认快捷键」：改动全清掉（Bindings 变回空＝全默认），落盘、刷新、说一声。</summary>
    private void ClearAllShortcuts()
    {
        Settings.Shortcuts.Bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        Save();
        RefreshShortcutRows();
        Notify(null, UiText.Translate(HasUnsavedChanges ? "快捷键已在本次运行恢复默认，但尚未保存。" : "播放器快捷键已恢复默认。其他设置没有动。"),
            HasUnsavedChanges ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    /// <summary>照当前绑定把每一行的显示串重算一遍（重绑、清一个、清全部之后都走它）。</summary>
    private void RefreshShortcutRows()
    {
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        foreach (var row in _shortcutRows)
            row.ComboText = ShortcutCatalog.Format(effective[row.Id]);
        RefreshShortcutFilter();
        ShellPrefs.ApplyShortcuts();
    }

}
