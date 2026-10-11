using System.Collections.ObjectModel;
using System.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>
/// 搜索结果里的一张候选。图是异步取的（走服务器那条转发，见
/// <see cref="EmbyClient.GetRemoteSearchImageAsync"/>），所以这一格和「修改媒体封面图」那一格一样，先空着
/// 再填上 —— 取不到就留一个字形，不留一块空白。
/// </summary>
public sealed partial class IdentifyCandidate : INotifyPropertyChanged
{
    private BitmapImage? _picture;
    private int _generation;

    public IdentifyCandidate(RemoteSearchResult result)
    {
        Result = result;
        Title = result.Name is { Length: > 0 } name ? name : "（没有名字）";
        Subtitle = result.Describe();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RemoteSearchResult Result { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public BitmapImage? Picture
    {
        get => _picture;
        private set
        {
            if (ReferenceEquals(_picture, value)) return;
            _picture = value;
            Raise(nameof(Picture));
            Raise(nameof(HasPicture));
            Raise(nameof(EmptyMark));
        }
    }

    public Visibility HasPicture => _picture is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EmptyMark => _picture is null ? Visibility.Visible : Visibility.Collapsed;

    internal async Task LoadAsync(Func<RemoteSearchResult, Task<byte[]?>> fetch)
    {
        if (Result.ImageUrl is not { Length: > 0 }) return;
        var generation = ++_generation;
        try
        {
            var bytes = await fetch(Result).ConfigureAwait(true);
            if (generation != _generation || bytes is not { Length: > 0 }) return;
            var picture = await PosterLoader.DecodeAsync(bytes, 140).ConfigureAwait(true);
            if (generation == _generation) Picture = picture;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug("ui", $"识别结果里那张海报取不回来：{error.Message}");
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 「识别」那张表。子面板自己按需要问服务器，服务器能力全部由固定身份的委托传入（同
/// <see cref="CoverDialog"/>）—— 这里不持有会话，也就不可能拿着一个已经换了主人的身份去发请求。
/// <para>
/// 表单上的字、哪几种条目能识别、什么时候不该问，都是 Core 里 <see cref="ItemIdentify"/> 的事；这一头只管
/// 把结果摆出来、把用户按下的那一下交给调用方。
/// </para>
/// <para>
/// 同一张表里换四段（见 <see cref="ShowStage"/>），一次只露该露的两颗按钮 —— 正文比窗口高的时候，正文滚、
/// 按钮不滚。
/// </para>
/// </summary>
public sealed partial class IdentifyDialog : ContentDialog
{
    private const string Category = "ui";

    /// <summary>这张表这一刻在哪一段。哪几颗按钮露面、正文露哪一块，全看它。</summary>
    private enum Stage
    {
        /// <summary>填条件。</summary>
        Form,

        /// <summary>候选摆出来了，等挑一条。</summary>
        Results,

        /// <summary>挑好了，等按「识别」落下去。</summary>
        Apply,

        /// <summary>「移除识别」那一句问话。</summary>
        Confirm
    }

    private readonly EmbyItem _item;
    private readonly Func<Task<List<ExternalIdInfo>>> _fields;
    private readonly Func<RemoteSearchQuery, Task<List<RemoteSearchResult>>> _search;
    private readonly Func<RemoteSearchResult, Task<byte[]?>> _picture;
    private readonly Func<RemoteSearchResult, bool, Task> _apply;
    private readonly Func<Task> _remove;
    private readonly List<(string Key, TextBox Box)> _idBoxes = [];
    private IdentifyCandidate? _chosen;

    /// <summary>这一趟里改过服务器上的东西没有。调用方据此决定要不要重读那一页。</summary>
    public bool Touched { get; private set; }

    /// <summary>稳定的一份：绑定到 <c>ItemsSource</c> 的集合对象原地增删，不整份换掉。</summary>
    public ObservableCollection<IdentifyCandidate> Candidates { get; } = [];

    /// <param name="path">
    /// 服务器上这个条目背后的文件，网页端那张表顶上「路径」那一行就是它。调用方先按 id 问过一遍才拿得到
    /// （列表接口回来的卡片不带路径），没有就传空 —— 那一行整段不出现。
    /// </param>
    public IdentifyDialog(
        EmbyItem item,
        string? path,
        Func<Task<List<ExternalIdInfo>>> fields,
        Func<RemoteSearchQuery, Task<List<RemoteSearchResult>>> search,
        Func<RemoteSearchResult, Task<byte[]?>> picture,
        Func<RemoteSearchResult, bool, Task> apply,
        Func<Task> remove)
    {
        InitializeComponent();

        // 对话框摆在 XamlRoot 的浮层根上，从提出它的那棵树上继承不到主题 —— 不点明就按 Windows 的应用模式来。
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        _item = item;
        _fields = fields;
        _search = search;
        _picture = picture;
        _apply = apply;
        _remove = remove;

        // 标题里带上片名：开着这张表的时候，菜单早没了，而一屏「某某片子对哪一条记录」是很容易认错的。
        Title = $"识别 — {item.Name}";

        if (path is { Length: > 0 })
        {
            PathRow.Visibility = Visibility.Visible;
            PathText.Text = path;
        }

        ShowStage(Stage.Form);
        _ = LoadFieldsAsync();
    }

    /// <summary>问这个条目能填哪几家的标识符，按服务器答的那几家摆出输入框（每家下面挂它自己的网址）。</summary>
    private async Task LoadFieldsAsync()
    {
        try
        {
            var fields = await _fields().ConfigureAwait(true);
            foreach (var info in fields)
            {
                var column = new StackPanel { Spacing = Space("EgSpaceXS") };

                var box = new TextBox { Header = ItemIdentify.FieldLabel(info.Name) };
                column.Children.Add(box);
                _idBoxes.Add((info.Key, box));

                // 网址那一行照网页端来：点了开系统浏览器。服务器没给网址的那几家（豆瓣有时不给）就只有输入框。
                if (info.Website is { Length: > 0 } site)
                    column.Children.Add(new HyperlinkButton
                    {
                        Content = site,
                        NavigateUri = Uri.TryCreate(site, UriKind.Absolute, out var uri) ? uri : null
                    });

                ProviderFields.Children.Add(column);
            }
        }
        catch (Exception error)
        {
            Report("读取标识符字段失败", error);
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        var ids = _idBoxes.Select(entry => (entry.Key, Value: entry.Box.Text)).ToList();

        if (!ItemIdentify.HasCriteria(NameBox.Text, ids.Select(entry => entry.Value)))
        {
            Problem.Message = "至少填一个条件：片名，或者任何一家的标识符。";
            Problem.IsOpen = true;
            return;
        }

        Problem.IsOpen = false;
        SetBusy(true);
        try
        {
            var results = await _search(ItemIdentify.SearchQuery(_item, NameBox.Text, YearBox.Text, ids))
                .ConfigureAwait(true);

            Candidates.Clear();
            foreach (var result in results) Candidates.Add(new IdentifyCandidate(result));

            NoResults.Visibility = Candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowStage(Stage.Results);

            // 海报一张张来：先把格子摆出来，图各自到位 —— 等全部取回来再画会让一屏结果卡在那里不动。
            foreach (var candidate in Candidates) _ = candidate.LoadAsync(_picture);
        }
        catch (Exception error)
        {
            Report("搜索刮削源失败", error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IdentifyCandidate candidate }) return;

        _chosen = candidate;
        ChosenName.Text = candidate.Title;
        ChosenDetail.Text = candidate.Subtitle;

        // 默认**不**换图：用户刚挑的那张封面不该因为一次识别被扔掉（同 EmbyClient.ApplyRemoteSearchResultAsync）。
        ReplaceImages.IsChecked = false;

        ShowStage(Stage.Apply);
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_chosen is not { } candidate) return;

        SetBusy(true);
        try
        {
            await _apply(candidate.Result, ReplaceImages.IsChecked == true).ConfigureAwait(true);
            Touched = true;
            Hide();
        }
        catch (Exception error)
        {
            Report("识别失败", error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>「移除识别」：先问一句。这一下把服务器上这个条目的元数据整个重置掉，问不出口就什么都不做。</summary>
    private void OnAskRemove(object sender, RoutedEventArgs e)
    {
        ConfirmText.Text = $"将重置服务器上「{_item.Name}」的元数据，之后它不再对上任何一部片子。"
            + "手改过的片名、简介和分级会一起没掉，此操作无法撤销。";
        ShowStage(Stage.Confirm);
    }

    private void OnCancelRemove(object sender, RoutedEventArgs e) => ShowStage(Stage.Form);

    private async void OnConfirmRemove(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            await _remove().ConfigureAwait(true);
            Touched = true;
            Hide();
        }
        catch (Exception error)
        {
            Report("移除识别失败", error);
            ShowStage(Stage.Form);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnBackToForm(object sender, RoutedEventArgs e) => ShowStage(Stage.Form);

    private void OnBackToResults(object sender, RoutedEventArgs e) => ShowStage(Stage.Results);

    /// <summary>
    /// 换一段：正文露那一块，按钮露那两颗。四段互斥，所以一份名单就够了。
    /// <para>
    /// 按钮在滚动区外面，一次只露该露的两颗 —— 全都摆着会让用户在「返回搜索结果」上按下去发现回的是搜索表单。
    /// </para>
    /// </summary>
    private void ShowStage(Stage stage)
    {
        SearchForm.Visibility = stage == Stage.Form ? Visibility.Visible : Visibility.Collapsed;
        ConfirmArea.Visibility = stage == Stage.Confirm ? Visibility.Visible : Visibility.Collapsed;
        ResultsArea.Visibility = stage == Stage.Results ? Visibility.Visible : Visibility.Collapsed;
        ApplyArea.Visibility = stage == Stage.Apply ? Visibility.Visible : Visibility.Collapsed;

        SearchButton.Visibility = stage == Stage.Form ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = stage == Stage.Form ? Visibility.Visible : Visibility.Collapsed;

        ConfirmRemoveButton.Visibility = stage == Stage.Confirm ? Visibility.Visible : Visibility.Collapsed;
        CancelRemoveButton.Visibility = stage == Stage.Confirm ? Visibility.Visible : Visibility.Collapsed;

        ApplyButton.Visibility = stage == Stage.Apply ? Visibility.Visible : Visibility.Collapsed;
        BackToFormButton.Visibility = stage == Stage.Results ? Visibility.Visible : Visibility.Collapsed;
        BackToResultsButton.Visibility = stage == Stage.Apply ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 忙的时候只把会发请求的那几颗按钮按下去 —— 把「返回」也一起关在门里，用户按不到取消的时候只会去点窗口
    /// 的叉，那一下连正在飞的那一趟也一起丢了。
    /// </summary>
    private void SetBusy(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy;
        ConfirmRemoveButton.IsEnabled = !busy;
    }

    private void Report(string what, Exception error)
    {
        Log.Warn(Category, what, error);
        Problem.Message = $"{what}：{Failure.Describe(error)}";
        Problem.IsOpen = true;
    }

    /// <summary>取一个间距刻度。XAML 里是 <c>StaticResource</c>，代码里造控件时只能这样拿。</summary>
    private static double Space(string key) =>
        Application.Current.Resources[key] is double value ? value : 0;
}
