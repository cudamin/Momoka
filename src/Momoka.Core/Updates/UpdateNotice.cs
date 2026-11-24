namespace Momoka.Updates;

/// <summary>
/// 「发现新版本」这件事的几句措辞。放在 Core 而不是两个调用方各写一遍：启动时那一趟和设置页里手动按的那一下
/// 说的是同一件事，两处各写一份迟早一处改了另一处没跟上 —— 而两句话不一致时，屏上看起来都挺对。
/// </summary>
public static class UpdateNotice
{
    /// <summary>问话的抬头。也是自检认这一件事的记号。</summary>
    public const string Title = "发现新版本";

    /// <summary>确认那颗按钮上的字。</summary>
    public const string OpenLabel = "打开下载页";

    /// <summary>设置页那一行「检查新版本」的标签，自检按它找这一行。</summary>
    public const string RowLabel = "检查新版本";

    /// <summary>「启动时自动检查新版本」那一格开关的标签，自检按它找这一格。</summary>
    public const string ToggleLabel = "启动时自动检查新版本";

    /// <summary>检查进行中，那一行读数。</summary>
    public const string Checking = "正在检查…";

    /// <summary>检查失败，那一行读数（具体原因由页面那条提示条说）。</summary>
    public const string Failed = "检查失败";

    /// <summary>
    /// 「Momoka 0.1.7 已发布，当前是 0.1.6。是否打开下载页？」加一段去向 —— 两边各是哪一版都写出来，用户才知道
    /// 要不要去下；第二段写「设置 → 关于」里那个开关的名字，想关掉这类提示的人不用猜它在哪儿（2026-11-24 用户令）。
    /// 带 <c>{0}</c>（新版）与 <c>{1}</c>（手上这一版）两个占位：设置页要按英文词典翻译这一句，得拿到模板本身，
    /// 所以模板单独露出来，成品只由 <see cref="Prompt"/> 填。
    /// </summary>
    public const string PromptFormat =
        "Momoka {0} 已发布，当前是 {1}。是否打开下载页？\n\n"
        + "设置 → 关于 里可以打开或关闭「" + ToggleLabel + "」，关掉后启动时不再提示。";

    /// <summary>「Momoka 0.1.7 已发布，当前是 0.1.6。是否打开下载页？」—— 措辞见 <see cref="PromptFormat"/>。</summary>
    public static string Prompt(AppRelease release, Version current) =>
        string.Format(PromptFormat, release.VersionText, current.ToString(3));

    /// <summary>
    /// 弹不出对话框时（窗口还没落到树上）退成一条不带按钮的提示用的话 —— 末尾不写「是否打开下载页」，因为那里
    /// 没有那颗按钮可按，写出来就是一句兑现不了的话。
    /// </summary>
    public static string Available(AppRelease release, Version current) =>
        $"发现新版本 Momoka {release.VersionText}（当前 {current.ToString(3)}），"
        + "可在 设置 → 关于 里打开下载页。";

    /// <summary>设置页那一行读数：有新版。</summary>
    public static string Found(AppRelease release) => $"发现新版本 {release.VersionText}";

    /// <summary>设置页那一行读数：已是最新。</summary>
    public static string UpToDate(Version current) => $"已是最新版本（{current.ToString(3)}）";
}
