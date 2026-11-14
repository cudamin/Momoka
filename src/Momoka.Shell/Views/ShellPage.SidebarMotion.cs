using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private readonly AnimatedSettingsVisualSource _sidebarSettingsIconSource = new();
    private readonly UISettings _sidebarSettings = new();
    private TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _sidebarSettingsChanged;
    private bool _sidebarMotionActive;
    private bool _sidebarMotionWatching;
    private int _sidebarMotionLifetime;

    private void AttachSidebarMotion()
    {
        if (_sidebarMotionActive)
        {
            SyncSidebarSettingsIcon();
            return;
        }

        _sidebarMotionActive = true;
        var lifetime = ++_sidebarMotionLifetime;
        HomeMotion.PreferenceChanged += OnSidebarMotionPreferenceChanged;
        _sidebarSettingsChanged = (sender, args) => OnSidebarSettingsChanged(sender, args, lifetime);
        try
        {
            _sidebarSettings.AnimationsEnabledChanged += _sidebarSettingsChanged;
            _sidebarMotionWatching = true;
        }
        catch (Exception)
        {
            // 平台拒绝观察时无法保证继续收到减少动画通知，停在静态图标；不阻断外壳 Loaded。
            _sidebarMotionWatching = false;
            try { _sidebarSettings.AnimationsEnabledChanged -= _sidebarSettingsChanged; }
            catch (Exception) { /* 未知原生订阅由 lifetime 拒绝；不能声称已释放 native。 */ }
        }
        SyncSidebarSettingsIcon();
    }

    private void DetachSidebarMotion()
    {
        _sidebarMotionActive = false;
        _sidebarMotionWatching = false;
        ++_sidebarMotionLifetime;
        HomeMotion.PreferenceChanged -= OnSidebarMotionPreferenceChanged;
        if (_sidebarSettingsChanged is not null)
        {
            try { _sidebarSettings.AnimationsEnabledChanged -= _sidebarSettingsChanged; }
            catch (Exception) { /* 生命周期已撤销，不允许晚到原生通知重新绑定 Source。 */ }
            _sidebarSettingsChanged = null;
        }
        SyncSidebarSettingsIcon();
    }

    /// <summary>
    /// Gallery 6de91feccadfbb0a796b71caedead5a2284f730e，Samples/AnimatedIcon/AnimatedIconPage.xaml：
    /// NavigationViewItem 驱动 AnimatedIcon 状态，FontIconSource 提供静态回退。
    /// 本地只补应用/Windows 偏好与离树收尾，保留 XAML 原有语义。
    /// </summary>
    private void SyncSidebarSettingsIcon()
    {
        var source = _sidebarMotionActive && _sidebarMotionWatching && HomeMotion.AnimationsEnabled ? _sidebarSettingsIconSource : null;
        if (!ReferenceEquals(SidebarSettingsIcon.Source, source)) SidebarSettingsIcon.Source = source;
    }

    private void OnSidebarMotionPreferenceChanged() => SyncSidebarSettingsIcon();

    private void OnSidebarSettingsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args, int lifetime)
    {
        if (!_sidebarMotionActive || lifetime != _sidebarMotionLifetime) return;
        void ApplyCurrentPreference()
        {
            if (_sidebarMotionActive && lifetime == _sidebarMotionLifetime) SyncSidebarSettingsIcon();
        }
        if (DispatcherQueue.HasThreadAccess) ApplyCurrentPreference();
        else DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyCurrentPreference);
    }
}
