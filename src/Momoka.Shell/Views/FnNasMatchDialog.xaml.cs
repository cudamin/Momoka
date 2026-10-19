using Momoka.Diagnostics;
using Momoka.FnNas;
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
    private readonly Func<string, string, Task<IReadOnlyList<FnNasScrapResult>>> _search;
    private bool _busy;

    public FnNasMatchDialog(string keyword, Func<string, string, Task<IReadOnlyList<FnNasScrapResult>>> search)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _search = search;
        KeywordBox.Text = keyword;
    }

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
        if (_busy) return;

        var keyword = (KeywordBox.Text ?? "").Trim();
        if (keyword.Length == 0)
        {
            ShowProblem("请先输入要搜索的片名或编号");
            return;
        }

        _busy = true;
        Busy.Visibility = Visibility.Visible;
        SearchButton.IsEnabled = false;
        Selected = null;
        IsPrimaryButtonEnabled = false;
        Problem.IsOpen = false;
        Results.ItemsSource = null;

        try
        {
            var source = SourceBox.SelectedIndex switch
            {
                1 => FnNasItemActions.SearchByImdbId,
                2 => FnNasItemActions.SearchByTmdbId,
                _ => FnNasItemActions.SearchByTitle
            };
            var found = await _search(source, keyword).ConfigureAwait(true);
            var rows = found.Select(result => new FnNasScrapCandidate(result)).ToList();
            Results.ItemsSource = rows;
            if (rows.Count == 0) ShowProblem("没有找到匹配结果，换个关键词或搜索方式再试");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Warn("ui", "飞牛手动匹配搜索失败", error);
            ShowProblem($"搜索失败：{Failure.Describe(error)}");
        }
        finally
        {
            _busy = false;
            Busy.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Selected = Results.SelectedItem as FnNasScrapCandidate;
        IsPrimaryButtonEnabled = Selected is not null && !_busy;
    }

    private void OnMatch(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (Selected is null)
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
