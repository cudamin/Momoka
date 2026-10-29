using Momoka.Diagnostics;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Momoka.Shell.Views;

/// <summary>匹配结果表上的一行：原始候选和给人看的副标题。</summary>
public sealed record FnNasScrapCandidate(FnNasScrapResult Result)
{
    public string Title => Result.Title.Length > 0 ? Result.Title : "（无标题）";

    public string Detail
    {
        get
        {
            var parts = new List<string>(3);
            if (Result.ReleaseDate.Length >= 4) parts.Add(Result.ReleaseDate[..4]);
            if (Result.Source.Length > 0) parts.Add(Result.Source);
            if (Result.Type.Length > 0) parts.Add(Result.Type switch { "TV" => "剧集", "Movie" => "电影", _ => Result.Type });
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }
}

/// <summary>飞牛的手动匹配表。搜索本身由调用方交进来，这里只管输入、候选和选中。</summary>
public sealed partial class FnNasMatchDialog : ContentDialog
{
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<FnNasScrapResult>>> _search;
    private readonly LoadGeneration _searches = new();
    private bool _active;
    private bool _busy;
    private int _visit;
    private string _searchSource = "";
    private string _searchKeyword = "";

    public FnNasMatchDialog(string keyword,
        Func<string, string, CancellationToken, Task<IReadOnlyList<FnNasScrapResult>>> search)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _search = search;
        KeywordBox.Text = keyword;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    /// <summary>保留匿名 UI 探针的惯用委托；生产搜索必须走带令牌的构造函数。</summary>
    internal FnNasMatchDialog(string keyword, Func<string, string, Task<IReadOnlyList<FnNasScrapResult>>> search)
        : this(keyword, (source, term, _) => search(source, term))
    {
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _active = false;
        _visit++;
        _searches.Dispose();
        _busy = false;
        Selected = null;
        IsPrimaryButtonEnabled = false;
        Results.ItemsSource = null;
        Problem.IsOpen = false;
        Busy.Visibility = Visibility.Collapsed;
        SearchButton.IsEnabled = true;
        _active = true;
        // 重开只恢复输入与按钮，搜索仍须用户明确提交。
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _active = false;
        _visit++;
        _searches.Dispose();
        _busy = false;
        Busy.Visibility = Visibility.Collapsed;
        SearchButton.IsEnabled = false;
        Results.ItemsSource = null;
        // Primary 的已选候选由调用方拿去确认后的写路径；关闭取消只读搜索，不抹掉这个结果。
    }

    private bool SearchCurrent(int visit, CancellationToken token) =>
        _active && visit == _visit && _searches.IsCurrent(token);

    public FnNasScrapCandidate? Selected { get; private set; }

    private void OnSearchClicked(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private void OnKeywordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (!_active) return;

        var keyword = (KeywordBox.Text ?? "").Trim();
        if (keyword.Length == 0)
        {
            ShowProblem("请先输入要搜索的片名或编号");
            return;
        }

        var source = SourceBox.SelectedIndex switch
        {
            1 => FnNasItemActions.SearchByImdbId,
            2 => FnNasItemActions.SearchByTmdbId,
            _ => FnNasItemActions.SearchByTitle
        };
        // 同一请求的重复提交忽略；用户改词/方式再按回车则替代旧读取，仍不随输入自动搜索。
        if (_busy && source == _searchSource && keyword == _searchKeyword) return;
        _searchSource = source;
        _searchKeyword = keyword;
        var visit = _visit;
        var token = _searches.Begin();
        _busy = true;
        Busy.Visibility = Visibility.Visible;
        SearchButton.IsEnabled = false;
        Selected = null;
        IsPrimaryButtonEnabled = false;
        Problem.IsOpen = false;
        Results.ItemsSource = null;

        try
        {
            if (!SearchCurrent(visit, token)) return;
            // 令牌传到真实只读请求；即使某个读取不合作，取消等待也不继续保留关闭的 UI。
            var found = await _search(source, keyword, token).WaitAsync(token).ConfigureAwait(true);
            if (!SearchCurrent(visit, token)) return;
            var rows = found.Select(result => new FnNasScrapCandidate(result)).ToList();
            Results.ItemsSource = rows;
            if (!SearchCurrent(visit, token)) return;
            if (rows.Count == 0) ShowProblem("没有找到匹配结果，换个关键词或搜索方式再试");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!SearchCurrent(visit, token)) return;
            Log.Warn("ui", "飞牛手动匹配搜索失败", error);
            ShowProblem($"搜索失败：{Failure.Describe(error)}");
        }
        finally
        {
            if (SearchCurrent(visit, token))
            {
                _busy = false;
                Busy.Visibility = Visibility.Collapsed;
                if (SearchCurrent(visit, token)) SearchButton.IsEnabled = true;
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_active) return;
        Selected = Results.SelectedItem is FnNasScrapCandidate candidate
            && Results.ItemsSource is IReadOnlyList<FnNasScrapCandidate> rows && rows.Any(row => ReferenceEquals(row, candidate)) ? candidate : null;
        IsPrimaryButtonEnabled = Selected is not null && !_busy;
    }

    private void OnMatch(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_active) { args.Cancel = true; return; }
        if (_busy || Selected is null)
        {
            args.Cancel = true;
            ShowProblem("请先选择一个匹配结果");
        }
    }

    private void ShowProblem(string message)
    {
        Problem.Message = message;
        Problem.IsOpen = true;
    }
}
