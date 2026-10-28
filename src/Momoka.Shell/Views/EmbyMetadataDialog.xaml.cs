using System.Globalization;
using Momoka.Diagnostics;
using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell.Views;

/// <summary>The Emby web editor's field order and semantics, expressed using native WinUI controls.</summary>
public sealed partial class EmbyMetadataDialog : ContentDialog
{
    private readonly EmbyMetadataEdit _initial;
    private readonly Func<EmbyMetadataEdit, Task<bool>> _save;
    private readonly List<ToggleButton> _locks = [];
    private readonly Dictionary<string, TextBox> _providerBoxes = new(StringComparer.OrdinalIgnoreCase);
    private bool _ready;
    private bool _saving;
    private bool _saved;

    public bool Changed { get; private set; }

    public EmbyMetadataDialog(string name, EmbyMetadataEdit edit, EmbyMetadataEditorInfo info,
        Func<EmbyMetadataEdit, Task<bool>> save)
    {
        _initial = edit;
        _save = save;
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = $"编辑元数据 — {name}";
        ItemPathText.Text = edit.Path;
        ItemPathText.Visibility = edit.Path.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = edit.Basic.Name;
        SortNameBox.Text = edit.Basic.SortName;
        DateCreatedBox.Text = edit.DateCreated;
        ParentIndexBox.Text = edit.Basic.ParentIndexNumber;
        IndexBox.Text = edit.Basic.IndexNumber;
        CommunityRatingBox.Text = edit.Basic.CommunityRating;
        OverviewBox.Text = edit.Basic.Overview;
        OriginalTitleBox.Text = edit.Basic.OriginalTitle;
        CriticRatingBox.Text = edit.Basic.CriticRating;
        ProductionYearBox.Text = edit.Basic.ProductionYear;
        PremiereDatePicker.MinDate = new DateTimeOffset(1800, 1, 1, 0, 0, 0, TimeSpan.Zero);
        PremiereDatePicker.MaxDate = new DateTimeOffset(2200, 12, 31, 0, 0, 0, TimeSpan.Zero);
        AddedDatePicker.MinDate = PremiereDatePicker.MinDate;
        AddedDatePicker.MaxDate = PremiereDatePicker.MaxDate;
        if (DateTimeOffset.TryParseExact(edit.Basic.PremiereDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var premiere)) PremiereDatePicker.Date = premiere;
        FillChoices(OfficialRatingBox, info.ParentalRatingOptions.Select(rating => (rating.Name, rating.Name)), edit.Basic.OfficialRating, "未设置");
        FillChoices(CustomRatingBox, info.ParentalRatingOptions.Select(rating => (rating.Name, rating.Name)), edit.CustomRating, "未设置");
        FillChoices(VideoFormatBox, EmbyMetadataEdit.VideoFormats.Select(key => (key, FormatName(key))), edit.Video3DFormat, "未设置");
        FillChoices(LanguageBox, info.Cultures.Select(culture =>
            (culture.TwoLetterISOLanguageName, Label(culture.DisplayName, culture.Name))), edit.PreferredMetadataLanguage, "继承上级或全局默认值");
        FillChoices(CountryBox, info.Countries.Select(country =>
            (country.TwoLetterISORegionName, Label(country.DisplayName, country.Name))), edit.PreferredMetadataCountryCode, "继承上级或全局默认值");
        LockItemSwitch.IsOn = edit.LockData;
        _locks.AddRange([NameLock, SortNameLock, CommunityRatingLock, OverviewLock, OfficialRatingLock, OriginalTitleLock, CriticRatingLock]);
        foreach (var toggle in _locks)
        {
            toggle.IsChecked = edit.LockedFields.Contains((string)toggle.Tag);
            OnLockChanged(toggle, new RoutedEventArgs());
            var header = (string)toggle.Tag switch
            {
                "Name" => "标题", "SortName" => "排序标题", "Overview" => "概要", "CommunityRating" => "社区评分",
                "OfficialRating" => "家长评分", "OriginalTitle" => "原始标题", "CriticRating" => "影评指数", _ => "此字段"
            };
            AutomationProperties.SetName(toggle, $"锁定{header}，阻止自动刷新覆盖");
        }
        GenresEditor.Configure("类型", "Genres", edit.Genres.Select(MetadataEntry.ForName), edit.LockedFields.Contains("Genres"));
        PeopleEditor.Configure("人物", "Cast", edit.People.Select(MetadataEntry.ForPerson), edit.LockedFields.Contains("Cast"), people: true);
        StudiosEditor.Configure("工作室", "Studios", edit.Studios.Select(MetadataEntry.ForStudio), edit.LockedFields.Contains("Studios"), studio: true);
        TagsEditor.Configure("标签", "Tags", edit.Tags.Select(MetadataEntry.ForName), edit.LockedFields.Contains("Tags"));
        BuildExternalIds(info.ExternalIdInfos);
        _ready = true;
        Validate();
    }

