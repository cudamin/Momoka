using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Emby;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 详情页那几颗下拉（媒体源、音频、字幕、季）用的一层壳：把框架模板里那个弹层从「窗口化弹层」翻成
/// 「树内弹层」，好让调色板给浮层的那支应用内亚克力真的采得到它底下的那一页。
/// <para>
/// 用户 2026-09-27：「剧页面、电影页面、集页面，季页面，把音频和字幕的选择栏改成亚克力背景」。底下那件事
/// 不是没配过 —— <c>Palette.xaml</c> 里 <c>ComboBoxDropDownBackground</c> 从 2026-09-25 起就是一支
/// <c>AcrylicBrush</c>，两轮调浓淡（0.72/0.65 → 0.55/0.55）都没让它像玻璃。根因在框架那一侧，不在浓淡：
/// **框架的 ComboBox 把弹层设成窗口化的**（<c>ComboBox_Partial.cpp</c> 里
/// <c>m_tpPopupPart->put_ShouldConstrainToRootBounds(false)</c>），于是浮层开在自己那个窗口里，而
/// 应用内亚克力只采得到「同一个窗口里画在它后面的东西」（WinUI 团队在 microsoft-ui-xaml#9523 里的原话：
/// 「AcrylicBrush is only able to blur other content drawn by WinAppSDK in the same window. For windowed
/// popups … the AcrylicBrush can only blur content below it in that popup window.」）—— 那个窗口里除了浮层
/// 自己什么都没有，采不到就退回 <c>FallbackColor</c>，屏上因此是一块不透明的实心色。拿用户的截图量过：
/// 浮层底下压着 <c>#49363E</c> 那张粉紫色的剧照，浮层上是 <c>#20262D</c>（正是那支画笔的 FallbackColor），
/// 一点都没透上来。
/// </para>
/// <para>
/// 所以这一层壳的第一件事是把那颗弹层的 <see cref="Popup.ShouldConstrainToRootBounds"/> 设回 <c>true</c>。
/// 浮层于是回到<em>同一棵树</em>里渲染（不再开窗口），亚克力按它本来的意思工作 —— 模糊底下的页面、按调色板
/// 的 tint 上色，和详情页正文那几块玻璃（<c>EgFrostBrush</c>）是同一种材料、同一套参数。
/// </para>
/// <para>
/// 代价说清楚：树内弹层受窗口边界约束。窗口矮到装不下整张浮层时，框架会把浮层往上翻、必要时收窄它
/// （列表本来就在 <c>ScrollViewer</c> 里，收窄之后照样滚得到），而不是像窗口化弹层那样悬到窗口外面去。
/// 详情页这几颗下拉都长在页面里、离窗口边很远，实际看得见的差别只有「下拉不再越出窗口」这一条。
/// </para>
/// <para>
/// 第二件事是展开态的宽度（用户 2026-10-16：「选择栏展开后会触发自动收缩导致选择栏与右侧的组件重叠」，
/// 同日补一句「展开后自动调整选择框长度适配选择项中最长的那一条」）。**框架在弹层打开那一拍会把选中项的
/// 文本收掉**（模板的 DropDownOpened 态把 <c>ContentPresenter</c> 置零），框的期望宽度随即塌回
/// <see cref="FrameworkElement.MinWidth"/>；而弹层是按<em>最宽</em>那一条轨道名撑的。副屏离线探针实拍读数
/// （`detail-picker-open.txt`，电影页尾部那一排，长名音轨）：框 44–616 弹开时塌成 44–244、44–577 塌成
/// 44–244，弹层两次都是 44–602 —— 整排跟着重排，右边那一颗往左挪进弹层底下，屏上就是「选择栏与右侧的
/// 组件重叠」。所以展开这一拍把框的宽度钉到列表那一份的宽度上（取展开前那一拍的宽度与列表宽度里大的
/// 那个，免得选中最长那条时反被收窄；再按容器里剩下的地方封顶，免得把这一排顶出界）。收起时把
/// <c>Width</c> 清回去，闭合态照旧由内容（选中那条轨道名）撑。弹层自己也跟着封顶（2026-11-08）：框被栏宽
/// 压着的时候，弹层同样收到那个宽 —— 探出去的那一截会压在右边那张艺术图上。
/// </para>
/// <para>
/// 为什么是子类而不是改模板：模板那一份是框架的 <c>DefaultComboBoxStyle</c>，抄一份进仓库就等于自己养一份
/// 会随 SDK 过期的副本；而 <c>Popup</c> 部件是框架自己 <c>GetTemplateChild</c> 拿的那个（名字就叫
/// <c>Popup</c>），从子类的 <see cref="OnApplyTemplate"/> 里问它，正好是子类该干的事。基类在
/// <c>OnApplyTemplate</c> 里把它设成 false，所以这里要先 <c>base</c> 再改。
/// </para>
/// <para>
/// 当前调用者是详情页六颗文件选择框（<c>DetailPage.xaml</c>；宽版式三颗、尾部三颗）和
/// <c>AniBtSearchPanel.xaml</c> 的三颗筛选框。设置页仍使用框架 ComboBox；不因维护此扩展而扩大应用范围。
/// 季选择栏那一颗（原来挂在集列表牌子右端）2026-11-02 已按用户令删掉。
/// </para>
/// </summary>
public sealed class EgPicker : ComboBox
{
    /// <summary>
    /// 收起态那一拍的宽度。模板在弹层打开这一拍把选中项的文本收掉，框的宽度当场塌回
    /// <see cref="FrameworkElement.MinWidth"/>，所以要在这之前记下来 —— 展开态的宽度不许比它窄。
    /// <para>
    /// 记的是「收起时的每一次尺寸变化」，不是打开那一刻读一次：框架收文本与重排不是同步做完的，
    /// 打开那一拍读到的可能已经是塌下去的数。
    /// </para>
    /// </summary>
    private double _restingWidth;

