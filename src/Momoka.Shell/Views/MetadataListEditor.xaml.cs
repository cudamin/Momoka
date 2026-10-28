using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Momoka.Shell.Views;

/// <summary>One native list editor shared by the Emby form's four list sections. It performs no server IO.</summary>
public sealed partial class MetadataListEditor : UserControl
{
    private MetadataEntry? _editing;
    private bool _people;
    private bool _studio;
    private string _header = "";
    public ObservableCollection<MetadataEntry> Rows { get; } = [];
    public string LockedField { get; private set; } = "";
    public bool IsLocked { get => LockToggle.IsChecked == true; set => LockToggle.IsChecked = value; }

    public MetadataListEditor()
    {
        InitializeComponent();
        Rows.CollectionChanged += OnRowsChanged;
    }

    public void Configure(string header, string lockedField, IEnumerable<MetadataEntry> rows, bool locked,
        bool people = false, bool studio = false)
    {
        _header = header;
        _people = people;
        _studio = studio;
        LockedField = lockedField;
        HeaderText.Text = header;
        AutomationProperties.SetName(Entries, header);
        AutomationProperties.SetName(AddButton, $"添加{header}");
        AutomationProperties.SetName(LockToggle, $"锁定{header}，阻止自动刷新覆盖");
        IsLocked = locked;
        OnLockChanged(LockToggle, new RoutedEventArgs());
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        EntryRoleBox.Visibility = EntryTypeBox.Visibility = people ? Visibility.Visible : Visibility.Collapsed;
        UpdateChrome();
    }

    private void OnLockChanged(object sender, RoutedEventArgs e)
    {
        if (LockIcon is null) return;
        LockIcon.Glyph = IsLocked ? "\uE72E" : "\uE785";
        ToolTipService.SetToolTip(LockToggle, IsLocked ? "已锁定，点击允许自动刷新" : "点击锁定，防止自动刷新覆盖");
    }

    private void OnAdd(object sender, RoutedEventArgs e) => OpenDraft((FrameworkElement)sender, null);
    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MetadataEntry row } element) OpenDraft(element, row);
    }

    private void OpenDraft(FrameworkElement anchor, MetadataEntry? row)
    {
        _editing = row;
        DraftTitle.Text = $"{(row is null ? "添加" : "编辑")}{_header}";
        EntryNameBox.Text = row?.Name ?? "";
        EntryRoleBox.Text = row?.Person?.Role ?? "";
        EntryTypeBox.Items.Clear();
        var type = row?.Person?.Type ?? "Actor";
        foreach (var key in EmbyMetadataEdit.PersonTypes.Concat([type]).Distinct())
        {
            var option = new ComboBoxItem { Content = PersonTypeName(key), Tag = key };
            EntryTypeBox.Items.Add(option);
            if (key == type) EntryTypeBox.SelectedItem = option;
        }
        ValidateDraft();
        DraftFlyout.ShowAt(anchor);
        EntryNameBox.Focus(FocusState.Programmatic);
    }

    private void OnDraftEdited(object sender, TextChangedEventArgs e) => ValidateDraft();
    private void ValidateDraft()
    {
        if (EntryNameBox is null || ApplyEntryButton is null) return;
        var text = EntryNameBox.Text.Trim();
        var problem = text.Length == 0 ? "名称不能为空"
            : !_people && Rows.Any(row => !ReferenceEquals(row, _editing)
                && row.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) ? "此项已经存在" : null;
        ApplyEntryButton.IsEnabled = problem is null;
        DraftProblem.Message = problem ?? "";
        DraftProblem.IsOpen = text.Length > 0 && problem is not null;
    }

    private void OnApplyEntry(object sender, RoutedEventArgs e) => ApplyDraft();
    private void ApplyDraft()
    {
        ValidateDraft();
        if (!ApplyEntryButton.IsEnabled) return;
        var name = EntryNameBox.Text.Trim();
        var row = _people
            ? MetadataEntry.ForPerson((_editing?.Person ?? new EmbyMetadataPerson()) with
            {
                Name = name, Role = EntryRoleBox.Text.Trim(),
                Type = (EntryTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Actor"
            })
            : _studio ? MetadataEntry.ForStudio((_editing?.Studio ?? new EmbyMetadataStudio()) with { Name = name })
            : MetadataEntry.ForName(name);
        var index = _editing is null ? -1 : Rows.IndexOf(_editing);
        if (index < 0) Rows.Add(row);
        else Rows[index] = row;
        Entries.SelectedItem = row;
        DraftFlyout.Hide();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MetadataEntry row }) Rows.Remove(row);
    }
    private void OnCancelEntry(object sender, RoutedEventArgs e) => DraftFlyout.Hide();
    private void OnDraftClosed(object sender, object e) => _editing = null;
    private void OnDraftKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ApplyDraft();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateChrome();
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateChrome();
    private void UpdateChrome()
    {
        if (EmptyText is null || Entries is null) return;
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = Entries.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoveDownButton.Visibility = Entries.SelectedIndex >= 0 && Entries.SelectedIndex < Rows.Count - 1
            ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        var oldIndex = Entries.SelectedIndex;
        var newIndex = oldIndex + delta;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Rows.Count) return;
        var selected = Rows[oldIndex];
        Rows.Move(oldIndex, newIndex);
        Entries.SelectedItem = selected;
        UpdateChrome();
    }
    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Keyboard fallback for the drag handle; arrows without Alt retain ordinary ListView navigation.
        if (!e.KeyStatus.IsMenuKeyDown || e.Key is not (VirtualKey.Up or VirtualKey.Down)) return;
        e.Handled = true;
        Move(e.Key == VirtualKey.Up ? -1 : 1);
    }
    private static string PersonTypeName(string type) => type switch
    {
        "Actor" => "演员", "Director" => "导演", "Writer" => "编剧", "Producer" => "制片人",
        "GuestStar" => "客串", "Composer" => "作曲", "Conductor" => "指挥", "Lyricist" => "作词", _ => type
    };
}

public sealed record MetadataEntry
{
    public string Name { get; private init; } = "";
    public string Description { get; private init; } = "";
    public Visibility DescriptionVisibility => Description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public EmbyMetadataPerson? Person { get; private init; }
    public EmbyMetadataStudio? Studio { get; private init; }
    public static MetadataEntry ForName(string name) => new() { Name = name };
    public static MetadataEntry ForPerson(EmbyMetadataPerson person) => new()
        { Name = person.Name, Description = person.Description, Person = person };
    public static MetadataEntry ForStudio(EmbyMetadataStudio studio) => new() { Name = studio.Name, Studio = studio };
}
