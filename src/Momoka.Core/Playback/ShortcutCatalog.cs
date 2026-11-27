using System.Text;

namespace Momoka.Playback;

/// <summary>
/// 一次按键：一个键，加上三个修饰键的开关。
/// <para>
/// <see cref="Key"/> 是一个**规范 token 串**（"Space"、"Left"、"F"、"BracketLeft"…），不是
/// <c>Windows.System.VirtualKey</c> —— Core 这一层不碰 WinRT/UI（见 CLAUDE.md）。外壳那边有一张
/// <c>KeyStrokeInterop</c>（VirtualKey ↔ token）是全项目唯一出现 <c>VirtualKey</c> 的地方，它产出的
/// token 就是这里认的这套词汇；修饰键三个开关由外壳在按下那一刻从系统读（<c>KeyRoutedEventArgs</c> 不带
/// 修饰键，见 <c>Native.ShiftHeld</c> 那段注释）。
/// </para>
/// </summary>
public readonly record struct KeyStroke(string Key, bool Ctrl, bool Alt, bool Shift)
{
    /// <summary>「没有键」——用来表示某个动作被显式解绑，或一次解析失败。<see cref="Key"/> 为空串。</summary>
    public static readonly KeyStroke None = new("", false, false, false);

    /// <summary>没有键就是空。<c>default(KeyStroke)</c> 的 Key 是 null，这里一并当空看，所以别拿它和别的比。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Key);
}

/// <summary>播放器里一个可重绑的动作：稳定 Id、屏上中文名、装机默认键。</summary>
public sealed record ShortcutAction(string Id, string Label, KeyStroke Default);

/// <summary>
/// 播放器键盘快捷键这件事的全部判断，做成纯函数放在 Core —— 单元测试进不到外壳那个程序集，而「这个键触发
/// 哪个动作」「这个组合键有没有和别人撞」「存到文件里长什么样、显示成什么样」都是一给定输入就有唯一答案的
/// 判断，留在视图模型里就是没人看着的判断（CLAUDE.md 分层那条）。
/// <para>
/// <b>存储只存改动。</b> 设置文件里那张 <c>Dictionary&lt;string,string&gt;</c>（<see cref="Configuration"/>
/// 里的 <c>ShortcutSettings.Bindings</c>）只记用户动过的：缺键 = 用装机默认；空串 = 用户显式解绑（和默认不
/// 一样，默认都是有键的）；否则是一个 <see cref="Serialize"/> 出来的 token 串。装机是空字典，所以新装的人
/// 和从旧版本升上来的人拿到的都是这里定义的那套默认，日后加一个动作也不用迁移 —— 老用户的字典里没有它，
/// 自然落到它的默认上。
/// </para>
/// <para>
/// 退出播放与跳过提示也在动作目录中。两个跳过提示动作可与普通动作共用按键；提示出现时先处理提示，
/// 提示收起后回到普通动作。两个提示动作彼此仍保持唯一，防止确认与关闭同时命中。
/// </para>
/// </summary>
public static class ShortcutCatalog
{
    public const string ExitPlayback = "exit-fullscreen-or-stop";
    public const string DismissSkip = "dismiss-skip";
    public const string TakeSkip = "take-skip";

    /// <summary>只在跳过提示显示时参与派发的动作；它们与普通动作分别检查按键占用。</summary>
    public static bool IsSkipPrompt(string id) => id is DismissSkip or TakeSkip;

