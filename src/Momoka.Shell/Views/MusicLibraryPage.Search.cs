using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Momoka.Shell.Views;

public sealed partial class MusicLibraryPage
{
    private SearchInputGuard? _searchInputGuard;

    // Gallery MainWindow.xaml.cs, CtrlF_Invoked, v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7.
    // Hidden server-specific searches and detached pages must not consume the accelerator.
    private void CtrlF_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_released || _request is null || !MusicSearch.IsLoaded
            || MusicSearch.Visibility != Visibility.Visible || !MusicSearch.IsEnabled) return;
        args.Handled = MusicSearch.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 音乐搜索框与媒体库搜索框共用同一份输入法补偿：微软拼音漏送退格按下时，抬起补删光标左边一字或选区。
    /// 这一页不做输入防抖，文字由双向绑定回到 <see cref="MusicLibraryViewModel.Search"/>，回车再读一次列表。
    /// </summary>
    private void OnMusicSearchGotFocus(object sender, RoutedEventArgs args)
    {
        if (_released) return;
        (_searchInputGuard ??= new SearchInputGuard(submit: text => _ = ViewModel.ReloadAsync()))
            .Watch(args.OriginalSource);
    }

    private async void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // 原生查询与抬起补搜共用一次回车；这里返回 false 表示已经由另一边处理或仍在组字。
        if (_searchInputGuard?.TakeQuerySubmitted() == false) return;
        await ViewModel.ReloadAsync();
    }

    private void DetachSearchInput()
    {
        _searchInputGuard?.Release();
        _searchInputGuard = null;
    }
}
