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
            Choice("按钮与选择框背景", new[] { ("现有底色", false), ("亚克力", true) },
                () => ui.UseAcrylicControls, value => ui.UseAcrylicControls = value,
                "详情页按钮、选择框和主页详情按钮一起切换，文字图标保持清晰。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Slider("按钮与选择框底色不透明度（%）", 0, 100, 1,
                () => ui.ControlBackgroundOpacity, value => ui.ControlBackgroundOpacity = value,
                "选择框和主页详情按钮：0 完全透明，100 完全不透明。详情操作按钮保留浅色底；选择亚克力时可调材质浓度。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Choice("背景图效果", new[] { ("暗色遮罩", 0), ("高斯模糊", 1), ("亚克力", 2) },
                () => ui.UseAcrylicBackdrop ? 2 : ui.UseGaussianBackdrop ? 1 : 0,
                value =>
                {
                    ui.UseAcrylicBackdrop = value == 2;
                    ui.UseGaussianBackdrop = value == 1;
                },
                "电影、剧和季可用亚克力，普通与窄窗同样生效；高斯仅沉浸模式生效并保留深色阅读保护。主页轮播不受影响。",
                after: () => ShellPrefs.ApplyAppearance(ui)),
            Slider("背景图不透明度（%）", 0, 100, 1,
                () => ui.BackdropOpacity, value => ui.BackdropOpacity = value,
                "0 关闭背景处理；100 暗色遮罩和亚克力为实底，高斯最糊。高斯非零档保留深色阅读保护。",
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
