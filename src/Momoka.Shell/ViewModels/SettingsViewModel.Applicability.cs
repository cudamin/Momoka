using Momoka.Mpv;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    /// <summary>
    /// 复用视频卡的目录与说明，只在首次进入行集合前给控制入口补上 after。
    /// 局部适用性不是新的播放规则；不改下发语义，也不把当前片源当全局偏好的隐藏条件。
    /// </summary>
    private SettingSection ApplicableShaderCard()
    {
        var section = ShaderCard();
        var toggle = (SettingToggleRow)section.Rows.Single(row => row.Label == "启用着色器");
        section.Rows[section.Rows.IndexOf(toggle)] = new SettingToggleRow(toggle.Label, toggle.Note, toggle.IsOn,
            value => Settings.Shaders.Enabled = value, Save, RefreshShaderApplicability);
        if (!Settings.Shaders.Enabled)
            section.UpdateRows(section.Rows.Take(2).ToArray()); // 预设独立，总开关始终保留。
        return section;
    }

    private SettingSection ApplicableHdrCard()
    {
        var section = HdrCard();
        var mode = (SettingChoiceRow)section.Rows.Single(row => row.Label == "HDR 处理");
        section.Rows[section.Rows.IndexOf(mode)] = new SettingChoiceRow(mode.Label, mode.Note, mode.Choices,
            mode.Selected, Save, RefreshHdrApplicability);
        if (string.Equals(Settings.Video.HdrMode, "tonemap", StringComparison.OrdinalIgnoreCase))
            section.UpdateRows(section.Rows.Where(row => row.Label != "HDR 输出峰值亮度").ToArray());
        return section;
    }

    private void RefreshShaderApplicability()
    {
        if (_settings is null) return;
        var section = Sections.FirstOrDefault(card => card.Title == "画质与着色器");
        section?.UpdateRows(ApplicableShaderCard().Rows);
    }

    private void RefreshHdrApplicability()
    {
        if (_settings is null) return;
        var section = Sections.FirstOrDefault(card => card.Title == "HDR 与杜比视界");
        section?.UpdateRows(ApplicableHdrCard().Rows);
    }

    private SettingSliderRow SubtitleShadowRow() =>
        UnitSlider("阴影偏移 / 背景盒留白", 0,
            () => Settings.Playback.SubtitleShadowOffset, Live<string>(value => Settings.Playback.SubtitleShadowOffset = value),
            "普通样式和逐行盒：阴影向右下偏移；整体背景盒：控制底板留白，不另画文字阴影。"
                + "出厂 0.5；阴影与背景盒都使用下面的底板颜色", "sub-shadow-offset", RefreshSubtitleBackRows);

    private void SyncSubtitleBackControls()
    {
        if (_settings is null) return;
        // 2026-10-27 起字幕分类拆成三张卡，底板那几行住「字幕外观」那一张。
        var section = Sections.FirstOrDefault(card => card.Title == "字幕外观");
        if (section is null) return;
        var playback = Settings.Playback;
        var changed = false;
        if (section.Rows.FirstOrDefault(row => row.Label == "字幕底板") is SettingChoiceRow mode)
        {
            var label = MpvOutputOptions.Describe(MpvOutputOptions.SubtitleBackStyles, playback.SubtitleBackStyle);
            changed = mode.Selected?.Label != label;
            mode.ReseedSelection(label);
        }
        if (section.Rows.FirstOrDefault(row => row.Label == "阴影偏移 / 背景盒留白") is SettingSliderRow shadow
            && !shadow.Value.Equals(ParseUnit(playback.SubtitleShadowOffset) ?? 0))
        {
            // 滑块没有草稿输入和免保存 reseed；只换共享值确实变化的这一行，不用 setter 再保存/推送。
            section.Rows[section.Rows.IndexOf(shadow)] = SubtitleShadowRow();
            changed = true;
        }
        // 共享条件变化也要让同一条示例更新；不重造预览行、不推播放委托，也不额外保存。
        if (changed) _subtitlePreview?.Refresh();
    }

    private IReadOnlyList<SettingRow> SubtitleBackRows()
    {
        var playback = Settings.Playback;
        // background-box 在留白为 0 时仍有底板。未指定/未知阴影不是明确关闭，不凭缺值隐藏偏好。
        if (ParseUnit(playback.SubtitleShadowOffset) == 0
            && !string.Equals(playback.SubtitleBackStyle, "background-box", StringComparison.OrdinalIgnoreCase))
            return [];
        return
        [
            ColorRow("底板颜色", () => playback.SubtitleBackColor, Live<string>(value => playback.SubtitleBackColor = value),
                "sub-back-color", BackColorNote(playback)),
            Slider("底板不透明度（%）", 0, 100, 5, () => playback.SubtitleBackOpacity,
                Live<double>(value => playback.SubtitleBackOpacity = (int)value),
                "控制底板颜色的透明度；颜色选「不设置」时使用黑色，透明度仍按此值。逐行盒的描边盒由描边颜色控制", "sub-back-color")
        ];
    }

    private void RefreshSubtitleBackRows()
    {
        if (_settings is null) return;
        var section = Sections.FirstOrDefault(card => card.Title == "字幕外观");
        if (section is null) return;
        var rows = section.Rows.Where(row => row.Label is not ("底板颜色" or "底板不透明度（%）")).ToList();
        var anchor = rows.FindIndex(row => row.Label == "字幕底板");
        if (anchor < 0) return;
        rows.InsertRange(anchor + 1, SubtitleBackRows());
        // 只更新这两行；现有颜色行的说明从 desired 重述，不捕获未被采用的临时行。
        // 字体查询、预览与其余行对象/集合都留在原位，不额外 Save 或推送。
        section.UpdateRows(rows);
    }

    private void RefreshSettingsApplicability()
    {
        RefreshShaderApplicability();
        RefreshHdrApplicability();
        SyncSubtitleBackControls();
        RefreshSubtitleBackRows();
    }
}
