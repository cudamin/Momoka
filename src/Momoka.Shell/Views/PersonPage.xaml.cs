using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Windows.UI.Text;

namespace Momoka.Shell.Views;

/// <summary>
/// 人物页的页面层：解析服务、把视图模型接上、处理点击/右键/「更多」，以及那一行用内联元素搭出来的
/// 数据库链接。凡是「这一页上发生了什么」都在这里；凡是「这一页该显示什么」都在
/// <see cref="PersonViewModel"/> 里。
/// </summary>
public sealed partial class PersonPage : Page, IShellContent
{
    private const string Category = "人物";

    private PersonRequest? _request;
    private EmbySession? _session;
    private IShellActions? _actions;
    private ISystemLauncher? _launcher;
    private bool _released;

    public PersonViewModel ViewModel { get; } = new();

    public PersonPage()
    {
        InitializeComponent();
    }

    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not PersonRequest request)
        {
            Log.Warn(Category, "导航参数缺失，无法确定要显示哪个演职人员");
            return;
        }

        _released = false;
        _request = request;
        Tag = string.Empty;

        var services = request.Services;
        _session = services.GetRequiredService<EmbySession>();
        _actions = services.GetRequiredService<IShellActions>();
        _launcher = services.GetRequiredService<ISystemLauncher>();

        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Attach(request, _actions, services.GetRequiredService<EmbyImageStore>());
        _actions.SetTitleStrip(TitleStrip.Plain);
        _ = ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Release();
    }

    /// <inheritdoc />
    public void Release()
    {
        if (_released) return;
        _released = true;

        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.Cancel();
        ViewModel.Dispose();
        OtherFactsRow.Children.Clear();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PersonViewModel.OtherFacts))
            PaintOtherFacts();
    }

    /// <summary>
    /// 其他信息那一行：目前只有「数据库链接」。和内联文字一样在代码里搭，因为每一段链接是
    /// <see cref="Hyperlink"/>；整行交给 <see cref="WrapRow"/>，窄窗口上换行而不是把后面几个链接挤出去。
    /// </summary>
    private void PaintOtherFacts()
    {
        OtherFactsRow.Children.Clear();

        var caption = (Style)Application.Current.Resources["EgCaptionStyle"];
        var body = (Style)Application.Current.Resources["EgBodyStyle"];
        var faint = (Brush)Application.Current.Resources["EgTextFaintBrush"];
        var dim = (Brush)Application.Current.Resources["EgTextDimBrush"];
        var accent = (Brush)Application.Current.Resources["EgAccentBrush"];

        foreach (var fact in ViewModel.OtherFacts)
        {
            if (OtherFactsRow.Children.Count > 0)
                OtherFactsRow.Children.Add(new TextBlock { Text = "·", Style = caption, Foreground = faint });

            OtherFactsRow.Children.Add(new TextBlock
            {
                Text = fact.Label + "：",
                Style = caption,
                Foreground = faint
            });

            for (var index = 0; index < fact.Values.Count; index++)
            {
                if (index > 0)
                    OtherFactsRow.Children.Add(new TextBlock { Text = "·", Style = caption, Foreground = faint });

                var value = fact.Values[index];
                if (value.Url.Length == 0)
                {
                    OtherFactsRow.Children.Add(new TextBlock { Text = value.Text, Style = body, Foreground = dim });
                    continue;
                }

                var holder = new TextBlock { Style = body };
                var link = new Hyperlink
                {
                    UnderlineStyle = UnderlineStyle.None,
                    Foreground = accent
                };
                link.Inlines.Add(new Run { Text = value.Text });
                var url = value.Url;
                link.Click += (_, _) => OpenLink(url);
                holder.Inlines.Add(link);
                OtherFactsRow.Children.Add(holder);
            }
        }
    }

    private void OpenLink(string url)
    {
        try
        {
            _launcher?.OpenUrl(url);
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"打开链接失败（{url}）：{error.Message}");
            _actions?.Notify($"打不开这个链接：{error.Message}", InfoBarSeverity.Warning);
        }
    }

    private void OnCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: PosterCard { Card: { } card } }) return;

        NavigationTiming.Click("person-card");
        ViewModel.OpenCard(card);
    }

    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        ItemCommands.Handle(_session, _actions, sender, args,
            changed: () => _ = ViewModel.LoadAsync(),
            presenterStyle: (Style)Application.Current.Resources["EgDetailMenuPresenterStyle"]);

    private void OnCardActionRequested(object? sender, CardActionEventArgs args) =>
        ItemCommands.Handle(_session, _actions, sender, args,
            changed: () => _ = ViewModel.LoadAsync(),
            presenterStyle: (Style)Application.Current.Resources["EgDetailMenuPresenterStyle"]);

    private void OnCreditClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PersonCreditRow row }) _actions?.OpenItem(row.Item);
    }

    private void OnGroupMoreClicked(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: PersonCreditGroup group }) ViewModel.OpenAll(group);
    }

    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null || _actions is null) return;

        ItemCommands.Run(ItemCommand.EditMetadata, _session, _actions, this, ViewModel.CommandCard,
            changed: () => _ = ViewModel.LoadAsync());
    }

    private void OnMoreClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null || _actions is null || sender is not FrameworkElement anchor) return;

        ItemCommands.Show(_session, _actions, anchor, ViewModel.CommandCard,
            position: null,
            changed: () => _ = ViewModel.LoadAsync(),
            presenterStyle: (Style)Application.Current.Resources["EgDetailMenuPresenterStyle"],
            self: true);
    }
}
