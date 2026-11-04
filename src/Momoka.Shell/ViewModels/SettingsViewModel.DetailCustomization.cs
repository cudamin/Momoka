using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    public List<SettingHomeLayoutRow> DetailCards { get; } = [];

    private SettingSection DetailAppearanceCard()
    {
        var ui = Settings.Ui;
        return new SettingSection("界面", "详情页外观", "调整媒体库详情页的控件底色、背景图和大屏布局。",
        [
            Slider("按钮与选择框底色不透明度（%）", 0, 100, 1,
                () => ui.ControlBackgroundOpacity, value => ui.ControlBackgroundOpacity = value,
                "0 保留现有底色，向右调更不透明，100 完全不透明；详情页按钮、选择框和主页详情按钮一起生效，文字图标不变。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Slider("背景图不透明度（%）", 0, 100, 1,
                () => ui.BackdropOpacity, value => ui.BackdropOpacity = value,
                "0 保留详情页现有背景浓度，向右调更不透明，100 完全不透明。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Toggle("固定使用沉浸模式（大屏模式）",
                "电影、剧和季详情页有背景图时固定使用沉浸布局；窄窗口保留紧凑排版。关闭后随窗口大小自动切换。",
                () => ui.AlwaysImmersive, value => ui.AlwaysImmersive = value,
                after: () => ShellPrefs.ApplyAppearance(ui))
        ]);
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
