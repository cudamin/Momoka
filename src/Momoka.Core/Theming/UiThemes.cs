namespace Momoka.Theming;

/// <summary>
/// 五套固定深色主题与「跟随系统」，共用五个基础色推导出的角色表。
/// 系统选项始终保留 system id，深浅由外壳读取 Windows 应用模式决定，不能把一次解析的结果保存成固定主题。
/// 2026-09-05 删除的独立 daylight 主题仍不在目录，旧 id 继续回落到默认主题。
/// </summary>
public static class UiThemes
{
    /// <summary>设置文件里认不出来的 id 一律回落到这一套。</summary>
    public const string DefaultId = "emby-dark";

    public const string SystemId = "system";

    private static readonly UiTheme SystemDark = Make(SystemId, "跟随系统", "随 Windows 的浅色或深色应用模式自动切换。", dark: true,
        window: "#0C1116", surface: "#141B22", surfaceAlt: "#1C252E", text: "#F2F6FA", accent: "#6EE7C5");

    private static readonly UiTheme SystemLight = Make(SystemId, "跟随系统", "随 Windows 的浅色或深色应用模式自动切换。", dark: false,
        window: "#F1F3F6", surface: "#FFFFFF", surfaceAlt: "#E8EBEF", text: "#15181D", accent: "#2E7D32");

    /// <summary>
    /// 顺序就是设置里那排色板的顺序：默认的在最前。
    /// </summary>
    public static IReadOnlyList<UiTheme> All { get; } =
    [
        // 现在这套。窗口、面、次面、字、绿五个值和 Theme/Palette.xaml 里手调的那份一致 —— 那份现在只
        // 负责第一帧，真正生效的是这里推出来的表。
        Make("emby-dark", "薄荷影院", "深邃的蓝黑底色，配以柔和薄荷绿。", dark: true,
            window: "#0C1116", surface: "#141B22", surfaceAlt: "#1C252E", text: "#F2F6FA", accent: "#6EE7C5"),

        // 真黑，给 OLED：这块屏上 #16181C 和 #000000 差着一整档功耗，暗场也差着一整档。
        Make("oled-black", "纯黑", "面全部压到黑，给 OLED 和全暗的房间。", dark: true,
            window: "#000000", surface: "#0A0D10", surfaceAlt: "#13191F", text: "#F2F6FA", accent: "#6EE7C5"),

        Make("midnight", "午夜", "偏蓝的深色面配亮蓝强调色。", dark: true,
            window: "#0F1420", surface: "#161C2B", surfaceAlt: "#1E2637", text: "#E8EDF7", accent: "#4C8DF6"),

        Make("graphite", "石墨", "中性灰的面配琥珀色强调色，海报的颜色不被主题抢。", dark: true,
            window: "#17181A", surface: "#1F2124", surfaceAlt: "#282B2F", text: "#F0F1F3", accent: "#E0A22B"),

        Make("plum", "紫夜", "偏紫的深色面配淡紫强调色。", dark: true,
            window: "#15121C", surface: "#1D1926", surfaceAlt: "#262032", text: "#F1EDF8", accent: "#A97BF0"),

        SystemDark
    ];

    /// <summary>目录中的系统项以深色预览；运行时与测试分别解析深浅两种有效配色。</summary>
    public static UiTheme System(bool dark) => dark ? SystemDark : SystemLight;

    /// <summary>下拉框和自检都从这里取默认那套，不各自写一遍 id。</summary>
    public static UiTheme Default => Resolve(DefaultId);

    /// <summary>id 对不上就给默认那套 —— 手改过的设置文件、降级回来的旧版本都会走到这里。</summary>
    public static UiTheme Resolve(string? id) =>
        Find(id) ?? All.First(theme => theme.Id == DefaultId);