    /// <summary>展开中的那一份弹层内容 —— 只在展开期间挂着，收起时解掉订阅并清掉封顶。</summary>
    private FrameworkElement? _dropDown;
    private int _dropDownVersion;
    private bool _widthPinned;

    /// <summary>模板里的闭合正文（ContentPresenter），收起态短名往它身上写。</summary>
    private ContentPresenter? _selectionPresenter;
    private int _selectionTextVersion;

    /// <summary>成员名的反射查表，键是（条目类型，成员名）；同一批轨道行反复选来选去时不必每回再问一次。</summary>
    private static readonly Dictionary<(Type Type, string Path), PropertyInfo?> MemberLookup = [];

    /// <summary>
    /// 收起态显示选中项的哪个成员（2026-11-16 用户令「没展开的时候这样显示」）。非空时，闭合框的正文取
    /// <see cref="Selector.SelectedItem"/> 的这个成员，展开列表和 <see cref="Selector.DisplayMemberPath"/> 都不动。
    /// 商店那一行用它把音频/字幕的闭合态换成 <c>TrackRow.ShortText</c>（完整轨道名去掉「自动」前缀），
    /// 列表里仍是带自动说明的那一份。详情页音频/字幕的展开列表自 2026-11-19 起是两行（第二行是轨道标题，
    /// 见 <c>TrackPickerRowTemplate</c>），那颗 32 高的闭合框靠它只取 <c>TrackRow.Text</c> 这一行。
    /// 空串（默认）时收起态照框架的 DisplayMemberPath／ItemTemplate 显示，其它页面的选择框完全不受影响。
    /// </summary>
    public static readonly DependencyProperty SelectionTextMemberPathProperty = DependencyProperty.Register(
        nameof(SelectionTextMemberPath), typeof(string), typeof(EgPicker),
        new PropertyMetadata(null, OnSelectionTextMemberPathChanged));

    public string? SelectionTextMemberPath
    {
        get => (string?)GetValue(SelectionTextMemberPathProperty);
        set => SetValue(SelectionTextMemberPathProperty, value);
    }

