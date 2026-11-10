using Momoka.Configuration;
using Momoka.LocalMedia;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static TaskCompletionSource<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>? Pending { get; set; }
        public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int logicalWidth)
            => Pending?.Task ?? Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(null);
    }
}

namespace Momoka.Shell.Platform
{
    internal interface ISystemLauncher
    {
        void OpenFolder(string path);
        void OpenUrl(string url);
    }
}

namespace Momoka.Shell.Services
{
    internal sealed class LocalLibraryCoordinator
    {
        internal List<LocalMediaLibrarySettings> Configurations { get; } = [];
        internal List<LocalMediaLibraryEntry> Entries { get; } = [];
        public IReadOnlyList<LocalMediaLibrarySettings> Libraries => Configurations;
        public IReadOnlyList<LocalMediaLibraryEntry> EntriesFor(string id) => Entries.Where(entry => entry.LibraryId == id).ToArray();
        public event EventHandler? Changed;
        public string StatusFor(string id) => "后台监控已开启";
        internal Func<string, Task>? Refresh { get; set; }
        public Task RefreshAsync(string id) => Refresh?.Invoke(id) ?? Task.CompletedTask;
        internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        internal int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    }
}
