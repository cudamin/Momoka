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
            AppearanceColorRow("按钮与选择框染色颜色", false, ui),
            Slider("按钮与选择框色调不透明度（%）", 0, 100, 1,
                () => ui.ControlTintOpacity, value => ui.ControlTintOpacity = value,
                "颜色覆盖层的浓度，值越大染色越明显。", after: () => ShellPrefs.ApplyAppearance(ui)),
            Slider("按钮与选择框亮度不透明度（%）", 0, 100, 1,
                () => ui.ControlTintLuminosityOpacity, value => ui.ControlTintLuminosityOpacity = value,
                "调整材质的明暗层，独立于整层透明度。", after: () => ShellPrefs.ApplyAppearance(ui)),
            Slider("按钮与选择框底色不透明度（%）", 0, 100, 0.01,
                () => ui.ControlBackgroundOpacity, value => ui.ControlBackgroundOpacity = value,
                "默认约5.88%，使用固定亚克力。0常态完全透明，100完全不透明；悬停与按下保留反馈。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Choice("显示模式", new[] { ("沉浸模式", true), ("简洁模式", false) },
                () => ui.EnableImmersive,
                value => ui.EnableImmersive = value,
                "电影、剧、季和单集详情页有背景图时固定使用沉浸布局",
                after: () =>
                {
                    ShellPrefs.ApplyAppearance(ui);
                    RefreshDetailAppearance();
                })
        };
        if (ui.EnableImmersive)
        {
            rows.Add(Choice("背景染色来源", new[] { ("当前主图", true), ("自定义颜色", false) },
                () => ui.BackdropTintFromArtwork, value => ui.BackdropTintFromArtwork = value,
                "默认跟随当前主图底色；也可指定染色颜色，省电模式自动使用回退色。",
                after: () => { ShellPrefs.ApplyAppearance(ui); RefreshDetailAppearance(); }));
            if (!ui.BackdropTintFromArtwork) rows.Add(AppearanceColorRow("背景染色颜色", true, ui));
            rows.AddRange(
            [
                Slider("背景色调不透明度（%）", 0, 100, 1,
                    () => ui.BackdropTintOpacity, value => ui.BackdropTintOpacity = value,
                    "颜色覆盖层的浓度，值越大染色越明显。", after: () => ShellPrefs.ApplyAppearance(ui)),
                Slider("背景亮度不透明度（%）", 0, 100, 1,
                    () => ui.BackdropTintLuminosityOpacity, value => ui.BackdropTintLuminosityOpacity = value,
                    "调整背景材质的明暗层，独立于整层透明度。", after: () => ShellPrefs.ApplyAppearance(ui)),
                Slider("背景图不透明度（%）", 0, 100, 1,
                    () => ui.BackdropOpacity, value => ui.BackdropOpacity = value,
                    "默认20%。0完全透明，100完全不透明；省电模式由系统自动回退。",
                    after: () => ShellPrefs.ApplyAppearance(ui))
            ]);
        }
        return new SettingSection("界面", "详情页外观", "按钮与选择框设置全局生效；背景与显示模式用于媒体详情页。", rows);
    }

    private void RefreshDetailAppearance()
    {
        var section = Sections.FirstOrDefault(card => card.Title == "详情页外观");
        if (section is null) return;
        foreach (var color in section.Rows.OfType<SettingColorRow>()) color.CancelEdit();
        section.UpdateRows(DetailAppearanceCard().Rows);
    }

    private SettingColorRow AppearanceColorRow(string label, bool backdrop, UiSettings ui)
    {
        var row = new SettingColorRow(label, "拾色器、预设或六位 HEX；完成保存，取消或点击外部恢复原色。",
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
