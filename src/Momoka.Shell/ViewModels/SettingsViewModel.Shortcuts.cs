using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Playback;
using Momoka.Shell.Localization;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    // 全部可用行与搜索结果分开保管；过滤不能解绑、丢失行回调，或让恢复默认只作用于屏上的几行。
    private readonly List<SettingShortcutRow> _shortcutRows = [];
    // 四颗方向键的步长挂在各自的快捷键行下面（用户令 2026-11-24「上图的设置移到快捷键页面对应快捷键下方」）。
    // 它们不是快捷键，不参与改绑，也不计入「可改快捷键」；值仍住 PlaybackSettings，读写与保存和从前一样。
    private readonly Dictionary<string, List<SettingNumberRow>> _shortcutSeekRows = new(StringComparer.Ordinal);
    private static readonly string[] ShortcutTailIds =
        [ShortcutCatalog.ExitPlayback, ShortcutCatalog.DismissSkip, ShortcutCatalog.TakeSkip];
    private SettingFactRow? _shortcutResetRow;
    private readonly SettingFactRow _shortcutEmptyRow = Fact("没有找到匹配的快捷键", "试试操作名称或按键，如「字幕」「Ctrl + ↑」；清空搜索可查看全部。", "");

    /// <summary>快捷键页建出来的每一张分区卡与它负责的动作 Id；搜索时逐张重算行表。</summary>
    private readonly List<ShortcutSectionPlan> _shortcutSectionPlans = [];

    private sealed record ShortcutSectionPlan(SettingSection Section, IReadOnlyList<string> Ids, bool Tail);

    /// <summary>
    /// 快捷键页的分区：标题、一句说明、这一区从上到下的动作 Id。
    /// <para>
    /// 2026-11-24 用户令「给设置中的快捷键页面分区」：一页一百六十多行挤在一张卡里，找一颗键除了搜索没有别的路。
    /// 现在按「这颗键在回答哪个问题」分成十三张卡，卡片次序就是阅读次序。Id 与 <see cref="ShortcutCatalog.Actions"/>
    /// 一一对应；没列进任何一组的（以后 <c>ShortcutCatalog</c> 新加的）落进末尾「其他」，不会从页面上消失。
    /// </para>
    /// <para>
    /// 同一组内按这里列出的次序排，不跟 <c>ShortcutCatalog.Actions</c> 的追加次序 —— 动作是逐年加进来的，目录次序
    /// 只保证「旧的不动」；页面上把一对对相关的键放一起更好找（快退／快进、大步、精细各一对）。
    /// </para>
    /// </summary>
    private static readonly (string Title, string Description, string[] Ids)[] ShortcutGroups =
    [
        ("播放与进度",
            "播放、暂停与进度跳转。点右边方框后按键即可改绑，可带 Ctrl / Alt / Shift；冲突时可确认替换，× 清除。"
                + "四颗方向键各有一行跳转跨度，快退、快进另有长按倍率／倍速行。集成与独占模式共用这些数值。",
            [
                "toggle-pause",
                "seek-backward", "seek-forward", "seek-backward-long", "seek-forward-long",
                "seek-backward-fine", "seek-forward-fine",
                "frame-forward", "frame-backward",
                "seek-start", "revert-seek", "skip-to-silence"
            ]),

        ("播放控制",
            "音量、静音、全屏、置顶、窗口不在前台时的画面单击与播放信息。音量键只亮音量条，不叫出整套播放控件。",
            [
                "volume-up", "volume-down", "toggle-mute",
                "toggle-fullscreen", "toggle-pin", "toggle-background-click",
                "show-playback-info", "show-stats", "toggle-stats",
                "osd-timeline", "osd-position"
            ]),

        ("集数与章节",
            "上一集／下一集、章节跳转，以及在当前片子里标记、改名、删除和导出章节。",
            [
                "previous-episode", "next-episode", "chapter-previous", "chapter-next",
                "chapter-mark", "chapter-rename", "chapter-remove",
                "chapter-export-chp", "chapter-export-ogm"
            ]),

        ("速度与循环",
            "调速、恢复常速与 A-B 片段循环；倍速和循环只作用于这一场播放，不写进片子的进度。",
            [
                "speed-down", "speed-up", "speed-reset",
                "speed-half", "speed-one-and-half", "speed-double",
                "toggle-ab-loop", "clear-ab-loop", "toggle-loop"
            ]),

        ("字幕",
            "主字幕的开关、挑轨、延迟、缩放、位置与外观。改完立刻作用到正在播的这一场。",
            [
                "toggle-subtitle", "subtitle-next", "subtitle-previous",
                "subtitle-delay-decrease", "subtitle-delay-increase", "subtitle-delay-reset",
                "subtitle-seek-previous", "subtitle-seek-next",
                "subtitle-scale-down", "subtitle-scale-up",
                "subtitle-position-up", "subtitle-position-down",
                "subtitle-render-style", "subtitle-timing-fix", "subtitle-reload",
                "subtitle-font", "subtitle-reset"
            ]),

        ("次字幕与兼容性",
            "第二字幕轨，以及图形字幕黑边、灰度与渲染兼容开关；这些多半只在当前这一场有效，换片按逐片状态复位。",
            [
                "secondary-subtitle-next", "secondary-subtitle-toggle",
                "secondary-subtitle-position-up", "secondary-subtitle-position-down",
                "secondary-subtitle-delay-decrease", "secondary-subtitle-delay-increase", "secondary-subtitle-delay-reset",
                "subtitle-secondary-style",
                "subtitle-ass-margins", "subtitle-srt-margins", "subtitle-pgs-margins", "subtitle-pgs-gray",
                "subtitle-blend", "subtitle-color-compat", "subtitle-video-data",
                "subtitle-bidi", "subtitle-border-scale"
            ]),

        ("音轨与音频",
            "挑轨、音频延迟与输出处理：声道、均衡、滤镜、独占和精确跳转时丢帧。",
            [
                "audio-next", "audio-previous",
                "audio-delay-decrease", "audio-delay-increase", "audio-delay-reset",
                "audio-channels-cycle", "audio-normalization", "audio-clear-filters",
                "audio-downmix", "audio-exclusive", "audio-hr-seek-drop"
            ]),

        ("画面与缩放",
            "宽高比、缩放、平移、旋转、裁切与去黑边检测；画面操作都能一键归零。",
            [
                "video-aspect-cycle",
                "video-zoom-out", "video-zoom-in", "video-zoom-reset",
                "video-pan-left", "video-pan-right", "video-pan-up", "video-pan-down", "video-pan-reset",
                "video-rotate-left", "video-rotate-right",
                "video-crop", "video-reset", "video-detect-crop"
            ]),

        ("画质与色彩",
            "调色、去色带、HDR 与色彩管理；数值微调一下一个台阶，开关即刻作用在当前画面上。",
            [
                "contrast-decrease", "contrast-increase",
                "brightness-decrease", "brightness-increase",
                "gamma-decrease", "gamma-increase",
                "saturation-decrease", "saturation-increase",
                "hue-decrease", "hue-increase",
                "color-reset",
                "deband-toggle", "deband-increase", "deband-decrease",
                "hdr-tone-map", "hdr-peak", "hdr-output", "hdr-sdr",
                "hdr-trc", "hdr-white", "hdr-gamut", "hdr-visualize",
                "video-icc", "video-nonlinear"
            ]),

        ("视频与滤镜",
            "视频轨、插值同步与视频滤镜。滤镜叠在解码之后，逐项开关，效果即时可见。",
            [
                "video-next",
                "video-interpolation", "video-interpolation-off",
                "video-deinterlace", "video-flip", "video-sync-cycle",
                "vf-clear", "vf-deblock", "vf-range-limit",
                "vf-flip-vertical", "vf-flip-horizontal", "vf-rotate-180",
                "vf-gamma", "vf-fps", "vf-pad", "vf-temperature"
            ]),

        ("截图",
            "当前画面截图与逐帧截图。",
            [
                "screenshot", "screenshot-original", "screenshot-window", "screenshot-subtitles",
                "screenshot-each-frame-subtitles", "screenshot-each-frame-video", "screenshot-each-frame-window"
            ]),

        ("弹幕",
            "弹幕开关与时间偏移。服务关着或这个后端画不出弹幕时整张收起，绑定仍保留。",
            ["toggle-danmaku", "danmaku-delay-decrease", "danmaku-delay-increase", "danmaku-delay-reset"])
    ];

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

    /// <summary>
    /// 快捷键页的全部分区卡。2026-11-24 之前这一页只有一张「快捷键」卡；用户令分区后按 <see cref="ShortcutGroups"/>
    /// 建卡，末尾再跟一张「退出、跳过与恢复默认」。
    /// </summary>
    private IReadOnlyList<SettingSection> ShortcutSections()
    {
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        _shortcutRows.Clear();
        _shortcutRows.AddRange(AvailableShortcutActions().Select(action => ShortcutRow(action, effective[action.Id])));
        BuildShortcutSeekRows();
        _shortcutResetRow = Fact("恢复默认快捷键", "恢复全部快捷键，包括当前搜索未显示的项目；其他设置不受影响",
            UiText.Format("{0} 个可改快捷键", _shortcutRows.Count), "恢复默认", ClearAllShortcuts);

        _shortcutSectionPlans.Clear();
        var sections = new List<SettingSection>();
        var covered = new HashSet<string>(ShortcutTailIds, StringComparer.Ordinal);
        foreach (var group in ShortcutGroups)
        {
            covered.UnionWith(group.Ids);
            var section = new SettingSection("快捷键", group.Title, group.Description, ShortcutRowsFor(group.Ids, effective));
            _shortcutSectionPlans.Add(new ShortcutSectionPlan(section, group.Ids, Tail: false));
            sections.Add(section);
        }

        // 兜底：目录里新加、还没归组的动作仍要看得见；空组由 SettingSection 自己折起来，不留空卡。
        var ungrouped = _shortcutRows.Select(row => row.Id).Where(id => !covered.Contains(id)).ToArray();
        if (ungrouped.Length > 0)
        {
            var section = new SettingSection("快捷键", "其他", "还没有归入上面各分区的动作；新加进来的键先落在这里。",
                ShortcutRowsFor(ungrouped, effective));
            _shortcutSectionPlans.Add(new ShortcutSectionPlan(section, ungrouped, Tail: false));
            sections.Add(section);
        }

        var tailActions = ShortcutRowsFor(ShortcutTailIds, effective);
        var tail = new SettingSection("快捷键", "退出、跳过与恢复默认",
            "退出键在全屏时退出全屏，非全屏时停止播放。关闭、确认跳过提示的按键可与其他快捷键重复；提示出现时优先处理提示，消失后恢复其他动作。关闭与确认两者仍需使用不同按键。",
            ShortcutTailRows(sections.Sum(section => section.Rows.Count) + tailActions.Count, tailActions));
        _shortcutSectionPlans.Add(new ShortcutSectionPlan(tail, ShortcutTailIds, Tail: true));
        sections.Add(tail);
        RefreshShortcutFilter();
        return sections;
    }

    /// <summary>
    /// 快捷键行下面的附注行。2026-11-24 之前只有四行「跨度（秒）」；用户先令在快进下面加「快进跨度（倍）」，
    /// 随后「快退也要」，所以 <c>seek-backward</c> 与 <c>seek-forward</c> 下面现在各挂两行：先跳秒、
    /// 后长按倍率（← 管回退速率、→ 管播放倍速，两格各自保存）。大步与精细那两颗不参与长按，仍只有一行。
    /// </summary>
    private void BuildShortcutSeekRows()
    {
        _shortcutSeekRows.Clear();
        var playback = Settings.Playback;
        Subordinate("seek-backward", "快退跨度（秒）", "短按「快退」快捷键时跳转的秒数（默认 ←）",
            1, 600, () => playback.SeekBackwardSeconds, value => playback.SeekBackwardSeconds = value);
        Subordinate("seek-backward", "长按快退倍率", "按住「快退」快捷键时，每 200ms 连续回退「倍率 × 200ms」进度，不改变播放倍速",
            PlaybackSettings.MinimumSeekHoldMultiplier, PlaybackSettings.MaximumSeekHoldMultiplier,
            () => playback.SeekBackwardHoldMultiplier, value => playback.SeekBackwardHoldMultiplier = value);
        Subordinate("seek-forward", "快进跨度（秒）", "短按「快进」快捷键时跳转的秒数（默认 →）",
            1, 600, () => playback.SeekForwardSeconds, value => playback.SeekForwardSeconds = value);
        Subordinate("seek-forward", "长按快进倍速", "按住「快进」快捷键时，以该倍速连续快进",
            PlaybackSettings.MinimumSeekHoldMultiplier, PlaybackSettings.MaximumSeekHoldMultiplier,
            () => playback.SeekForwardHoldMultiplier, value => playback.SeekForwardHoldMultiplier = value);
        Subordinate("seek-backward-long", "大步快退跨度（秒）", "按下「快退（大步）」快捷键时跳转的秒数（默认 ↓）",
            1, 600, () => playback.SeekBackwardLongSeconds, value => playback.SeekBackwardLongSeconds = value);
        Subordinate("seek-forward-long", "大步快进跨度（秒）", "按下「快进（大步）」快捷键时跳转的秒数（默认 ↑）",
            1, 600, () => playback.SeekForwardLongSeconds, value => playback.SeekForwardLongSeconds = value);

        void Subordinate(string id, string label, string note, int minimum, int maximum,
            Func<int> read, Action<int> write)
        {
            var row = new SettingNumberRow(label, note, minimum, maximum, read(),
                value => write((int)value), () => read(), Save)
            { IsSubordinate = true };
            _shortcutSeekRows.TryAdd(id, []);
            _shortcutSeekRows[id].Add(row);
        }
    }

    /// <summary>
    /// 一组的行。搜索时空查询不过滤；命中这一行的动作或它下面任一附注都算命中 —— 附注藏在快捷键行底下，
    /// 只显示附注而没有上面那颗键就等于没说「这是谁在跳」。
    /// </summary>
    private IReadOnlyList<SettingRow> ShortcutRowsFor(IReadOnlyList<string> ids, IReadOnlyDictionary<string, KeyStroke> effective)
    {
        var searching = !string.IsNullOrWhiteSpace(ShortcutSearchQuery);
        var rows = new List<SettingRow>(ids.Count);
        foreach (var id in ids)
        {
            var row = _shortcutRows.FirstOrDefault(candidate => candidate.Id == id);
            if (row is null) continue;

            var spans = _shortcutSeekRows.GetValueOrDefault(id);
            var matches = !searching
                || ShortcutCatalog.MatchesSearch(ShortcutSearchQuery, row.Label, effective[id])
                || (spans is not null && spans.Any(span => ShortcutCatalog.MatchesText(ShortcutSearchQuery, $"{span.Label} {span.Note}")));
            if (!matches) continue;

            rows.Add(row);
            if (spans is not null) foreach (var span in spans) rows.Add(span);
        }
        return rows;
    }

    /// <summary>最后一张卡：退出和跳过提示的可改按键、空结果说明与恢复默认入口。</summary>
    private IReadOnlyList<SettingRow> ShortcutTailRows(int actionMatches, IReadOnlyList<SettingRow> actions)
    {
        var searching = !string.IsNullOrWhiteSpace(ShortcutSearchQuery);
        var rows = new List<SettingRow>(actions);
        if (searching && actionMatches == 0) rows.Add(_shortcutEmptyRow);
        if (_shortcutResetRow is not null) rows.Add(_shortcutResetRow);
        return rows;
    }

    private void RefreshShortcutFilter()
    {
        if (_settings is null || _shortcutSectionPlans.Count == 0) return;
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        var matched = 0;
        foreach (var plan in _shortcutSectionPlans)
        {
            if (plan.Tail) continue;
            var rows = ShortcutRowsFor(plan.Ids, effective);
            plan.Section.UpdateRows(rows);
            matched += rows.Count;
        }

        var searching = !string.IsNullOrWhiteSpace(ShortcutSearchQuery);
        var tail = _shortcutSectionPlans.First(plan => plan.Tail);
        var tailActions = ShortcutRowsFor(tail.Ids, effective);
        matched += tailActions.Count;
        tail.Section.UpdateRows(ShortcutTailRows(matched, tailActions));

        ShortcutSearchSummary = searching
            ? UiText.Format("找到 {0} 项 · 共 {1} 个可改快捷键", matched, _shortcutRows.Count)
            : UiText.Format("{0} 个可改快捷键 · 支持按名称或当前按键搜索", _shortcutRows.Count);
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
