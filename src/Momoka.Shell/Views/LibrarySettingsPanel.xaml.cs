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

    /// <summary>面板上这一份字段表是给哪一页摆的；换页（比如切到「文件夹」）要重摆。</summary>
    private LibrarySection? _section;

    /// <summary>这一页是不是索引页、是哪一种（类型 / 播出平台 / 不是）。见 <see cref="ApplyIndexRows"/>。</summary>
    private LibraryIndexKind _index = LibraryIndexKind.None;

    /// <summary>
    /// 这一页属于哪种库（movies / tvshows / mixed）。同样叫「节目」，电影库与混合内容库的字段表并不是
    /// 同一份（电影二十二项、评分叫 IMDb；混合内容同文件夹那份、评分叫社区）。
    /// </summary>
    private string? _collectionType;

    /// <summary>
    /// 存下来的那份里、这一页没有开关的那几项（文件夹页的文件名、集页的集号在别的页就是这种）。
    /// 面板只改自己摆出来的那几行，这几项原样带回去 —— 不然在这一页改一处，别的页的勾选就被抹掉了。
    /// </summary>
    private IReadOnlyList<LibraryField> _carried = [];

    /// <summary>回填控件时按下去：那是「显示现在这一套」，不是用户改了东西。</summary>
    private bool _syncing;

    public LibrarySettingsPanel()
    {
        InitializeComponent();
        BuildViewOptions();
        BuildSizeOptions();
    }

    /// <summary>用户改了任意一处。递出去的是改完之后的一整份。</summary>
    public event Action<LibraryDisplay>? Changed;

    /// <summary>面板上那颗 ✕。宿主据此收起浮层。</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 把面板摆成这一套。<paramref name="section"/> 决定「显示字段」那一列摆哪几项 —— 文件夹那一页列的是
    /// 文件本身（文件名、容器、码率…），别的页列的是条目（类型、导演、播出平台…），网页端也是两套。
    /// <paramref name="index"/> 是「这一页是不是索引页、是哪一种」：类型与播出平台只有「标题」一项，
    /// 也没有「将项目分组到合集中」这回事（它们列的是类型名与台名，不是条目）。
    /// </summary>
    internal void Show(LibraryDisplay display, LibrarySection section, LibraryIndexKind index = LibraryIndexKind.None,
        string? collectionType = null)
    {
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        _syncing = true;
        try
        {
            _section = section;
            _index = index;
            _collectionType = collectionType;
            BuildFieldRows(section, display.View, index, collectionType);
            _carried = [.. display.Fields.Where(field => !LibraryDisplayCatalog.Applies(field, section, display.View, index, collectionType))];
            Select(ViewCombo, display.View);
            Select(SizeCombo, display.CardSize);
            GroupSwitch.IsOn = display.GroupIntoCollections;

            foreach (var (field, toggle) in _fieldToggles)
                toggle.IsOn = display.Fields.Contains(field);

            ApplyViewDependentRows(display.View);
            ApplyIndexRows();
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
        // 按全表顺序存，与视图模型里的 Normalized 同一把尺子：两处各排一次，才不会来回抖。
        // 这一页没有开关的那几项（_carried）原样带上，别的页的勾选不会因为在这里改了一处就丢。
        Fields = [.. _fieldToggles.Where(pair => pair.Value.IsOn).Select(pair => pair.Key)
            .Concat(_carried)
            .Distinct()
            .OrderBy(entry => LibraryDisplayCatalog.CanonicalOrder.ToList().IndexOf(entry))]
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
    /// 这一页的字段表：电视剧默认十四行；电影内容页与文件夹页二十二行（电影评分为 IMDb、文件夹与混合
    /// 内容为社区评分）；集页二十一项（表格视图多剧名与集号），两张索引页只有「标题」一行。
    /// 顺序就是网页端那一列，一行一个开关；开关的自动化名用中文名，读屏和探针都靠它认人。
    /// </summary>
    private void BuildFieldRows(LibrarySection section, LibraryView view, LibraryIndexKind index = LibraryIndexKind.None,
        string? collectionType = null)
    {
        FieldRows.Children.Clear();
        _fieldToggles.Clear();

        foreach (var info in LibraryDisplayCatalog.FieldsFor(section, view, index, collectionType))
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

    /// <summary>
    /// 索引页（类型、播出平台）少的那两样。
    /// <para>
    /// 「将项目分组到合集中」对它们没有意义 —— 那一页列的是类型名与台名，不是条目，没有合集可归。整组连说明
    /// 一起收起（见 CLAUDE.md 的显隐规矩：收起完整的无效区域，不留分隔线和空白）。
    /// </para>
    /// <para>
    /// **视图与图像大小留着**：类型的方卡与播出平台的宽卡各按那八档缩放，两页都认这两个设置（2026-10-18 起；
    /// 从前它们在索引页上完全无效，见 <c>LibraryPage.ApplyView</c> 里那段）。
    /// </para>
    /// </summary>
    private void ApplyIndexRows()
    {
        var index = _index != LibraryIndexKind.None;
        GroupSection.Visibility = index ? Visibility.Collapsed : Visibility.Visible;
        FieldsSection.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- 事件

    private void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;

        var next = Current;
        if (_section is { } section)
        {
            _syncing = true;
            try
            {
                BuildFieldRows(section, next.View, _index, _collectionType);
                _carried = [.. next.Fields.Where(field => !LibraryDisplayCatalog.Applies(field, section, next.View, _index, _collectionType))];
                foreach (var (field, toggle) in _fieldToggles) toggle.IsOn = next.Fields.Contains(field);
            }
            finally { _syncing = false; }
        }
        ApplyViewDependentRows(next.View);
        ApplyIndexRows();
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
