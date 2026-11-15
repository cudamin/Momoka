using System.Collections;
using System.Collections.Specialized;
using System.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>详情页的版本、音频和字幕选择；平台按钮与浮层负责输入、焦点和边界回退。</summary>
public sealed class DetailFilePicker : DropDownButton
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(DetailFilePicker), new PropertyMetadata(null, OnItemsSourceChanged));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(DetailFilePicker), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DetailFilePicker), new PropertyMetadata(string.Empty, OnPresentationChanged));
    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(DetailFilePicker), new PropertyMetadata("无音轨", OnPresentationChanged));
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(DetailFilePicker), new PropertyMetadata("Text", OnDisplayMemberPathChanged));

    private static readonly (string Key, byte Alpha)[] ListStateBrushes =
    [
        ("ListViewItemBackgroundPointerOver", 15),
        ("ListViewItemBackgroundPressed", 10),
        ("ListViewItemBackgroundSelected", 15),
        ("ListViewItemBackgroundSelectedPointerOver", 10),
        ("ListViewItemBackgroundSelectedPressed", 15),
        ("ListViewItemBackgroundSelectedDisabled", 15)
    ];

    private readonly Flyout _pickerFlyout;
    private readonly Border _flyoutContent;
    private readonly PickerListView _itemsList;
    private readonly CollectionViewSource _groupedItems = new() { IsSourceGrouped = true };
    private readonly TextBlock _labelText;
    private readonly TextBlock _valueText;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _systemColors = new();
    private readonly Dictionary<string, SolidColorBrush> _listBrushes = [];
    private IReadOnlyList<object> _items = [];
    private IReadOnlyList<object> _openItems = [];
    private object? _openSource;
    private INotifyCollectionChanged? _observedCollection;
    private bool _observing;
    private bool _synchronizing;
    private bool _isOpen;
    private bool _closing;
    private bool _pendingOpen;
    private int _openRequestVersion;
    private long _visibilityToken;
    private long _enabledToken;
    private int _lifetimeVersion;
    private string? _generatedAutomationName;

    public DetailFilePicker()
    {
        DefaultStyleKey = typeof(DropDownButton);
        // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e:
        // Samples/DropDownButton/DropDownButtonPage.xaml + .xaml.cs and
        // Samples/Flyout/FlyoutPage.xaml + .xaml.cs keep the native button/Flyout and Hide path.
        // The Gallery has no grouped file-picker sample; combine its ListView grouped-header
        // example below without copying experimental dependencies or the old ComboBox popup.
        var content = (Grid)XamlReader.Load("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" HorizontalAlignment="Stretch">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border x:Name="PickerLabelSurface" Padding="6,0,6,0"
                        Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                    <TextBlock x:Name="PickerLabel" VerticalAlignment="Center" />
                </Border>
                <Border x:Name="PickerSeparator" Grid.Column="1" Width="1" Height="16" Margin="0,0,8,0"
                        Background="{ThemeResource ControlStrokeColorDefaultBrush}" />
                <TextBlock x:Name="PickerValue" Grid.Column="2" VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis" />
            </Grid>
            """);
        _labelText = (TextBlock)content.FindName("PickerLabel");
        _valueText = (TextBlock)content.FindName("PickerValue");
        foreach (var text in new[] { _labelText, _valueText })
        {
            text.SetBinding(TextBlock.ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            text.SetBinding(TextBlock.FontSizeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(FontSize)) });
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
        }
        Content = content;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        _itemsList = new PickerListView(this)
        {
            Name = "PickerItemsList",
            SelectionMode = ListViewSelectionMode.Single,
            SingleSelectionFollowsFocus = false,
            IsItemClickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 420
        };
        // Copy the CollectionViewSource/GroupStyle/non-sticky ItemsStackPanel structure from
        // Gallery Samples/ListView/ListViewPage.xaml + .xaml.cs at the commit above.
        // Only data, caption size and lifecycle ownership are adapted for this short menu.
        _itemsList.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""
            <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ItemsStackPanel AreStickyGroupHeadersEnabled="False" />
            </ItemsPanelTemplate>
            """);
        _itemsList.GroupStyle.Add(new GroupStyle
        {
            HidesIfEmpty = true,
            HeaderTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Border Margin="12,8,12,4" AutomationProperties.AccessibilityView="Raw">
                        <TextBlock AutomationProperties.AccessibilityView="Raw"
                                   FontSize="{ThemeResource EgCaptionFontSize}" FontWeight="SemiBold" Text="{Binding Key}" />
                    </Border>
                </DataTemplate>
                """)
        });
        _itemsList.SelectionChanged += OnListSelectionChanged;
        _itemsList.ItemClick += OnListItemClick;
        _itemsList.AddHandler(KeyDownEvent, new KeyEventHandler(OnListKeyDown), true);

        _flyoutContent = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Name="PickerFlyoutContent" Padding="4" BorderThickness="1" CornerRadius="8"
                    Background="{ThemeResource ComboBoxDropDownBackground}"
                    BorderBrush="{ThemeResource ComboBoxDropDownBorderBrush}" />
            """);
        _flyoutContent.Child = _itemsList;
        _pickerFlyout = new Flyout
        {
            Content = _flyoutContent,
            Placement = FlyoutPlacementMode.LeftEdgeAlignedTop,
            ShouldConstrainToRootBounds = true,
            FlyoutPresenterStyle = (Style)XamlReader.Load("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="FlyoutPresenter">
                    <Setter Property="Background" Value="Transparent" />
                    <Setter Property="BorderThickness" Value="0" />
                    <Setter Property="Padding" Value="0" />
                </Style>
                """)
        };
        _pickerFlyout.Opening += OnFlyoutOpening;
        _pickerFlyout.Opened += OnFlyoutOpened;
        _pickerFlyout.Closing += OnFlyoutClosing;
        _pickerFlyout.Closed += OnFlyoutClosed;
        Click += OnPickerClick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        foreach (var (key, alpha) in ListStateBrushes)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            _listBrushes.Add(key, brush);
            _itemsList.Resources[key] = brush;
        }
        UpdateListStateBrushes();
        UpdateItemTemplate();
        RefreshItems();
    }

    public object? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string Label { get => GetValue(LabelProperty) as string ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty) as string ?? "无音轨"; set => SetValue(EmptyTextProperty, value); }
    public string DisplayMemberPath { get => GetValue(DisplayMemberPathProperty) as string ?? "Text"; set => SetValue(DisplayMemberPathProperty, value); }
    public IReadOnlyList<object> Items => _items;

    // An automatic track row with no real stream is a placeholder, not an audio menu.
    // TrackRow.Disable means "不使用字幕", which remains an available, real choice.
    public bool HasItems => _items.Any(item => IsItemAvailable(item)
        && item is not TrackRow { Stream: null, Disable: false });
    public Flyout PickerFlyout => _pickerFlyout;
    public FrameworkElement FlyoutContent => _flyoutContent;
    public ListView ItemsList => _itemsList;
    // DetailPage owns the existing system-appearance observation for all six pickers.
    // A page refresh must not repaint a picker after its view lifetime has ended.
    public void RefreshSystemAppearance()
    {
        if (_observing && IsLoaded) UpdateListStateBrushes();
    }
    public bool IsDropDownOpen
    {
        get => _isOpen;
        set
        {
            if (!value) ClosePicker();
            else if (!_isOpen && CanOpen()) RequestOpen();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DetailFilePicker)sender;
        picker.ObserveCollection();
        picker.RefreshItems();
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DetailFilePicker)sender).RefreshItems();

    private static void OnDisplayMemberPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DetailFilePicker)sender;
        picker.UpdateItemTemplate();
        picker.RefreshItems();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_observing) return;
        _observing = true;
        _lifetimeVersion++;
        _visibilityToken = RegisterPropertyChangedCallback(VisibilityProperty, OnAvailabilityChanged);
        _enabledToken = RegisterPropertyChangedCallback(IsEnabledProperty, OnAvailabilityChanged);
        ObserveCollection();
        RefreshItems();
        UpdateListStateBrushes();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_observing) return;
        _observing = false;
        _lifetimeVersion++;
        UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityToken);
        UnregisterPropertyChangedCallback(IsEnabledProperty, _enabledToken);
        ObserveCollection();
        ClosePicker();
        Flyout = null;
    }

    private void ObserveCollection()
    {
        if (_observedCollection is { } previous) previous.CollectionChanged -= OnCollectionChanged;
        _observedCollection = _observing ? ItemsSource as INotifyCollectionChanged : null;
        if (_observedCollection is { } current) current.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_observing && ReferenceEquals(sender, _observedCollection)) RefreshItems();
    }

    private void OnAvailabilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (Visibility != Visibility.Visible || !IsEnabled) ClosePicker();
        Flyout = CanOpen() ? _pickerFlyout : null;
    }

    private void RefreshItems()
    {
        ClosePicker();
        _items = ItemsSource is IEnumerable source ? source.Cast<object?>().OfType<object>().ToArray() : [];
        _labelText.Text = Label;
        var value = HasItems && SelectedItem is { } selected && ContainsReference(_items, selected)
            ? DisplayText(selected) : EmptyText;
        _valueText.Text = value;
        ToolTipService.SetToolTip(this, value);
        var automationName = AutomationProperties.GetName(this);
        if (string.IsNullOrEmpty(automationName) || automationName == _generatedAutomationName)
        {
            _generatedAutomationName = $"{Label} {value}".Trim();
            AutomationProperties.SetName(this, _generatedAutomationName);
        }
        AutomationProperties.SetName(_itemsList, Label);
        BuildGroups();
        Flyout = CanOpen() ? _pickerFlyout : null;
    }

    private void UpdateItemTemplate()
    {
        var path = SecurityElement.Escape(DisplayMemberPath);
        _itemsList.ItemTemplate = (DataTemplate)XamlReader.Load($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Margin="0,5,0,5" Text="{Binding {{path}}}" TextWrapping="Wrap"
                           FontSize="{ThemeResource EgBodyFontSize}" />
            </DataTemplate>
            """);
    }

    private void BuildGroups()
    {
        var current = SelectedItem is { } selected && ContainsReference(_items, selected) ? selected : null;
        _synchronizing = true;
        try
        {
            _groupedItems.Source = new[]
            {
                new PickerGroup($"当前{Label}", current is null ? [] : [current]),
                new PickerGroup($"其他{Label}", _items.Where(item => !ReferenceEquals(item, current)))
            };
            _itemsList.ItemsSource = _groupedItems.View;
            _itemsList.SelectedItem = current;
        }
        finally { _synchronizing = false; }
    }

    private bool CanOpen()
    {
        if (!_observing || !IsLoaded || !IsEnabled || !HasItems) return false;
        for (DependencyObject? element = this; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is UIElement { Visibility: Visibility.Collapsed } or Control { IsEnabled: false }) return false;
        }
        return true;
    }

    private void OnFlyoutOpening(object? sender, object args)
    {
        if (!CanOpen())
        {
            // Hide also cancels an opening before Popup.IsOpen becomes true.
            _pickerFlyout.Hide();
            return;
        }
        _closing = false;
        _pendingOpen = false;
        BuildGroups();
        UpdateListStateBrushes();
        var maximum = XamlRoot is { } root ? Math.Max(1, Math.Min(480, root.Size.Width - 24)) : 480;
        _flyoutContent.MaxWidth = maximum;
        _flyoutContent.MinWidth = Math.Min(ActualWidth, maximum);
        _openItems = _items;
        _openSource = ItemsSource;
        _isOpen = true;
    }

    private void OnFlyoutOpened(object? sender, object args)
    {
        if (!_isOpen || !CanOpen() || !ReferenceEquals(_openSource, ItemsSource))
        {
            ClosePicker();
            return;
        }
        _itemsList.UpdateLayout();
        var focusItem = SelectedItem is { } selected && IsItemAvailable(selected) && ContainsReference(_openItems, selected)
            ? selected : _openItems.FirstOrDefault(IsItemAvailable);
        if (focusItem is not null && _itemsList.ContainerFromItem(focusItem) is ListViewItem container)
            container.Focus(FocusState.Programmatic);
        else _itemsList.Focus(FocusState.Programmatic);
    }

    private void OnFlyoutClosing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        // Hide can cancel Opening before a presenter exists; that path has no Closed event.
        // Only an actually open popup needs the presenter-teardown wait before reopening.
        _closing |= _pickerFlyout.IsOpen;
        InvalidateOpenItems();
    }

    private void OnFlyoutClosed(object? sender, object args)
    {
        // Native Closed is raised during presenter teardown before its open-flyout metadata
        // is cleared. Queue same-target reopen to the next UI turn, then recheck ownership.
        // Supporting source: microsoft-ui-xaml 2a73274618c3f1042ee617dca5f64be782f93261,
        // dxaml/xcp/dxaml/lib/FlyoutBase_partial.cpp CheckAndHandleOpenFlyout/OnPresenterUnloaded.
        if (_pickerFlyout.IsOpen && !_closing) return;
        _closing = false;
        InvalidateOpenItems();
        if (!_pendingOpen) return;
        var request = _openRequestVersion;
        var lifetime = _lifetimeVersion;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (!_pendingOpen || request != _openRequestVersion || lifetime != _lifetimeVersion || !CanOpen()) return;
            _pendingOpen = false;
            RequestOpen();
        })) _pendingOpen = false;
    }

    private void RequestOpen()
    {
        _openRequestVersion++;
        if (_closing)
        {
            _pendingOpen = true;
            return;
        }
        _pendingOpen = false;
        _pickerFlyout.ShowAt(this);
    }

    private void OnPickerClick(object sender, RoutedEventArgs args)
    {
        // Button_Partial.cpp OnClick raises Click before OpenAssociatedFlyout calls ShowAt.
        // The native same-target call is ignored while old presenter metadata is closing;
        // remember this click without replacing the button's normal opening behavior.
        if (_closing && CanOpen()) RequestOpen();
    }

    private void InvalidateOpenItems()
    {
        _isOpen = false;
        _openItems = [];
        _openSource = null;
    }

    private void ClosePicker()
    {
        _pendingOpen = false;
        _openRequestVersion++;
        var wasOpen = _isOpen;
        InvalidateOpenItems();
        if (_pickerFlyout.IsOpen || wasOpen) _pickerFlyout.Hide();
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // The native single-selection provider (including UIA Select) shares the click commit.
        // Opening preselection is guarded; SingleSelectionFollowsFocus=false keeps arrows inert.
        if (!_synchronizing && args.AddedItems.Count > 0) TryCommit(args.AddedItems[0]);
    }

    private void OnListItemClick(object sender, ItemClickEventArgs args) => TryCommit(args.ClickedItem);

    private void OnListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            ClosePicker();
            args.Handled = true;
        }
        else if (args.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            var focused = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
            while (focused is not null && focused is not ListViewItem && !ReferenceEquals(focused, _itemsList))
                focused = VisualTreeHelper.GetParent(focused);
            var item = focused is ListViewItem container ? _itemsList.ItemFromContainer(container) : _itemsList.SelectedItem;
            TryCommit(item);
            args.Handled = true;
        }
    }

    private void TryCommit(object? item)
    {
        if (!_isOpen || !CanOpen() || item is null || !IsItemAvailable(item)
            || !ReferenceEquals(_openSource, ItemsSource) || !ReferenceEquals(_openItems, _items)
            || !ContainsReference(_openItems, item) || !ContainsReference(_items, item)
            || _itemsList.ContainerFromItem(item) is Control { IsEnabled: false }) return;
        // End ownership before writing the DP: selecting a source synchronously rebuilds both
        // track collections, so old routed selection/click events must already be harmless.
        ClosePicker();
        if (!ReferenceEquals(SelectedItem, item)) SelectedItem = item;
    }

    private string DisplayText(object item) => DisplayMemberPath == "Text" ? item switch
    {
        SourceRow source => source.Text,
        TrackRow track => track.Text,
        _ => item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty
    } : string.IsNullOrEmpty(DisplayMemberPath) ? item.ToString() ?? string.Empty
        : item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item)?.ToString() ?? string.Empty;

    private static bool ContainsReference(IEnumerable<object> items, object item) => items.Any(candidate => ReferenceEquals(candidate, item));
    private static bool IsItemAvailable(object item) => item is not TrackRow { IsAvailable: false } && item is not Control { IsEnabled: false };

    private void UpdateListStateBrushes()
    {
        var highContrast = _accessibility.HighContrast;
        var highlight = highContrast ? _systemColors.UIElementColor(UIElementType.Highlight) : default;
        var window = highContrast ? _systemColors.UIElementColor(UIElementType.Window) : default;
        foreach (var (key, alpha) in ListStateBrushes)
        {
            _listBrushes[key].Color = highContrast
                ? key == "ListViewItemBackgroundSelectedDisabled" ? window : highlight
                : Color.FromArgb(alpha, 255, 255, 255);
        }
    }

    // Adapted from Gallery ListViewPage.xaml.cs GroupInfoList: items stay the actual VM rows.
    public sealed class PickerGroup(string key, IEnumerable<object> items) : List<object>(items)
    {
        public string Key { get; } = key;
    }

    private sealed class PickerListView(DetailFilePicker owner) : ListView
    {
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is not ListViewItem container) return;
            container.IsEnabled = IsItemAvailable(item);
            AutomationProperties.SetName(container, owner.DisplayText(item));
        }
    }
}
