using Momoka.Configuration;
using Momoka.Danmaku;

namespace DanmakuReview;

internal class SettingRow(string label) { internal string Label { get; } = label; }
internal sealed class SettingSection(string category, string title, string description, List<SettingRow> rows)
{
    internal string Category { get; } = category;
    internal string Title { get; } = title;
    internal string Description { get; } = description;
    internal List<SettingRow> Rows { get; } = rows;
}
internal sealed class SettingSecretRow : SettingRow
{
    internal SettingSecretRow(string label, Action<string> set, Func<bool> hasValue, Func<bool> save, Action after) : base(label) { }
    internal void Restate(string description) { }
}
internal sealed class SettingTextRow : SettingRow
{
    internal SettingTextRow(string label, string description, string placeholder, string value, Func<string, string> set, Action save) : base(label) { }
}
internal sealed partial class SettingsRows(AppSettings settings)
{
    private const string DanmakuCategory = "弹幕";
    private static readonly (string Label, int Value)[] DanmakuDensityModes = [("正常", 0), ("较多", 1), ("重叠", 2)];
    private static readonly (string Label, int Value)[] DanmakuBorderStyles = [("描边", 0), ("重墨", 1), ("45° 投影", 2)];
    private static readonly (string Label, DanmakuSourceMode Value)[] DanmakuSources =
        [("弹弹play", DanmakuSourceMode.DandanPlay), ("danmu_api（自己部署）", DanmakuSourceMode.DanmuApi), ("两者合并", DanmakuSourceMode.Both)];
    private readonly ISecretProtector _secretProtector = new NoSecrets();
    private AppSettings Settings { get; } = settings;
    private bool DanmakuReady => DanmakuSourceRules.IsAvailable(Settings);
    internal string[] Labels() => DanmakuSections().SelectMany(s => s.Rows).Select(r => r.Label).ToArray();
    private static SettingRow Toggle(string label, string description, Func<bool> get, Action<bool> set, Action after) => new(label);
    private static SettingRow Choice<T>(string label, (string Label, T Value)[] choices, Func<T> get, Action<T> set, string description, Action after) => new(label);
    private static SettingRow Text(string label, string placeholder, Func<string> get, Action<string> set, string description, Action after) => new(label);
    private static SettingRow Slider(string label, double min, double max, double step, Func<double> get, Action<double> set, string description = "", Action? after = null) => new(label);
    private static SettingRow Fact(string label, string description, string value, string button, Action action) => new(label);
    private static SettingRow DanmakuFont(DanmakuSettings settings) => new("弹幕字体");
    private void Save() { }
    private void RefreshDanmakuRows() { }
    private static void ApplyDanmakuSettings() { }
    private bool TrySaveDanmuApiToken() => true;
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("Fixture never reads or writes secrets.");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("Fixture never reads or writes secrets.");
    }
}
