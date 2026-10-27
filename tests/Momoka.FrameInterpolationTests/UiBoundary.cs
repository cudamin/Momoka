using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;

// Only the absent UI/persistence/parent-owner boundaries are substitutes. Choice/number selection,
// after callbacks, stable section merging and the new card execute current production source.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }

    /// <summary>
    /// 生产 <see cref="Momoka.Shell.ViewModels.SettingRow.DividerThickness"/> 的返回类型，也是行模板
    /// <c>BorderThickness</c> 绑定的那个值。真类型在 WinUI 里，这个项目不引 Windows App SDK，只能替身；
    /// 值语义照着来（struct、四个只读边、两个构造、按四边相等），否则抽取出来的生产判断在这边量不到同一件事。
    /// 2026-10-27 精简模式行间画线之前，这个边界只需要 <see cref="Visibility"/>。
    /// </summary>
    public readonly struct Thickness : IEquatable<Thickness>
    {
        public Thickness(double uniformLength)
            : this(uniformLength, uniformLength, uniformLength, uniformLength)
        {
        }

        public Thickness(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Left { get; }

        public double Top { get; }

        public double Right { get; }

        public double Bottom { get; }

        public bool Equals(Thickness other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

        public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

        public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

        public override string ToString() => $"{Left},{Top},{Right},{Bottom}";
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed partial class SettingsViewModel
    {
        private const string VideoCategory = "视频输出";
        private const string NextPlayback = "自动保存，下次播放生效。";
        internal AppSettings Settings { get; } = new();
        internal ObservableCollection<SettingSection> Sections { get; } = [];
        internal int SaveCount { get; private set; }
        internal int BackendRefreshCount { get; private set; }
        private void Save() => SaveCount++;
        private void RefreshVideoBackend()
        {
            BackendRefreshCount++;
            if (BackendRefreshCount > 50) throw new InvalidOperationException("刷新发生递归");
            // Simulate the exact parent contract: backend refresh calls our row-only refresh.
            RefreshFrameInterpolationRows();
        }
        internal SettingSection BuildCard()
        {
            var card = FrameInterpolationCard();
            Sections.Add(card);
            return card;
        }
        internal void RefreshCard() => RefreshFrameInterpolationRows();
    }

    public sealed class SettingToggleRow : SettingRow
    {
        internal SettingToggleRow(string label, string note, bool value, Action<bool> write, Action save)
            : base(label, note) => IsOn = value;
        public bool IsOn { get; private set; }
        internal void Reseed(bool value) => IsOn = value;
    }

    public sealed class SettingToggleGroupRow : SettingRow
    {
        internal SettingToggleGroupRow() : base("", null) { }
        public IReadOnlyList<SettingToggleRow> Toggles { get; } = [];
    }

    public sealed class SettingFactRow : SettingRow
    {
        internal SettingFactRow() : base("", null) { }
        public string Value { get; internal set; } = "";
    }

    // 卡片合并（SettingSection.UpdateRows）按「标签＋类型」复用在场行，因此这两类也要有替身 ——
    // 补帧卡自己不用滑块和快捷键行，但那段真实源码的 switch 会点到它们的名字。
    public sealed class SettingSliderRow : SettingRow
    {
        internal SettingSliderRow() : base("", null) { }
        public double Value { get; internal set; }
        internal void Reseed(double value) => Value = value;
    }

    public sealed class SettingShortcutRow : SettingRow
    {
        internal SettingShortcutRow() : base("", null) { }
        public string ComboText { get; internal set; } = "";
    }
}