    public void SetItemType(string type)
    {
        IndexRow.Visibility = type is EmbyItemType.Episode or EmbyItemType.Season ? Visibility.Visible : Visibility.Collapsed;
        ParentIndexBox.Visibility = type == EmbyItemType.Episode ? Visibility.Visible : Visibility.Collapsed;
        IndexBox.Header = type == EmbyItemType.Season ? "季编号" : "集编号";
        VideoFormatBox.Visibility = type is EmbyItemType.Movie or EmbyItemType.Episode or EmbyItemType.Video or EmbyItemType.Trailer
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public EmbyMetadataEdit Edit => _initial with
    {
        Basic = _initial.Basic with
        {
            Name = NameBox.Text, SortName = SortNameBox.Text, OriginalTitle = OriginalTitleBox.Text,
            Overview = OverviewBox.Text, CommunityRating = CommunityRatingBox.Text, CriticRating = CriticRatingBox.Text,
            ProductionYear = ProductionYearBox.Text,
            PremiereDate = PremiereDatePicker.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            OfficialRating = Choice(OfficialRatingBox), IndexNumber = IndexBox.Text, ParentIndexNumber = ParentIndexBox.Text
        },
        DateCreated = DateCreatedBox.Text, CustomRating = Choice(CustomRatingBox), Video3DFormat = Choice(VideoFormatBox),
        PreferredMetadataLanguage = Choice(LanguageBox), PreferredMetadataCountryCode = Choice(CountryBox),
        LockData = LockItemSwitch.IsOn, LockedFields = ReadLocks(),
        Genres = GenresEditor.Rows.Select(row => row.Name).ToArray(), Tags = TagsEditor.Rows.Select(row => row.Name).ToArray(),
        People = PeopleEditor.Rows.Select(row => row.Person!).ToArray(), Studios = StudiosEditor.Rows.Select(row => row.Studio!).ToArray(),
        ProviderIds = _providerBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase)
    };

    private string[] ReadLocks()
    {
        var fields = _initial.LockedFields.ToList();
        foreach (var toggle in _locks) Update((string)toggle.Tag, toggle.IsChecked == true);
        foreach (var list in new[] { GenresEditor, PeopleEditor, StudiosEditor, TagsEditor }) Update(list.LockedField, list.IsLocked);
        return fields.ToArray();
        void Update(string field, bool locked)
        {
            if (locked && !fields.Contains(field)) fields.Add(field);
            else if (!locked) fields.RemoveAll(value => value == field);
        }
    }