    private static void OnSelectionTextMemberPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (EgPicker)sender;
        if (string.IsNullOrEmpty(args.NewValue as string)) picker.RestoreSelectionText();
        else picker.QueueApplySelectionText();
    }

    public EgPicker()
    {
        SizeChanged += (_, e) =>
        {
            if (!IsDropDownOpen) _restingWidth = e.NewSize.Width;
        };
        // 列表那一份的宽度要等它自己布完才量得到，所以撑宽这一步排在下一个 UI 回合。
        DropDownOpened += (_, _) => QueueWidenToDropDown();
        DropDownClosed += (_, _) =>
        {
            RestoreDropDownWidths();
            // ComboBox_Partial.cpp, 693f472aeaf89c6dff66256ee6fcd453e5d553b8:
            // FinishClosingDropDown 在发 Closed 前会重写正文和两行列表模板；未换选中项也要恢复单行。
            QueueApplySelectionText();
        };
        Loaded += (_, _) =>
        {
            if (IsDropDownOpen) QueueWidenToDropDown();
            QueueApplySelectionText();
        };
        Unloaded += (_, _) => { if (!IsLoaded) RestoreDropDownWidths(); };
        // 框架每次换选中项都会把 ContentPresenter 的正文重写一遍，撑宽与换短名都排到它之后。
        SelectionChanged += (_, _) => QueueApplySelectionText();
    }

    /// <summary>
    /// 模板里那颗弹层，<see cref="OnApplyTemplate"/> 拿到之后留着给自检读（「浮层在树内」那一条）。
    /// 模板还没上树时是 null。
    /// </summary>
    internal Popup? PopupPart { get; private set; }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ComboBoxItem container)
            container.IsEnabled = item is not TrackRow { IsAvailable: false };
    }

    protected override void OnApplyTemplate()
    {
        // End the old popup's sizing ownership before the base replaces its template parts.
        RestoreDropDownWidths();
        PopupPart = null;
        _selectionPresenter = null;
        base.OnApplyTemplate();

        PopupPart = GetTemplateChild("Popup") as Popup;
        _selectionPresenter = GetTemplateChild("ContentPresenter") as ContentPresenter;
        QueueApplySelectionText();
        if (PopupPart is null) return;

        PopupPart.ShouldConstrainToRootBounds = true;
        if (IsDropDownOpen) QueueWidenToDropDown();
    }

    /// <summary>只让当前打开及模板所属的排队请求取得撑宽责任。</summary>
    private void QueueWidenToDropDown()
    {
        var version = ++_dropDownVersion;
        var popup = PopupPart;
        if (popup is null) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            // Closing, unloading, reapplying the template or another open notification ends
            // this request. An old queue item must not pin a closed/reopened picker again.
            if (version != _dropDownVersion || !IsLoaded || !IsDropDownOpen
                || !popup.IsOpen || !ReferenceEquals(PopupPart, popup)) return;
            WidenToDropDown();
        });
    }

    /// <summary>
    /// 展开态把框撑到列表那一份的宽度 —— 列表的宽度就是最宽那条轨道名撑出来的，框跟它齐平之后右邻才站得开。
    /// <para>
    /// 取「展开前那一拍的宽度」与「列表宽度」里大的那个：选中的正好是最长那条时，框自己的宽度（还带着那颗
    /// 下拉箭头）会比列表略宽，按列表收窄会让选中项的文本被截。再按容器里剩下的地方封顶：这一颗在宽版式里
    /// 站在片名那一栏的 <c>*</c> 格上，那格的宽就是它的上限，不封顶会把这一排顶出栏外（自检的
    /// <c>PickerFit</c> 读的就是这条）。
    /// </para>
    /// <para>
    /// 框被栏宽压着的时候，弹层自己也跟着封顶（2026-11-08）：它按最宽那条轨道名撑，会探出选择框右沿 —— 电影页
    /// 1280 上右上角那张艺术图占掉一栏之后实测探出 95 像素，探出去的那一截正压在那张画上（集页从 2026-09-12 起
    /// 一直是同一个处境，只是没有哪一条读数盯着它）。**不能只在展开那一拍量一次**：那一拍列表还是框那么宽，
    /// 条目实例化之后才撑开，所以这里挂 <see cref="FrameworkElement.SizeChanged"/> 一路盯着。
    /// </para>
    /// </summary>
    private void WidenToDropDown()
    {
        if (PopupPart?.Child is not FrameworkElement list) return;

        var room = RoomInParent();
        var width = Math.Min(Math.Max(_restingWidth, list.ActualWidth), room);
        if (width > 0)
        {
            Width = width;
            _widthPinned = true;
        }

        if (_dropDown is { } previous && !ReferenceEquals(previous, list))
        {
            previous.SizeChanged -= OnDropDownSizeChanged;
            previous.ClearValue(FrameworkElement.MaxWidthProperty);
        }
        _dropDown = list;
        list.SizeChanged -= OnDropDownSizeChanged;
        list.SizeChanged += OnDropDownSizeChanged;
        ClampDropDown();
    }

    /// <summary>弹层比这一颗宽了就收到这一颗的宽 —— 有地方的时候它本来就窄不过框，这一句什么都不改。</summary>
    private void ClampDropDown()
    {
        if (_dropDown is not { } list) return;

        var cap = Width;
        if (!double.IsFinite(cap) || cap <= 0) return;

        if (list.ActualWidth > cap + 0.5) list.MaxWidth = cap;
        else if (list.ActualWidth < cap - 0.5) list.ClearValue(FrameworkElement.MaxWidthProperty);
    }

    private void OnDropDownSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded && IsDropDownOpen && PopupPart is { IsOpen: true } popup
            && ReferenceEquals(sender, _dropDown) && ReferenceEquals(popup.Child, _dropDown)) ClampDropDown();
    }

    /// <summary>收起时把展开态钉下的宽度放回去 —— 下一次展开照旧由内容与容器重新撑。</summary>
    private void RestoreDropDownWidths()
    {
        _dropDownVersion++;
        if (_widthPinned)
        {
            ClearValue(WidthProperty);
            _widthPinned = false;
        }

        if (_dropDown is not { } list) return;
        list.SizeChanged -= OnDropDownSizeChanged;
        list.ClearValue(FrameworkElement.MaxWidthProperty);
        _dropDown = null;
    }

    /// <summary>
    /// 把闭合正文换成 <see cref="SelectionTextMemberPath"/> 指的那一份。框架换选中项时先自己写一遍
    /// <c>ContentPresenter.Content</c>，所以这一步排到下一个 UI 回合；没设短名的选择框直接不动。
    /// </summary>
    private void QueueApplySelectionText()
    {
        if (string.IsNullOrEmpty(SelectionTextMemberPath)) return;

        var version = ++_selectionTextVersion;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _selectionTextVersion) return;
            ApplySelectionText();
        });
    }

    private void ApplySelectionText()
    {
        if (_selectionPresenter is not { } presenter || string.IsNullOrEmpty(SelectionTextMemberPath)) return;

        presenter.Content = SelectedItem is { } item ? MemberText(item, SelectionTextMemberPath!) : null;
        presenter.ContentTemplate = null;
        presenter.ContentTemplateSelector = null;
    }

    /// <summary>
    /// 关掉短名时把闭合正文交回框架：控件只在选中项变化时自己重写，所以这里按 DisplayMemberPath 先写回一份，
    /// 否则收起框会停在上一次写进去的短名上。
    /// </summary>
    private void RestoreSelectionText()
    {
        if (_selectionPresenter is not { } presenter) return;

        if (SelectedItem is { } item && !string.IsNullOrEmpty(DisplayMemberPath))
        {
            presenter.Content = MemberText(item, DisplayMemberPath);
            presenter.ContentTemplate = null;
            presenter.ContentTemplateSelector = null;
            return;
        }

        presenter.ClearValue(ContentPresenter.ContentProperty);
        presenter.ClearValue(ContentPresenter.ContentTemplateProperty);
        presenter.ClearValue(ContentPresenter.ContentTemplateSelectorProperty);
    }

    /// <summary>读条目上某个公开成员的字；成员不在或值为空时退回条目自己的 ToString。</summary>
    private static string? MemberText(object item, string path)
    {
        var key = (item.GetType(), path);
        if (!MemberLookup.TryGetValue(key, out var property))
            MemberLookup[key] = property = item.GetType().GetProperty(path);
        return property?.GetValue(item)?.ToString() ?? item.ToString();
    }

    /// <summary>这一颗在它的父级里还剩多少横向地方；父级量不出来时不给上限（<c>WrapRow</c> 自己会换行）。</summary>
    private double RoomInParent()
    {
        if (Parent is not FrameworkElement parent || parent.ActualWidth <= 0) return double.PositiveInfinity;

        return parent.ActualWidth - TransformToVisual(parent).TransformPoint(new Point()).X;
    }
}
