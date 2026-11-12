using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 给原生 Image 接上 CardItem 的图片需求，不额外包一层卡片控件。
/// 数据绑定只负责显示 RowPoster；容器进树、换项和回收负责领取、读取与释放。
/// </summary>
public static class CardArtwork
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached(
        "Item", typeof(CardItem), typeof(CardArtwork), new PropertyMetadata(null, OnItemChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(CardArtwork), new PropertyMetadata(null));
    private static readonly DependencyProperty RealizedProperty = DependencyProperty.RegisterAttached(
        "Realized", typeof(bool), typeof(CardArtwork), new PropertyMetadata(true, OnRealizedChanged));

    public static CardItem? GetItem(DependencyObject element) => (CardItem?)element.GetValue(ItemProperty);
    public static void SetItem(DependencyObject element, CardItem? value) => element.SetValue(ItemProperty, value);

    // 回收池可把元素留在原Repeater里。XamlRoot仍在不能表示layout仍需要这项，
    // 显式realized状态独立于Item绑定，换项与幻影Loaded都不能重新抬起已回收的需求。
    internal static void SetRealized(Image image, bool value) => image.SetValue(RealizedProperty, value);

    private static void OnRealizedChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element.GetValue(StateProperty) is State state) state.ApplyRealization();
    }

    private static void OnItemChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Image image) return;
        var state = image.GetValue(StateProperty) as State;
        if (args.NewValue is not CardItem item)
        {
            state?.Dispose();
            image.ClearValue(StateProperty);
            return;
        }

        if (state is null)
        {
            state = new State(image);
            image.SetValue(StateProperty, state);
        }
        state.Use(item);
    }

    private sealed class State : IDisposable
    {
        private readonly Image _image;
        private CardItem? _item;
        private ImageArrivalFade? _arrival;

        internal State(Image image)
        {
            _image = image;
            image.Loaded += OnLoaded;
            image.Unloaded += OnUnloaded;
        }

        internal void Use(CardItem item)
        {
            if (ReferenceEquals(_item, item)) return;
            _arrival?.Stop();
            _item?.ReleasePoster(_image, keepWaiting: false);
            _item = item;
            Begin();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Begin();

        internal void ApplyRealization()
        {
            if (_image.GetValue(RealizedProperty) is true) Begin();
            else StopDemand();
        }

        private void Begin()
        {
            // 回收池会对仍在屏上的元素喊 Unloaded；XamlRoot 比一次性 Loaded 标记可靠。
            // 首次离树赋项也不能下载整页，真正进树之后才登记图片需求。
            if (_image.GetValue(RealizedProperty) is not true || _image.XamlRoot is null || _item is null) return;
            _arrival ??= new ImageArrivalFade(_image);
            _item.ClaimCard(_image);
            _ = _item.EnsurePosterAsync(BrowsePresentation.WaitForImagesAsync(_image));
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (_image.XamlRoot is not null) return;
            // 音乐 Image 真离树时停止保留晚到位图；重新进树会重新 Claim/Ensure 并同步复用缓存。
            // CardItem 自己判断当前持有者，旧容器的迟到释放不能清掉新容器的图。
            StopDemand();
        }

        private void StopDemand()
        {
            _item?.ReleasePoster(_image, keepWaiting: false);
            _arrival?.Dispose();
            _arrival = null;
        }

        public void Dispose()
        {
            _image.Loaded -= OnLoaded;
            _image.Unloaded -= OnUnloaded;
            StopDemand();
            _item = null;
        }
    }
}