    private void BuildExternalIds(IEnumerable<ExternalIdInfo> externalIds)
    {
        var descriptors = externalIds.Where(id => !string.IsNullOrWhiteSpace(id.Key))
            .DistinctBy(id => id.Key, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var key in _initial.ProviderIds.Keys)
            if (!descriptors.Any(info => info.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                descriptors.Add(new ExternalIdInfo { Key = key, Name = key });
        foreach (var info in descriptors)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new TextBox { Header = $"{Label(info.Name, info.Key)} 标识符", Text = _initial.ProviderIds.GetValueOrDefault(info.Key) ?? "" };
            AutomationProperties.SetAutomationId(box, $"ProviderId_{info.Key}");
            AutomationProperties.SetName(box, $"{Label(info.Name, info.Key)} 标识符");
            _providerBoxes[info.Key] = box;
            row.Children.Add(box);
            // Framework navigation rather than process creation in the view; only a validated URI is supplied.
            var link = new HyperlinkButton { Content = new FontIcon { Glyph = "\uE8A7" }, VerticalAlignment = VerticalAlignment.Bottom };
            AutomationProperties.SetAutomationId(link, $"ProviderLink_{info.Key}");
            AutomationProperties.SetName(link, $"打开 {Label(info.Name, info.Key)} 页面");
            ToolTipService.SetToolTip(link, "在浏览器中打开");
            Grid.SetColumn(link, 1);
            void RefreshLink()
            {
                link.NavigateUri = EmbyMetadataLink.Create(info, box.Text);
                link.Visibility = link.NavigateUri is null ? Visibility.Collapsed : Visibility.Visible;
            }
            box.TextChanged += (_, _) => RefreshLink();
            RefreshLink();
            row.Children.Add(link);
            ExternalIdRows.Children.Add(row);
        }
        ExternalIdsSection.Visibility = _providerBoxes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void FillChoices(ComboBox box, IEnumerable<(string Key, string Name)> choices, string selected, string empty)
    {
        box.Items.Add(new ComboBoxItem { Content = empty, Tag = "" });
        foreach (var (key, name) in choices.Where(choice => choice.Key.Length > 0).DistinctBy(choice => choice.Key))
            box.Items.Add(new ComboBoxItem { Content = name, Tag = key });
        var current = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
        // A missing catalog choice is retained and remains clearable, never silently changed to the default.
        if (current is null)
        {
            current = new ComboBoxItem { Content = selected, Tag = selected };
            box.Items.Add(current);
        }
        box.SelectedItem = current;
    }
    private static string Choice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private static string Label(string display, string fallback) => string.IsNullOrWhiteSpace(display) ? fallback : display;
    private static string FormatName(string format) => format switch
    {
        "HalfSideBySide" => "左右半宽", "FullSideBySide" => "左右全宽", "HalfTopAndBottom" => "上下半高",
        "FullTopAndBottom" => "上下全高", "MVC" => "MVC", _ => format
    };

    private void OnLockChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (toggle.Content is FontIcon icon) icon.Glyph = toggle.IsChecked == true ? "\uE72E" : "\uE785";
        ToolTipService.SetToolTip(toggle, toggle.IsChecked == true ? "已锁定，点击允许自动刷新" : "点击锁定，防止自动刷新覆盖");
    }
    private void OnCreatedDateOpening(object sender, object e)
    {
        var moment = DateTimeOffset.TryParse(DateCreatedBox.Text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var current) ? current : DateTimeOffset.Now;
        AddedDatePicker.Date = moment;
        AddedTimePicker.Time = moment.TimeOfDay;
    }
    private void OnApplyCreatedDate(object sender, RoutedEventArgs e)
    {
        if (AddedDatePicker.Date is not { } date) return;
        var seconds = DateTimeOffset.TryParse(DateCreatedBox.Text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var original) ? original.Second : 0;
        var time = AddedTimePicker.Time;
        DateCreatedBox.Text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + $" {time.Hours:00}:{time.Minutes:00}:{seconds:00}";
        CreatedDateFlyout.Hide();
    }
    private void OnEdited(object sender, TextChangedEventArgs e) => Validate();
    private void OnChoiceChanged(object sender, SelectionChangedEventArgs e) => Validate();
    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Validate();
    private void OnClearDate(object sender, RoutedEventArgs e) => PremiereDatePicker.Date = null;
    private void OnToggleChanged(object sender, RoutedEventArgs e) => Validate();
    private void Validate()
    {
        if (!_ready || _saving) return;
        var problem = Edit.Problem;
        Problem.Message = problem ?? "";
        Problem.IsOpen = problem is not null;
        IsPrimaryButtonEnabled = problem is null;
        ClearPremiereDate.Visibility = PremiereDatePicker.Date is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_saving) return;
        var edit = Edit;
        if (edit.Problem is not null) { Validate(); return; }
        var deferral = args.GetDeferral();
        _saving = true;
        IsPrimaryButtonEnabled = false;
        EditorScroll.IsEnabled = false;
        SavingProgress.Visibility = Visibility.Visible;
        Problem.IsOpen = false;
        try
        {
            Changed = await _save(edit);
            _saved = true;
            args.Cancel = false;
        }
        catch (Exception error)
        {
            Log.Warn("ui", "编辑元数据保存失败", error);
            Problem.Message = $"保存失败：{Failure.Describe(error)}。输入已保留，可以重试或取消。";
            Problem.IsOpen = true;
        }
        finally
        {
            _saving = false;
            EditorScroll.IsEnabled = true;
            SavingProgress.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = edit.Problem is null;
            deferral.Complete();
        }
    }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent Escape/X from discarding an in-flight shared metadata write.
        if (_saving && !_saved) args.Cancel = true;
    }
}
