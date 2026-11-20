using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionEditor : UserControl
{
    public MoviePilotSubscriptionEditorViewModel ViewModel { get; } = new();
    private ContentDialog? _dialog;
    private Func<Task>? _remove;
    private bool _saved;
    private bool _narrow;
    private bool _syncingPath;
    public MoviePilotSubscriptionEditor()
    {
        InitializeComponent();
        // 站点列表是就地展开的，没有浮层那层「点外面自动关」；点字段与面板之外就收回去。
        // 用 handledEventsToo 是因为按钮一类的子控件会先把 PointerPressed 标掉。
        EditorRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnEditorPointerPressed), true);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.IntervalMode) or nameof(ViewModel.BestVersion)) LayoutFields();
            if (args.PropertyName == nameof(ViewModel.SavePath)) SyncPathSelection();
            if (args.PropertyName == nameof(ViewModel.SitesExpanded)) SyncSitesPanel();
            if (args.PropertyName == nameof(ViewModel.Working) && ViewModel.Working) ViewModel.SitesExpanded = false;
        };
        ViewModel.Paths.CollectionChanged += (_, _) => SyncPathSelection();
    }
    internal void Set(MoviePilotSubscription subscription, MoviePilotService? service = null)
    {
        ViewModel.Attach(subscription, service);
        LayoutFields();
        SyncPathSelection();
    }
    internal MoviePilotSubscriptionEdit Read() => ViewModel.Read();

    internal static async Task<MoviePilotSubscriptionEdit?> ShowAsync(FrameworkElement owner, MoviePilotSubscription subscription,
        MoviePilotService? service = null, Func<MoviePilotSubscriptionEdit, Task>? save = null, Func<Task>? remove = null)
    {
        if (owner.XamlRoot is null) return null;
        var editor = new MoviePilotSubscriptionEditor();
        editor.Set(subscription, service);
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.Children.Add(new FontIcon { Glyph = "\uE9D5" });
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = "编辑订阅", Style = (Style)Application.Current.Resources["EgSectionTitleStyle"] });
        heading.Children.Add(new TextBlock { Text = subscription.Title, Style = (Style)Application.Current.Resources["EgCaptionStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(heading, 1); title.Children.Add(heading);
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            RequestedTheme = owner.ActualTheme,
            Title = title,
            Content = editor,
            PrimaryButtonText = "保存",
            CloseButtonText = "关闭",
            SecondaryButtonText = remove is null ? "" : "取消订阅",
            DefaultButton = ContentDialogButton.None
        };
        dialog.Resources["ContentDialogMaxWidth"] = 740d;
        // 2026-11-19 用户令三颗键形状统一：主、次和关闭同接 4 DIP 小圆角那一档（EgDialog*ButtonStyle），
        // 主次只靠底色、字色和字重区分；返回时仍走 40 的图标键，不在这排里。
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["EgDialogPrimaryButtonStyle"];
        foreach (var prefix in new[] { "AccentButtonBackground", "ToggleSwitchFillOn" })
        {
            dialog.Resources[prefix] = Application.Current.Resources["EgAccentBrush"];
            dialog.Resources[prefix + "PointerOver"] = Application.Current.Resources["EgAccentHoverBrush"];
            dialog.Resources[prefix + "Pressed"] = Application.Current.Resources["EgAccentPressedBrush"];
        }
        dialog.CloseButtonStyle = (Style)Application.Current.Resources["EgDialogSecondaryButtonStyle"];
        var removeStyle = new Style(typeof(Button)) { BasedOn = dialog.CloseButtonStyle };
        removeStyle.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.Resources["EgDangerBrush"]));
        dialog.SecondaryButtonStyle = removeStyle;
        var close = new Button { Content = new FontIcon { Glyph = "\uE8BB" }, Style = (Style)Application.Current.Resources["EgIconButtonStyle"] };
        AutomationProperties.SetAutomationId(close, "DismissSubscriptionEditor"); AutomationProperties.SetName(close, "关闭编辑订阅");
        close.Click += (_, _) => { if (!editor.ViewModel.Working) dialog.Hide(); };
        Grid.SetColumn(close, 2); title.Children.Add(close);
        editor._dialog = dialog; editor._remove = remove;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (editor.ViewModel.Working) return;
            var deferral = args.GetDeferral();
            try
            {
                var draft = editor.Read();
                editor.ViewModel.Working = true;
                if (save is not null) await save(draft);
                editor._saved = true;
                args.Cancel = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { editor.ViewModel.ReportSave(error); }
            finally { editor.ViewModel.Working = false; deferral.Complete(); }
        };
        dialog.SecondaryButtonClick += (_, args) => { args.Cancel = true; if (!editor.ViewModel.Working) editor.RemoveConfirmation.Visibility = Visibility.Visible; };
        dialog.Closing += (_, args) => { if (editor.ViewModel.Working) args.Cancel = true; };
        editor.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(editor.ViewModel.Working)) return;
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = !editor.ViewModel.Working;
            close.IsEnabled = !editor.ViewModel.Working;
        };
        var root = owner.XamlRoot;
        void Resize()
        {
            editor.Width = Math.Max(220, Math.Min(660, root.Size.Width - 100));
            title.Width = editor.Width;
            editor.EditorScroll.MaxHeight = Math.Max(120, Math.Min(680, root.Size.Height - 218));
        }
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        root.Changed += OnRootChanged;
        try
        {
            Resize();
            _ = editor.ViewModel.ReloadAsync();
            editor.Loaded += (_, _) => editor.SavePathInput.Text = editor.ViewModel.SavePath;
            await MoviePilotDialog.ShowAsync(dialog, service);
            return editor._saved ? editor.Read() : null;
        }
        finally { root.Changed -= OnRootChanged; editor.ViewModel.Cancel(); }
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (BasicFields is null || AdvancedFields is null) return;
        ViewModel.SitesExpanded = false;
        var advanced = sender.SelectedItem == AdvancedTab;
        BasicFields.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
        AdvancedFields.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        EditorScroll.ChangeView(null, 0, null);
    }
    private async void OnRetry(object sender, RoutedEventArgs args) => await ViewModel.ReloadAsync();
    private void OnClearFilters(object sender, RoutedEventArgs args) { foreach (var row in ViewModel.FilterGroups) row.Selected = false; }
    /// <summary>
    /// 展开态的两件收尾：箭头翻上来（字形转 180 度），以及把面板带进视野 —— 面板是就地撑开的，
    /// 字段贴着内容区底边时它会长在可视区外面。
    /// </summary>
    private void SyncSitesPanel()
    {
        SiteChevronRotation.Angle = ViewModel.SitesExpanded ? 180 : 0;
        if (!ViewModel.SitesExpanded) return;
        DispatcherQueue.TryEnqueue(() => SubscriptionSitesPanel.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true }));
    }
    private void OnEditorPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!ViewModel.SitesExpanded) return;
        if (args.OriginalSource is DependencyObject source
            && (Within(source, SubscriptionSitesInput) || Within(source, SubscriptionSitesPanel))) return;
        ViewModel.SitesExpanded = false;
    }
    private static bool Within(DependencyObject source, DependencyObject root)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }
    private void OnPathSelected(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingPath) return;
        if (SavePathInput.SelectedItem is not MoviePilotSubscriptionChoice choice) return;
        ViewModel.SavePath = choice.Value;
        SavePathInput.Text = choice.Value;
    }
    private void SyncPathSelection()
    {
        if (SavePathInput is null || _syncingPath) return;
        _syncingPath = true;
        try
        {
            SavePathInput.SelectedItem = ViewModel.Paths.FirstOrDefault(path => path.Value == ViewModel.SavePath);
            SavePathInput.Text = ViewModel.SavePath;
        }
        finally { _syncingPath = false; }
    }
    private void OnKeepSubscription(object sender, RoutedEventArgs args) => RemoveConfirmation.Visibility = Visibility.Collapsed;
    private async void OnConfirmRemove(object sender, RoutedEventArgs args)
    {
        if (_remove is null || ViewModel.Working) return;
        ViewModel.Working = true;
        var removed = false;
        try { await _remove(); removed = true; }
        catch (OperationCanceledException) { }
        catch (Exception error) { ViewModel.ReportSave(error); }
        finally { ViewModel.Working = false; }
        if (removed) _dialog?.Hide();
    }
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        _narrow = args.NewSize.Width < 560;
        LayoutFields();
    }
    private void LayoutFields()
    {
        if (UpgradeFields is null) return;
        Arrange(EpisodeFields, ViewModel.IsSeries ? [KeywordField, TotalField, StartField] : [KeywordField]);
        Arrange(QualityFields, [QualityField, ResolutionField, EffectField]);
        Arrange(IntervalFields, ViewModel.IntervalMode == 1 ? [IntervalModeField, IntervalHoursField] : [IntervalModeField]);
        Arrange(DestinationFields, [DownloaderField, SavePathField]);
        Arrange(UpgradeFields, ViewModel.IsSeries && ViewModel.BestVersion ? [UpgradeField, FullUpgradeField, ImdbField] : [UpgradeField, ImdbField]);
        Arrange(MatchFields, [IncludeField, ExcludeField]);
        Arrange(GroupFields, ViewModel.GroupVisibility == Visibility.Visible ? [GroupField, SeasonField] : [SeasonField]);
    }
    private void Arrange(Grid grid, FrameworkElement[] fields)
    {
        var columns = _narrow ? 1 : fields.Length;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (_narrow ? fields.Length : 1); i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < fields.Length; i++) { Grid.SetRow(fields[i], _narrow ? i : 0); Grid.SetColumn(fields[i], _narrow ? 0 : i); Grid.SetColumnSpan(fields[i], 1); }
    }
}
