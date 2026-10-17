using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>
/// 网页端媒体库「设置」面板。四组东西都在这里：视图、图像大小、将项目分组到合集中、显示字段。
/// <para>
/// **改一下就生效**，与网页端一致（那边每改一处就 saveValues + refreshItems，没有确定键）；面板只负责
/// 把当前这一套递给 <see cref="Changed"/>，重查与记忆由页面和视图模型做。
/// </para>
/// <para>
/// 面板不知道自己在哪个库：电视剧库和电影库拿到的字段表、默认值、能显示哪几行，都由调用方决定 ——
/// 两种库本来就不是同一套说法（剧集的「年份」是区间、时长是每集多长，电影的是一年、一整片）。
/// </para>
/// </summary>
public sealed partial class LibrarySettingsPanel : UserControl
{
    private readonly Dictionary<LibraryField, ToggleSwitch> _fieldToggles = [];

    /// <summary>回填控件时按下去：那是「显示现在这一套」，不是用户改了东西。</summary>
    private bool _syncing;

    public LibrarySettingsPanel()
    {
        InitializeComponent();
        BuildFieldRows();
        BuildViewOptions();
        BuildSizeOptions();
    }

    /// <summary>用户改了任意一处。递出去的是改完之后的一整份。</summary>
    public event Action<LibraryDisplay>? Changed;

    /// <summary>面板上那颗 ✕。宿主据此收起浮层。</summary>
    public event Action? CloseRequested;

    /// <summary>把面板摆成这一套。</summary>
    internal void Show(LibraryDisplay display)
    {
        _syncing = true;
        try
        {
            Select(ViewCombo, display.View);
            Select(SizeCombo, display.CardSize);
            GroupSwitch.IsOn = display.GroupIntoCollections;

            foreach (var (field, toggle) in _fieldToggles)
                toggle.IsOn = display.Fields.Contains(field);

            ApplyViewDependentRows(display.View);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>面板此刻拼出来的那一套。</summary>
    internal LibraryDisplay Current => new()
    {
        View = ViewCombo.SelectedItem is ComboBoxItem { Tag: LibraryView view } ? view : LibraryView.Poster,
        CardSize = SizeCombo.SelectedItem is ComboBoxItem { Tag: LibraryCardSize size } ? size : LibraryCardSize.Default,
        GroupIntoCollections = GroupSwitch.IsOn,
        Fields = [.. _fieldToggles.Where(pair => pair.Value.IsOn).Select(pair => pair.Key)
            .OrderBy(item => LibraryDisplayCatalog.Fields.ToList().FindIndex(info => info.Field == item))]
    };

    // ---------------------------------------------------------------- 供探针驱动

    /// <summary>把「视图」那一格换成这一档，走的是用户点下拉的同一条路。</summary>
    internal void SetView(LibraryView view) => Select(ViewCombo, view);

    /// <summary>把「图像大小」那一格换成这一档。</summary>
    internal void SetCardSize(LibraryCardSize size) => Select(SizeCombo, size);

    /// <summary>拨一格显示字段的开关。</summary>
    internal void SetField(LibraryField field, bool on)
    {
        if (_fieldToggles.TryGetValue(field, out var toggle)) toggle.IsOn = on;
    }

    /// <summary>拨「将项目分组到合集中」。</summary>
    internal void SetGroupIntoCollections(bool on) => GroupSwitch.IsOn = on;

    // ---------------------------------------------------------------- 构建

    private void BuildViewOptions()
    {
        foreach (var view in Enum.GetValues<LibraryView>())
        {
            var item = new ComboBoxItem { Content = LibraryDisplayCatalog.LabelOf(view), Tag = view };
            AutomationProperties.SetName(item, LibraryDisplayCatalog.LabelOf(view));
            ViewCombo.Items.Add(item);
        }
    }

    private void BuildSizeOptions()
    {
        foreach (var size in Enum.GetValues<LibraryCardSize>())
        {
            var item = new ComboBoxItem { Content = LibraryDisplayCatalog.LabelOf(size), Tag = size };
            AutomationProperties.SetName(item, LibraryDisplayCatalog.LabelOf(size));
            SizeCombo.Items.Add(item);
        }
    }

    /// <summary>
    /// 十四行显示字段。顺序就是网页端那一列，一行一个开关；开关的自动化名用「显示字段 + 中文名」，
    /// 读屏和探针都靠它认人。
    /// </summary>
    private void BuildFieldRows()
    {
        foreach (var info in LibraryDisplayCatalog.Fields)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = info.Label,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Application.Current.Resources.TryGetValue("EgBodyStyle", out var body) && body is Style bodyStyle)
                label.Style = bodyStyle;

            var toggle = new ToggleSwitch
            {
                MinWidth = 0,
                OffContent = "",
                OnContent = "",
                IsOn = false
            };
            AutomationProperties.SetName(toggle, info.Label);
            AutomationProperties.SetAutomationId(toggle, "SettingsField" + info.Field);
            Grid.SetColumn(toggle, 1);
            toggle.Toggled += OnFieldToggled;

            row.Children.Add(label);
            row.Children.Add(toggle);
            FieldRows.Children.Add(row);
            _fieldToggles[info.Field] = toggle;
        }
    }

    private static void Select(ComboBox combo, object tag)
    {
        for (var index = 0; index < combo.Items.Count; index++)
            if (combo.Items[index] is ComboBoxItem item && Equals(item.Tag, tag))
            {
                combo.SelectedIndex = index;
                return;
            }
    }

    /// <summary>
    /// 列表与表格没有「格子多大」这回事（网页端也一样，那两个视图里这一行是收起的）。
    /// </summary>
    private void ApplyViewDependentRows(LibraryView view) =>
        SizeSection.Visibility = view is LibraryView.List or LibraryView.Table
            ? Visibility.Collapsed
            : Visibility.Visible;

    // ---------------------------------------------------------------- 事件

    private void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;

        ApplyViewDependentRows(Current.View);
        Changed?.Invoke(Current);
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        Changed?.Invoke(Current);
    }

    private void OnGroupToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Changed?.Invoke(Current);
    }

    private void OnFieldToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Changed?.Invoke(Current);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
