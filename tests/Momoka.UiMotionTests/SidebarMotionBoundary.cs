// 只替代 WinUI 图标和外壳控件端点；生命周期与偏好逻辑原样链接生产 partial。
namespace Microsoft.UI.Xaml.Controls.AnimatedVisuals
{
    public sealed class AnimatedSettingsVisualSource;
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class AnimatedIcon
    {
        private object? _source;
        public int SourceWrites { get; private set; }
        public object? Source
        {
            get => _source;
            set { _source = value; SourceWrites++; }
        }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class ShellPage : Microsoft.UI.Xaml.FrameworkElement, IDisposable
    {
        private Microsoft.UI.Xaml.Controls.AnimatedIcon SidebarSettingsIcon { get; } = new();
        internal object? IconSource => SidebarSettingsIcon.Source;
        internal int IconSourceWrites => SidebarSettingsIcon.SourceWrites;
        internal void AttachMotion() => AttachSidebarMotion();
        internal void DetachMotion() => DetachSidebarMotion();
        public void Dispose() => DetachSidebarMotion();
    }
}
