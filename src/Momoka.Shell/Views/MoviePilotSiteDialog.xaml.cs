using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSiteDialog : ContentDialog
{
    private XamlRoot? _observedRoot;
    private readonly bool _searchOnConfirm;
    private readonly MoviePilotSitePickerViewModel _owner;

    public MoviePilotSitePickerViewModel ViewModel { get; }
    public string ConfirmText => _searchOnConfirm ? "搜索" : "完成";
    public bool CanConfirm => _searchOnConfirm ? ViewModel.CanSearch : ViewModel.CanComplete;
    internal ItemsRepeater SiteGrid => SiteChoices;

    internal MoviePilotSiteDialog(MoviePilotSitePickerViewModel owner, bool searchOnConfirm)
    {
        _searchOnConfirm = searchOnConfirm;
        _owner = owner;
        ViewModel = owner.CreateDraft();
        InitializeComponent();
        // 根属性上的 StaticResource 早于本表 Resources 求值，拿不到合并进来的确认按钮样式，改在这里接。
        if (Resources.MergedDictionaries[0].TryGetValue("SiteDialogConfirmStyle", out var style) && style is Style confirm)
            PrimaryButtonStyle = confirm;
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        ViewModel.PropertyChanged += OnStateChanged;
        _owner.PropertyChanged += OnStateChanged;
        _observedRoot = XamlRoot;
        if (_observedRoot is not null) _observedRoot.Changed += OnRootChanged;
        ResizeForRoot();
        // 框架模板给按钮写了本地 Stretch，会压过样式的 Right；不换模板，只调整实际部件。
        if (DialogDismiss.TemplatePart(this, "PrimaryButton") is Button primary)
            primary.HorizontalAlignment = HorizontalAlignment.Right;
        _ = ViewModel.EnsureLoadedAsync();
        IsPrimaryButtonEnabled = CanConfirm;
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.PropertyChanged -= OnStateChanged;
        _owner.PropertyChanged -= OnStateChanged;
        if (_observedRoot is not null) _observedRoot.Changed -= OnRootChanged;
        _observedRoot = null;
        ViewModel.Cancel();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs args) => IsPrimaryButtonEnabled = CanConfirm;
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeForRoot();

    private void ResizeForRoot()
    {
        if (XamlRoot is not { } root) return;
        // 包括框架正文/外侧的留白；宽度再小也不把一张卡撑出窗口。
        SiteDialogBody.Width = Math.Min(580, Math.Max(0, root.Size.Width - 96));
        // 框架 Title 默认左对齐，不显式给宽度时 X 会贴着标题而不是顶栏右端。
        SiteDialogHeader.Width = SiteDialogBody.Width;
        SiteDialogScroll.MaxHeight = Math.Min(420, Math.Max(80, root.Size.Height - 224));
    }

    private void OnGridSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // 168 DIP 起步：宽窗三列，窄窗二列/一列，最后一行不拉成整排宽卡。
        SiteGridLayout.MinItemWidth = Math.Min(168, Math.Max(1, args.NewSize.Width));
    }

    private void OnConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = !CanConfirm || !ViewModel.CommitSelection();
    }

    private void OnClose(object sender, RoutedEventArgs args) => Hide();
}
