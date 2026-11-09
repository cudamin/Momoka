using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    public List<SettingHomeLayoutRow> DetailCards { get; } = [];

    private SettingSection DetailAppearanceCard()
    {
        var ui = Settings.Ui;
        var rows = new List<SettingRow>
        {
            Choice("按钮与选择框背景", new[] { ("半透明叠层", false), ("亚克力", true) },
                () => ui.UseAcrylicControls, value => ui.UseAcrylicControls = value,
                "详情按钮与闭合选择框共用同一份背景；选亚克力时由下面三项染色参数决定材质。",
                after: () =>
                {
                    ShellPrefs.ApplyAppearance(ui);
                    // 两套参数各自属于一种材质：换材质时把不适用的行整组收起来（2026-11-07 用户令三项可调）。
                    RefreshDetailAppearance();
                }),
            AppearanceColorRow("按钮与选择框颜色", false, ui,
                ui.UseAcrylicControls
                    ? "亚克力的染色颜色（TintColor）。"
                    : "半透明叠层那层底色的 RGB；浓度在下一行。")
        };
        if (ui.UseAcrylicControls)
        {
            rows.AddRange(
            [
                Slider("按钮与选择框染色不透明度（%）", 0, 100, 1,
                    () => ui.ControlTintOpacity, value => ui.ControlTintOpacity = value,
                    "亚克力的 TintOpacity：染色层压得多浓，越高颜色盖得越实。",
                    after: () => ShellPrefs.ApplyAppearance(ui)),
                Slider("按钮与选择框明暗不透明度（%）", 0, 100, 1,
                    () => ui.ControlTintLuminosityOpacity, value => ui.ControlTintLuminosityOpacity = value,
                    "亚克力的 TintLuminosityOpacity：背后画面的明暗透上来多少，与染色层各管一头。",
                    after: () => ShellPrefs.ApplyAppearance(ui))
            ]);
        }
        else
        {
            rows.Add(Slider("按钮与选择框底色不透明度（%）", 0, 100, 0.01,
                () => ui.ControlBackgroundOpacity, value => ui.ControlBackgroundOpacity = value,
                "出厂 39.25%（那层中灰底的浓度）。0 常态完全透明，100 完全不透明；悬停与按下保留反馈。",
                after: () => ShellPrefs.ApplyAppearance(ui)));
        }
        rows.Add(Choice("显示模式", new[] { ("沉浸模式", true), ("简洁模式", false) },
            () => ui.EnableImmersive,
            value => ui.EnableImmersive = value,
            "电影、剧、季、单集与 MoviePilot 订阅详情页有背景图时使用沉浸布局；关掉改用不铺图的简洁卡面",
            after: () =>
            {
                ShellPrefs.ApplyAppearance(ui);
                RefreshDetailAppearance();
            }));
        if (ui.EnableImmersive)
        {
            rows.AddRange(
            [
                AppearanceColorRow("背景亚克力色调", true, ui,
                    "背景图那层亚克力的染色颜色（TintColor）；换图不跟着变。"),
                Slider("背景染色不透明度（%）", 0, 100, 1,
                    () => ui.BackdropTintOpacity, value => ui.BackdropTintOpacity = value,
                    "背景亚克力的 TintOpacity：染色层压得多浓。",
                    after: () => ShellPrefs.ApplyAppearance(ui)),
                Slider("背景明暗不透明度（%）", 0, 100, 1,
                    () => ui.BackdropTintLuminosityOpacity, value => ui.BackdropTintLuminosityOpacity = value,
                    "背景亚克力的 TintLuminosityOpacity：图上的明暗透上来多少。",
                    after: () => ShellPrefs.ApplyAppearance(ui))
            ]);
        }
        return new SettingSection("界面", "详情页外观", "调整媒体库详情页的按钮材质与背景图亚克力。", rows);
    }

    private void RefreshDetailAppearance()
    {
        var section = Sections.FirstOrDefault(card => card.Title == "详情页外观");
        if (section is null) return;
        foreach (var color in section.Rows.OfType<SettingColorRow>()) color.CancelEdit();
        section.UpdateRows(DetailAppearanceCard().Rows);
    }

    private SettingColorRow AppearanceColorRow(string label, bool backdrop, UiSettings ui, string? note = null)
    {
        var row = new SettingColorRow(label,
            note ?? "拾色器、预设或六位 HEX；完成保存，取消或点击外部恢复原色。",
            backdrop ? ui.BackdropColor : ui.ControlBackgroundColor,
            value =>
            {
                if (backdrop) ui.BackdropColor = value;
                else ui.ControlBackgroundColor = value;
            }, Save);
        row.ConfigureConfirmation(backdrop ? UiSettings.DefaultBackdropColor : UiSettings.DefaultControlBackgroundColor,
            value => ShellPrefs.SetAppearanceColorPreview(backdrop, value, ui),
            () => ShellPrefs.SetAppearanceColorPreview(backdrop, null, ui));
        return row;
    }

    private IReadOnlyList<SettingSection> DetailLayoutCards()
    {
        var ui = Settings.Ui;
        DetailCards.Clear();
        return
        [
            Card("剧页面卡片", EmbyItemType.Series, value => ui.SeriesCards = value),
            Card("单集页面卡片", EmbyItemType.Episode, value => ui.EpisodeCards = value),
            Card("电影页面卡片", EmbyItemType.Movie, value => ui.MovieCards = value)
        ];

        SettingSection Card(string title, string type, Action<List<DetailCardSetting>> write)
        {
            SettingHomeLayoutRow? row = null;
            row = new SettingHomeLayoutRow(title, "拖拽调整顺序，取消勾选隐藏；没有内容的卡片自动收起。",
                DetailLayout.For(ui, type).Select(card => new HomeRowChoice(card.Key, DetailLayout.Title(card.Key), card.Visible)),
                choices =>
                {
                    if (row is null || !DetailCards.Contains(row)) return;
                    write(DetailLayout.Plan(type, choices.Select(choice =>
                        new DetailCardSetting { Key = choice.Key, Visible = choice.Visible })).ToList());
                    Save();
                    ShellPrefs.ApplyDetailLayout(ui);
                }, enableSpring: true);
            DetailCards.Add(row);
            return new SettingSection("界面", title, "顺序与隐藏状态分别保存，修改后立即生效。", [row]);
        }
    }
}