    /// <summary>
    /// 不再有保留键。Esc、Enter、Delete 与其他键一样可以改绑；录制时 Esc 可绑定，
    /// 离开捕获框或按 Tab（未捕获时）结束录制。
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedKeys = [];

    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Tab"] = "Tab",
        ["CapsLock"] = "Caps Lock",
        ["NumLock"] = "Num Lock",
        ["ScrollLock"] = "Scroll Lock",
        ["PrintScreen"] = "Print Screen",
        ["Pause"] = "Pause",
        ["LeftWindows"] = "左 Win",
        ["RightWindows"] = "右 Win",
        ["Application"] = "菜单键",
        ["Semicolon"] = ";",
        ["Slash"] = "/",
        ["Backslash"] = "\\",
        ["Quote"] = "'",
        ["Oem102"] = "\\\\",
        ["NumPadDecimal"] = "小键盘 .",
        ["NumPadDivide"] = "小键盘 /",
        ["NumPadMultiply"] = "小键盘 *",
        ["NumPadSubtract"] = "小键盘 -",
        ["NumPadAdd"] = "小键盘 +",
        ["NumPadEnter"] = "小键盘回车",
        ["VolumeMute"] = "静音键",
        ["VolumeDown"] = "音量减小键",
        ["VolumeUp"] = "音量增大键",
        ["MediaNextTrack"] = "下一曲键",
        ["MediaPreviousTrack"] = "上一曲键",
        ["MediaStop"] = "媒体停止键",
        ["MediaPlayPause"] = "播放/暂停媒体键",
        ["BrowserBack"] = "浏览器后退键",
        ["BrowserForward"] = "浏览器前进键",
        ["BrowserRefresh"] = "浏览器刷新键",
        ["BrowserStop"] = "浏览器停止键",
        ["BrowserSearch"] = "浏览器搜索键",
        ["BrowserFavorites"] = "浏览器收藏键",
        ["BrowserHome"] = "浏览器主页键"
    };

    /// <summary>
    /// 可重绑动作，次序就是设置页上从上到下的次序。原有动作的默认键不变；新增键冲突时优先保留用户的绑定。
    /// <para>
    /// 后 4 个动作用的是那 4 颗方向键，是 2026-09-20 那两句话加出来的：「新增键盘上的左和右设置为播放进度
    /// 快退五秒和快进五秒，上方向键和下方向键改为播放进度快进30秒和快退30秒」。四颗方向键因此各有一对步长 ——
    /// 短的那对是 <c>seek-backward</c> / <c>seek-forward</c>（← →，用设置里的「快退/快进跨度」），新的
    /// 长的那对是下面两个（↑ ↓，用「大步快退/大步快进跨度」，装机 30 秒）。原先 ↑↓ 的音量搬到了 Ctrl+↑↓：
    /// 一颗键只能归一个动作，而一个动作只能有一颗键，所以「上下改为快进快退」这句话必然把音量让出去 —— 让到
    /// 修饰键上而不是删掉，是因为音量条还在、滚轮还在，键盘那一半不该凭空消失。
    /// </para>
    /// </summary>
    public static IReadOnlyList<ShortcutAction> Actions { get; } = Build();

    /// <summary>
    /// 此刻设置页该列出的动作：弹幕那四条只在弹幕真的能画出来的时候才排得动 —— 服务关着、或者播放后端
    /// 画不了弹幕时按下去什么都不会发生，所以跟着入口一起收起（CLAUDE.md「功能可用性与显隐」）。
    /// 绑定本身留着不动：条件恢复后照旧显示用户改过的键。
    /// </summary>
    public static IReadOnlyList<ShortcutAction> Available(bool danmakuReady) => danmakuReady
        ? Actions
        : Actions.Where(action => !IsDanmaku(action)).ToList();

    /// <summary>这条动作是不是弹幕那一组（Id 里带 danmaku 的那四条）。</summary>
    public static bool IsDanmaku(ShortcutAction action) => action.Id.Contains("danmaku", StringComparison.Ordinal);

    private static KeyStroke K(string key, bool ctrl = false, bool shift = false, bool alt = false) =>
        new(key, ctrl, alt, shift);

    private static IReadOnlyList<ShortcutAction> Build() =>
    [
        new("toggle-pause", "播放 / 暂停", K("Space")),
        new("seek-backward", "快退", K("Left")),
        new("seek-forward", "快进", K("Right")),
        new("seek-backward-long", "快退（大步）", K("Down")),
        new("seek-forward-long", "快进（大步）", K("Up")),
        new("volume-up", "音量增大", K("Up", ctrl: true)),
        new("volume-down", "音量减小", K("Down", ctrl: true)),
        new("toggle-fullscreen", "全屏切换", K("F")),
        new("toggle-mute", "静音", K("M")),
        new("previous-episode", "上一集", K("P")),
        new("next-episode", "下一集", K("N")),
        new("toggle-pin", "窗口置顶", K("T")),
        new("chapter-previous", "上一章节", K("PageUp")),
        new("chapter-next", "下一章节", K("PageDown")),
        new("speed-down", "减速（−0.1）", K("BracketLeft")),
        new("speed-up", "加速（+0.1）", K("BracketRight")),
        new("speed-reset", "恢复常速", K("Back")),
        new("subtitle-delay-decrease", "字幕延迟 −0.1 秒", K("Z")),
        new("subtitle-delay-increase", "字幕延迟 +0.1 秒", K("Z", shift: true)),
        new("audio-delay-decrease", "音频延迟 −0.1 秒", K("X")),
        new("audio-delay-increase", "音频延迟 +0.1 秒", K("X", shift: true)),
        new("screenshot", "截图（当前尺寸，含字幕）", K("S")),
        new("screenshot-original", "截图（原始尺寸，不含字幕）", K("S", shift: true)),
        new("toggle-subtitle", "字幕显示开关", K("V")),
        new("subtitle-next", "下一条字幕轨道", K("J")),
        new("subtitle-previous", "上一条字幕轨道", K("J", shift: true)),
        new("audio-next", "下一条音轨", K("A")),
        new("audio-previous", "上一条音轨", K("A", shift: true)),
        new("frame-forward", "前进一帧（暂停）", K("E")),
        new("frame-backward", "后退一帧（暂停）", K("E", shift: true)),
        new("seek-start", "跳到片头", K("Home")),
        new("revert-seek", "回到上次跳转前的位置", K("Back", shift: true)),
        new("show-playback-info", "播放信息", K("I", ctrl: true)),
        new("show-stats", "显示一次播放统计", K("I")),
        new("toggle-stats", "播放／着色器统计切换", K("I", shift: true)),
        new("toggle-danmaku", "弹幕显示开关", K("D")),
        new("danmaku-delay-decrease", "弹幕提前 0.5 秒", K("D", ctrl: true)),
        new("danmaku-delay-increase", "弹幕延后 0.5 秒", K("D", ctrl: true, shift: true)),
        new("seek-backward-fine", "快退 1 秒（精细）", K("Left", shift: true)),
        new("seek-forward-fine", "快进 1 秒（精细）", K("Right", shift: true)),
        new("subtitle-seek-previous", "跳到上一句字幕", K("Left", ctrl: true)),
        new("subtitle-seek-next", "跳到下一句字幕", K("Right", ctrl: true)),
        new("video-zoom-out", "画面缩小", K("BracketLeft", ctrl: true)),
        new("video-zoom-in", "画面放大", K("BracketRight", ctrl: true)),
        new("video-zoom-reset", "画面缩放归零", K("0", ctrl: true)),
        new("video-pan-left", "画面左移", K("Left", alt: true)),
        new("video-pan-right", "画面右移", K("Right", alt: true)),
        new("video-pan-up", "画面上移", K("Up", alt: true)),
        new("video-pan-down", "画面下移", K("Down", alt: true)),
        new("video-pan-reset", "画面位置归零", K("Home", ctrl: true)),
        new("subtitle-scale-down", "字幕缩小", K("BracketLeft", alt: true)),
        new("subtitle-scale-up", "字幕放大", K("BracketRight", alt: true)),
        new("subtitle-position-up", "字幕上移", K("Up", shift: true)),
        new("subtitle-position-down", "字幕下移", K("Down", shift: true)),
        new("subtitle-render-style", "字幕渲染样式切换", K("U")),
        new("subtitle-timing-fix", "字幕时序修复切换", K("U", shift: true)),
        new("subtitle-ass-margins", "ASS 字幕输出到黑边", K("A", alt: true)),
        new("subtitle-srt-margins", "SRT 字幕输出到黑边", K("S", alt: true)),
        new("subtitle-pgs-margins", "PGS 字幕输出到黑边", K("P", alt: true)),
        new("subtitle-pgs-gray", "PGS 字幕灰度转换", K("G", alt: true)),
        new("subtitle-reload", "重载当前字幕", K("R")),
        new("subtitle-delay-reset", "字幕延迟归零", K("Z", ctrl: true)),
        new("audio-delay-reset", "音频延迟归零", K("X", ctrl: true)),
        new("toggle-ab-loop", "设置 / 清除片段循环", K("L")),
        new("clear-ab-loop", "清除片段循环", K("L", shift: true)),
        new("toggle-loop", "单片循环播放开关", K("L", ctrl: true)),
        new("speed-half", "切换到 0.5 倍速", K("1", ctrl: true)),
        new("speed-one-and-half", "切换到 1.5 倍速", K("2", ctrl: true)),
        new("speed-double", "切换到 2 倍速", K("3", ctrl: true)),
        new("danmaku-delay-reset", "弹幕延迟归零", K("D", shift: true)),
        // 「窗口不在前台时单击画面播放/暂停」的开关（2026-11-20 用户令「记得加快捷键」）。B＝Background，
        // 与菜单里那一行同一个动作；默认键 B 此前没人用（Alt+B 是次字幕 bidi，与它不同组合）。
        new("toggle-background-click", "窗口不在前台时单击画面播放/暂停", K("B")),
        new(ExitPlayback, "退出全屏 / 退出播放", K("Escape")),
        new(DismissSkip, "关闭跳过片头 / 片尾提示", K("Delete")),
        new(TakeSkip, "确认跳过片头 / 片尾", K("Enter")),
        .. NativeMenuShortcuts.Actions,
        .. ChapterShortcuts.Actions
    ];

    /// <summary>这个动作此刻真正生效的键：动过就用动过的，没动过用默认，显式解绑（空串）就是 <see cref="KeyStroke.None"/>。</summary>
    private static KeyStroke Effective(IReadOnlyDictionary<string, string> bindings, ShortcutAction action)
    {
        if (!bindings.TryGetValue(action.Id, out var stored)) return action.Default;
        if (string.IsNullOrEmpty(stored)) return KeyStroke.None;

        // 认不出来的写法（手改设置文件改坏了）退回默认，而不是让这个动作变成没有键 —— 「界面在骗人」里
        // 最轻的一种也比一个默认动作莫名其妙失灵好。真正的垃圾键由 Clean 在读盘时清掉。
        return TryParse(stored, out var stroke) ? stroke : action.Default;
    }

    /// <summary>每个动作此刻生效的键。设置页刷新每一行的显示、自检核对时都用它。</summary>
    public static IReadOnlyDictionary<string, KeyStroke> Resolve(IReadOnlyDictionary<string, string> bindings)
    {
        var result = Actions.ToDictionary(action => action.Id, _ => KeyStroke.None, StringComparer.Ordinal);
        var used = new HashSet<KeyStroke>();
        var promptUsed = new HashSet<KeyStroke>();
        // 先放用户显式绑定，再放默认键。升级新增 D 等动作时不能抢走用户早已绑到 D 的旧动作。
        foreach (var action in Actions.Where(action => bindings.ContainsKey(action.Id)))
        {
            var stroke = Effective(bindings, action);
            var occupied = IsSkipPrompt(action.Id) ? promptUsed : used;
            if (!stroke.IsEmpty && occupied.Add(stroke)) result[action.Id] = stroke;
        }
        foreach (var action in Actions.Where(action => !bindings.ContainsKey(action.Id)))
        {
            var occupied = IsSkipPrompt(action.Id) ? promptUsed : used;
            if (occupied.Add(action.Default)) result[action.Id] = action.Default;
        }
        return result;
    }

    /// <summary>
    /// 按下这个组合键该触发哪个动作，没有就返回 null。空组合键谁都不触发。
    /// <para>
    /// 提示显示时先找确认/关闭动作；其余时间只找普通动作。同一组内由 <see cref="Resolve"/> 保持唯一，
    /// 因此一次按键最多派发一个动作。默认确认键兼容小键盘回车，改绑或解绑后不再保留这个别名。
    /// </para>
    /// </summary>
    public static string? Lookup(IReadOnlyDictionary<string, string> bindings, KeyStroke stroke, bool skipOffered = false)
    {
        if (stroke.IsEmpty) return null;

        var resolved = Resolve(bindings);
        if (skipOffered)
        {
            foreach (var (id, effective) in resolved)
                if (IsSkipPrompt(id) && effective == stroke) return id;

            if (!bindings.ContainsKey(TakeSkip) && resolved[TakeSkip] == K("Enter")
                && stroke == K("NumPadEnter")) return TakeSkip;
        }
        foreach (var (id, effective) in resolved)
            if (!IsSkipPrompt(id) && effective == stroke) return id;

        return null;
    }

    /// <summary>一次重绑的结果：要么成功（<see cref="Bindings"/> 是新的一份），要么被占用（<see cref="Conflict"/> 是占用者的 Id、绑定原封不动）。</summary>
    public readonly record struct RebindResult(IReadOnlyDictionary<string, string> Bindings, string? Conflict, bool Replaced = false);

    /// <summary>
    /// 把 <paramref name="id"/> 这个动作绑到 <paramref name="stroke"/> 上。
    /// <para>
    /// 默认冲突时保持原绑定不变。调用方明确选择替换时，新动作接收此键、旧占用者接收新动作原来的键；若新动作
    /// 原来未绑定，旧占用者就解绑。普通动作与跳过提示动作分别保持唯一，跨组允许共用；
    /// 不静默改变其他动作。绑回自己的默认键会删掉改动记录。
    /// </para>
    /// <para>空键或未知动作原样返回；全部按键（包含 Esc、回车和 Delete）均可绑定。</para>
    /// </summary>
    public static RebindResult Rebind(IReadOnlyDictionary<string, string> bindings, string id, KeyStroke stroke,
        bool replaceConflict = false)
    {
        var action = Actions.FirstOrDefault(a => a.Id == id);
        if (action is null || stroke.IsEmpty) return new(bindings, null);

        var effective = Resolve(bindings);
        ShortcutAction? occupant = null;
        foreach (var other in Actions)
        {
            if (other.Id == id || IsSkipPrompt(other.Id) != IsSkipPrompt(id)) continue;
            if (effective[other.Id] == stroke)
            {
                occupant = other;
                break;
            }
        }

        if (occupant is not null && !replaceConflict) return new(bindings, occupant.Id);

        var next = new Dictionary<string, string>(bindings, StringComparer.Ordinal);
        SetBinding(next, action, stroke);
        if (occupant is not null)
        {
            var previous = effective[action.Id];
            if (previous.IsEmpty) next[occupant.Id] = "";
            else SetBinding(next, occupant, previous);
        }

        return new(next, null, occupant is not null);
    }

    private static void SetBinding(Dictionary<string, string> bindings, ShortcutAction action, KeyStroke stroke)
    {
        if (stroke == action.Default) bindings.Remove(action.Id);
        else bindings[action.Id] = Serialize(stroke);
    }

    /// <summary>把这个动作解绑（× 那颗按钮）。存空串而不是删键：默认都是有键的，删键会退回默认，空串才是「就是不要」。</summary>
    public static IReadOnlyDictionary<string, string> Clear(IReadOnlyDictionary<string, string> bindings, string id)
    {
        if (Actions.All(a => a.Id != id)) return bindings;

        var next = new Dictionary<string, string>(bindings, StringComparer.Ordinal) { [id] = "" };
        return next;
    }

    /// <summary>读盘时把手改坏的清掉：认不出的动作 Id、解析不了的写法，全丢；空串（显式解绑）留着。</summary>
    public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? bindings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bindings is null) return result;

        var ids = Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, value) in bindings)
        {
            if (!ids.Contains(id)) continue;
            if (string.IsNullOrEmpty(value)) { result[id] = ""; continue; }
            if (!TryParse(value, out var stroke) || IsReserved(stroke)) continue;
            result[id] = Serialize(stroke);
        }

        return result;
    }

    /// <summary>保留给旧调用点的兼容检查；现在所有非空按键都能绑定。</summary>
    public static bool IsReserved(KeyStroke stroke) => false;

    /// <summary>动作的中文名，找不到就把 Id 原样给回去（不该发生，给个不炸的兜底）。</summary>
    public static string Label(string id) => Actions.FirstOrDefault(a => a.Id == id)?.Label ?? id;

    /// <summary>存进设置文件那份写法：修饰键按 Ctrl+Alt+Shift 的固定次序，加号连键。空键是空串。</summary>
    public static string Serialize(KeyStroke stroke)
    {
        if (stroke.IsEmpty) return "";

        var parts = new List<string>(4);
        if (stroke.Ctrl) parts.Add("Ctrl");
        if (stroke.Alt) parts.Add("Alt");
        if (stroke.Shift) parts.Add("Shift");
        parts.Add(stroke.Key);
        return string.Join('+', parts);
    }

    /// <summary>把 <see cref="Serialize"/> 的写法读回来。空串或只有修饰键、或有两个非修饰键，都算解析失败。</summary>
    public static bool TryParse(string? text, out KeyStroke stroke)
    {
        stroke = KeyStroke.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false;
        string? key = null;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default:
                    if (key is not null) return false;
                    key = part;
                    break;
            }
        }

        if (key is null) return false;
        if (!ShortcutKeyCatalog.IsKnownToken(key)) return false;

        stroke = new KeyStroke(key, ctrl, alt, shift);
        return true;
    }

    /// <summary>屏上显示那份写法：修饰键中间带空格，特殊键翻成人看得懂的字（空格、←、[、Esc…）。空键是「未设置」。</summary>
    public static string Format(KeyStroke stroke)
    {
        if (stroke.IsEmpty) return "未设置";

        var text = new StringBuilder();
        if (stroke.Ctrl) text.Append("Ctrl + ");
        if (stroke.Alt) text.Append("Alt + ");
        if (stroke.Shift) text.Append("Shift + ");
        text.Append(Display(stroke.Key));
        return text.ToString();
    }

    private static string Display(string key) => key switch
    {
        _ when DisplayNames.TryGetValue(key, out var label) => label,
        _ when key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 => $"小键盘 {key[^1]}",
        "ShiftKey" => "Shift",
        "Space" => "空格",
        "Enter" => "回车",
        "Delete" => "Del",
        "Left" => "←",
        "Right" => "→",
        "Up" => "↑",
        "Down" => "↓",
        "Back" => "Backspace",
        "BracketLeft" => "[",
        "BracketRight" => "]",
        "Minus" => "-",
        "Equal" => "=",
        "Comma" => ",",
        "Period" => ".",
        "Backquote" => "`",
        "Escape" => "Esc",
        _ when key.Length == 4 && key.StartsWith("Vk", StringComparison.Ordinal) => $"按键 {key[2..]}",
        _ => key
    };

    /// <summary>
    /// 搜索操作名称和当前生效的按键。空格分隔的词都要匹配；加号、英文大小写与按键别名不影响结果。
    /// 只传当前键而不是默认键，改绑或解绑后搜索结果才不会把旧键说成仍有效。
    /// </summary>
    public static bool MatchesSearch(string? query, string label, KeyStroke stroke)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var aliases = stroke.Key switch
        {
            "Space" => "Spacebar 空格",
            "Enter" => "Enter 回车",
            "Delete" => "Delete Del 删除",
            "Escape" => "Escape Esc",
            "Back" => "Backspace 退格",
            "Left" => "Left 左方向键",
            "Right" => "Right 右方向键",
            "Up" => "Up 上方向键",
            "Down" => "Down 下方向键",
            "BracketLeft" => "[ 左方括号",
            "BracketRight" => "] 右方括号",
            _ => ""
        };
        var text = $"{label} {Format(stroke)} {Serialize(stroke)} {aliases}";
        return MatchesText(query, text);
    }

    /// <summary>
    /// 搜索一段不含按键的文字，规则与 <see cref="MatchesSearch"/> 相同（空格分隔的词都要命中；空白与加号不参与
    /// 匹配；英文不分大小写）。快捷键页里那几行步长与倍率不是快捷键，但同样该被搜索栏找到，所以把这段判断单独拿了出来。
    /// </summary>
    public static bool MatchesText(string? query, string text)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var searchable = SearchText(text);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(SearchText).All(word => searchable.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string SearchText(string text) => new(text.Where(character =>
        !char.IsWhiteSpace(character) && character is not '+' and not '＋').ToArray());

    /// <summary>测试不变量：没有两个动作共用同一个默认键 —— 否则装机就带着一个 <see cref="Rebind"/> 拦不住的冲突。</summary>
    public static bool DefaultsAreUnique()
    {
        var seen = new HashSet<KeyStroke>();
        foreach (var action in Actions)
            if (!seen.Add(action.Default)) return false;

        return true;
    }
}