    public static UiTheme? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 五个色值 → 二十个角色。
    /// <para>
    /// 每条推导都是一句能说出口的规则：边框是次面朝前景挪一点，暗字是正字朝底色退一截，悬停比静止亮一档。
    /// 「朝前景」在深色主题里是朝白、浅色主题里是朝黑，这就是 <paramref name="dark"/> 在这里的全部作用。
    /// </para>
    /// </summary>
    private static UiTheme Make(
        string id, string name, string note, bool dark,
        string window, string surface, string surfaceAlt, string text, string accent,
        string? danger = null, string? warning = null, string? info = null)
    {
        var win = ThemeColor.Parse(window);
        var sur = ThemeColor.Parse(surface);
        var alt = ThemeColor.Parse(surfaceAlt);
        var ink = ThemeColor.Parse(text);
        var acc = ThemeColor.Parse(accent);

        // 「更靠前」的方向。深色主题往白走，浅色主题往黑走。
        var forward = dark ? ThemeColor.Rgb(0xFF, 0xFF, 0xFF) : ThemeColor.Rgb(0, 0, 0);
        var black = ThemeColor.Rgb(0, 0, 0);
        var white = ThemeColor.Rgb(0xFF, 0xFF, 0xFF);

        var palette = new UiPalette
        {
            Window = win,
            Surface = sur,
            SurfaceAlt = alt,
            SurfaceElevated = sur.Mix(forward, 0.05),
            SurfaceHover = alt.Mix(forward, 0.07),
            Border = alt.Mix(forward, 0.09),
            BorderStrong = alt.Mix(forward, 0.20),

            Text = ink,
            TextDim = ink.Mix(win, 0.36),

            // 浅色下退到 0.55 会跌破 3:1。系统模式的深浅两套都由 ThemeTests 验证，不放宽淡字门槛。
            TextFaint = ink.Mix(win, 0.52),
            TextOnAccent = OnAccent(acc),

            Accent = acc,
            AccentHover = dark ? acc.Mix(white, 0.14) : acc.Mix(black, 0.14),
            AccentPressed = acc.Mix(black, dark ? 0.14 : 0.28),
            AccentSoft = win.Mix(acc, dark ? 0.16 : 0.12),
            AccentMuted = acc.WithAlpha(dark ? (byte)0x33 : (byte)0x2B),

            Danger = ThemeColor.Parse(danger ?? (dark ? "#E5534B" : "#C0332B")),
            Warning = ThemeColor.Parse(warning ?? (dark ? "#E3B341" : "#8A6100")),
            Info = ThemeColor.Parse(info ?? (dark ? "#539BF5" : "#1F6FD0")),

            // 遮罩不跟着主题走色相：它的工作是把身后那一页压下去，而不是染色。浅色主题下透一点，
            // 因为下面本来就是亮的，压太狠会显得对话框凭空掉进一个黑洞里。
            Scrim = ThemeColor.Parse("#0C0E11").WithAlpha(dark ? (byte)0xB4 : (byte)0x8C)
        };

        return new UiTheme(id, name, note, dark, palette);
    }

    /// <summary>
    /// 压在强调色上的字：先试浅纸，够 4.5:1 就用它，不够就换深墨。
    /// <para>
    /// 顺序是有意的。「亮底配深字」在对比度上常常更划算 —— Emby 的绿配黑是 8:1，配白只有 2.6:1 —— 但一颗
    /// 强调色按钮上白字才是人预期的样子，所以只有白字真读不出来时才换。两个都不是纯黑纯白，各兑了一点强调色
    /// 进去：纯白压在饱和色上会有一层刺眼的边。
    /// </para>
    /// </summary>
    private static ThemeColor OnAccent(ThemeColor accent)
    {
        var paper = ThemeColor.Rgb(0xFF, 0xFF, 0xFF).Mix(accent, 0.06);
        return ThemeColor.Contrast(paper, accent) >= 4.5
            ? paper
            : ThemeColor.Rgb(0, 0, 0).Mix(accent, 0.10);
    }
}
