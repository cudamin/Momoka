using Momoka.Configuration;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    /// <summary>主页媒体库入口的显示预设，不修改服务器封面。关掉总开关时隐藏选择行并保留偏好。</summary>
    private SettingSection HomeLibraryCoverCard()
    {
        var ui = Settings.Ui;
        var rows = new List<SettingRow>
        {
            Toggle("覆盖媒体库封面",
                "在 Momoka 主页的媒体库那一排使用内置封面风格；关闭后恢复原封面，不修改服务器图片。",
                () => ui.HomeLibraryCoverOverride,
                value => ui.HomeLibraryCoverOverride = value,
                after: () =>
                {
                    RefreshHomeLibraryCoverRows();
                    ShellPrefs.NotifyHomeLibraryCover(ui);
                })
        };

        if (ui.HomeLibraryCoverOverride)
        {
            rows.Add(Choice("封面预设",
                new[]
                {
                    ("叠卡", HomeLibraryCoverPresets.Stack),
                    ("斜切大图", HomeLibraryCoverPresets.Diagonal),
                    ("九宫格海报墙", HomeLibraryCoverPresets.Wall),
                    ("纯文字渐变", HomeLibraryCoverPresets.Gradient)
                },
                () => HomeLibraryCoverPresets.Normalize(ui.HomeLibraryCoverPreset),
                value => ui.HomeLibraryCoverPreset = value,
                "全部媒体库入口使用同一套风格，切换后立即生效。",
                after: () => ShellPrefs.NotifyHomeLibraryCover(ui)));
        }

        return new SettingSection("主页", "媒体库封面", "仅替换主页媒体库那一排的封面显示。", rows);
    }

    private void RefreshHomeLibraryCoverRows()
    {
        if (_settings is null) return;
        Sections.FirstOrDefault(section => section.Category == "主页" && section.Title == "媒体库封面")
            ?.UpdateRows(HomeLibraryCoverCard().Rows);
    }
}
