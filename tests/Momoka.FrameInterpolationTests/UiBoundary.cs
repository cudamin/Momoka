using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;

// Only the absent UI/persistence/parent-owner boundaries are substitutes. Choice/number selection,
// after callbacks, stable section merging and the new card execute current production source.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
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
}
