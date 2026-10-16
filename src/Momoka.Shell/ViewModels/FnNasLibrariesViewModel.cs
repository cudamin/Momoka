using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasLibrariesViewModel : FnNasAdminViewModel
{
    private JsonObject? _original;
    private readonly List<string> _authorizedRoots = [];
    private PickedFnNasPlaylist? _playlist;
    private string? _uploadTempId;
    private FnNasLibraryRow[] _orderBeforeEdit = [];
    internal Func<Task<PickedFnNasPlaylist?>>? PickPlaylist { get; set; }
    public ObservableCollection<FnNasLibraryRow> Libraries { get; } = [];
    public ObservableCollection<FnNasChoice> Folders { get; } = [];
    public ObservableCollection<FnNasChoice> DirectoryItems { get; } = [];
    public ObservableCollection<FnNasChoice> Languages { get; } = [];
    public ObservableCollection<FnNasChoice> Categories { get; } =
    [new("Movie", "电影"), new("TV", "电视节目"), new("Mix", "混合影片"), new("IPTV", "电视直播（M3U8）"), new("Others", "其他视频")];
    [ObservableProperty] public partial bool Editing { get; set; }
    [ObservableProperty] public partial bool Sorting { get; set; }
    [ObservableProperty] public partial FnNasLibraryRow? SelectedLibrary { get; set; }
    [ObservableProperty] public partial bool BrowsingDirectories { get; set; }
    [ObservableProperty] public partial string LibraryName { get; set; } = "";
    [ObservableProperty] public partial string Category { get; set; } = "Movie";
    [ObservableProperty] public partial string Language { get; set; } = "any";
    [ObservableProperty] public partial string SubtitleLanguage { get; set; } = "any";
    [ObservableProperty] public partial bool PreferLocalNfo { get; set; } = true;
    [ObservableProperty] public partial bool FilterSmallFiles { get; set; } = true;
    [ObservableProperty] public partial double MinimumFileSize { get; set; } = 3;
    [ObservableProperty] public partial bool IncludeAdult { get; set; }
    [ObservableProperty] public partial bool AutoSubtitle { get; set; }
    [ObservableProperty] public partial bool ProgressThumb { get; set; }
    [ObservableProperty] public partial int ViewType { get; set; }
    [ObservableProperty] public partial string DirectoryPath { get; set; } = "";
    [ObservableProperty] public partial string PlaylistUrl { get; set; } = "";
    [ObservableProperty] public partial string PlaylistText { get; set; } = "";
    [ObservableProperty] public partial string PlaylistPath { get; set; } = "";
    [ObservableProperty] public partial string PlaylistFileName { get; set; } = "尚未选择文件";
    [ObservableProperty] public partial int PlaylistMode { get; set; }
    [ObservableProperty] public partial int PlaylistRefresh { get; set; }
    [ObservableProperty] public partial bool ReplacePlaylist { get; set; }
    public bool CanSave => CanUse && Editing;
    public bool CanSort => CanUse && Libraries.Count > 1;
    public bool CanChooseDirectory => CanSave && BrowsingDirectories && DirectoryPath.Length > 0 && Category != "IPTV";
    public bool CanDelete => CanSave && _original is not null;
    public string CountText => Libraries.Count == 0 ? "还没有媒体库，添加 NAS 上的文件夹以开始使用。" : $"{Libraries.Count} 个媒体库";
    public string EditorTitle => _original is null ? "新增媒体库" : "编辑媒体库";
    public Visibility ListVisibility => Show(!Editing);
    public Visibility EditorVisibility => Show(Editing);
    public Visibility DeleteVisibility => Show(_original is not null);
    public Visibility FilesVisibility => Show(Category != "IPTV");
    public Visibility IptvVisibility => Show(Category == "IPTV");
    public Visibility SubtitleVisibility => Show(Category is not ("IPTV" or "Others"));
    public Visibility ViewTypeVisibility => Show(Category == "Others");
    public Visibility MinimumSizeVisibility => Show(FilterSmallFiles);
    public Visibility DirectoryBrowserVisibility => Show(BrowsingDirectories);
    public Visibility SortVisibility => Show(Sorting);
    public string SortLabel => Sorting ? "取消排序" : "排序";
    public Visibility SubtitleLanguageVisibility => Show(Category is not ("IPTV" or "Others") && AutoSubtitle);
    public bool HasSavedPlaylist => _original is not null && Category == "IPTV" && FnNasJson.Text(_original["iptv"], "source_type") != "url";
    public Visibility KeepPlaylistVisibility => Show(HasSavedPlaylist);
    public Visibility PlaylistEditorVisibility => Show(!HasSavedPlaylist || ReplacePlaylist);
    public Visibility PlaylistUrlVisibility => Show(PlaylistMode == 0);
    public Visibility PlaylistTextVisibility => Show(PlaylistMode == 1);
    public Visibility PlaylistNasVisibility => Show(PlaylistMode == 2);
    public Visibility PlaylistUploadVisibility => Show(PlaylistMode == 3);

    protected override void ResetEditor()
    {
        Editing = Sorting = BrowsingDirectories = false;
        _original = null;
        _playlist = null;
        _uploadTempId = null;
        Libraries.Clear();
        Folders.Clear();
        DirectoryItems.Clear();
        AnnounceEditor();
    }

    public override Task ReloadAsync() => RunAsync("读取媒体库失败", ReadLibrariesAsync);

    private async Task ReadLibrariesAsync(EmbySessionScope scope, CancellationToken token)
    {
        var libraries = await scope.ExecuteAsync((client, ct) => client.FnNas.GetAdminLibrariesAsync(CurrentConnection(scope, client), ct), token);
        if (!Current(token)) return;
        Libraries.Clear();
        foreach (var library in libraries) Libraries.Add(new FnNasLibraryRow(library));
        SelectedLibrary = Libraries.FirstOrDefault();
        Sorting = false;
        OnPropertyChanged(nameof(CountText));
        BusyChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task NewLibraryAsync() => OpenAsync(null);

    internal Task OpenAsync(FnNasLibraryRow? row) => RunAsync("打开媒体库失败", async (scope, token) =>
    {
        var original = row is null ? null : await scope.ExecuteAsync((client, ct) => client.FnNas.GetLibraryAsync(CurrentConnection(scope, client), row.Id, ct), token);
        var settings = await scope.ExecuteAsync((client, ct) => client.FnNas.GetConfigurationAsync(client.Connection.ApiBase, ct), token);
        if (!Current(token)) return;
        _original = original;
        LibraryName = FnNasJson.Text(original, "name");
        Category = FnNasJson.Text(original, "category", "Movie");
        Language = FnNasJson.Text(original, "lan", "any");
        SubtitleLanguage = FnNasJson.Text(original, "subtitle_lan", "any");
        PreferLocalNfo = original is null || FnNasJson.Flag(original, "prefer_local_nfo");
        var size = FnNasJson.Number(original, "skip_filesize", 3);
        FilterSmallFiles = size > 0;
        MinimumFileSize = size > 0 ? size : 3;
        IncludeAdult = FnNasJson.Flag(original, "include_adult");
        AutoSubtitle = FnNasJson.Flag(original, "auto_scrap_subtitle");
        ProgressThumb = FnNasJson.Flag(original, "auto_progress_thumb");
        ViewType = FnNasJson.Number(original, "view_type");
        var playlist = original?["iptv"];
        PlaylistMode = FnNasJson.Text(playlist, "source_type") switch { "text" => 1, "local_file" => 2, "upload" => 3, _ => 0 };
        PlaylistUrl = FnNasJson.Text(playlist, "source_url");
        PlaylistPath = FnNasJson.Text(playlist, "local_file_path");
        PlaylistText = "";
        PlaylistFileName = "尚未选择文件";
        PlaylistRefresh = System.Array.IndexOf(new[] { 0, 3600, 10800, 21600, 43200 }, FnNasJson.Number(playlist, "refresh_interval"));
        if (PlaylistRefresh < 0) PlaylistRefresh = 0;
        ReplacePlaylist = false;
        _playlist = null;
        _uploadTempId = null;
        Folders.Clear();
        foreach (var path in FnNasJson.Strings(original?["dir_list"])) Folders.Add(new FnNasChoice(path, path));
        Languages.Clear();
        Languages.Add(new FnNasChoice("any", "跟随服务器"));
        foreach (var language in FnNasJson.Strings(settings["supported_languages"]).Append(Language).Append(SubtitleLanguage).Distinct())
            if (!Languages.Any(choice => choice.Id == language)) Languages.Add(new FnNasChoice(language, FnNasJson.LanguageName(language)));
        BrowsingDirectories = Sorting = false;
        Editing = true;
        AnnounceEditor();
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Back()
    {
        if (!CanSave) return;
        BrowsingDirectories = false;
        Editing = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("操作或刷新失败，请刷新列表核对后重试", async (scope, token) =>
    {
        if (string.IsNullOrWhiteSpace(LibraryName) || LibraryName.Trim().Length > 32) throw new ArgumentException("媒体库名称必须为 1～32 个字符。");
        if (Category != "IPTV" && Folders.Count == 0) throw new ArgumentException("请至少选择一个媒体库文件夹。");
        if (FilterSmallFiles && (!double.IsFinite(MinimumFileSize) || MinimumFileSize is < 1 or > 1024)) throw new ArgumentException("文件过滤大小须为 1～1024 MB。");
        var draft = _original is null ? new JsonObject() : FnNasJson.Copy(_original);
        draft["name"] = LibraryName.Trim();
        draft["category"] = Category;
        if (Category != "IPTV")
        {
            draft["dir_list"] = FnNasJson.Array(Folders.Select(folder => folder.Id));
            draft["lan"] = Language;
            draft["include_adult"] = IncludeAdult;
            draft["skip_filesize"] = FilterSmallFiles ? (int)MinimumFileSize : 0;
            draft["prefer_local_nfo"] = PreferLocalNfo ? 1 : 0;
            draft["auto_progress_thumb"] = ProgressThumb ? 1 : 0;
            if (Category == "Others") draft["view_type"] = ViewType;
            else
            {
                draft["subtitle_lan"] = SubtitleLanguage;
                draft["auto_scrap_subtitle"] = AutoSubtitle ? 1 : 0;
            }
        }
        else await WriteIptvAsync(draft, scope, token);
        if (!Current(token)) return;
        if (_original is { } original)
            await scope.ExecuteAsync((client, ct) => client.FnNas.UpdateLibraryAsync(CurrentConnection(scope, client), FnNasJson.Text(original, "guid"), draft, ct), token);
        else await scope.ExecuteAsync((client, ct) => client.FnNas.CreateLibraryAsync(CurrentConnection(scope, client), draft, ct), token);
        if (!Current(token)) return;
        Editing = BrowsingDirectories = false;
        Notify("已保存", "媒体库设置已更新。", InfoBarSeverity.Success);
        await ReadLibrariesAsync(scope, token);
    });

    private async Task WriteIptvAsync(JsonObject draft, EmbySessionScope scope, CancellationToken token)
    {
        if (HasSavedPlaylist && !ReplacePlaylist) { draft.Remove("iptv"); return; }
        JsonObject playlist;
        switch (PlaylistMode)
        {
            case 0:
                if (!Uri.TryCreate(PlaylistUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) throw new ArgumentException("请输入有效的 HTTP 或 HTTPS 播放列表地址。");
                playlist = new JsonObject { ["source_type"] = "url", ["source_url"] = url.AbsoluteUri, ["refresh_interval"] = new[] { 0, 3600, 10800, 21600, 43200 }[Math.Clamp(PlaylistRefresh, 0, 4)] };
                break;
            case 1:
                if (string.IsNullOrWhiteSpace(PlaylistText)) throw new ArgumentException("请粘贴播放列表内容。");
                playlist = new JsonObject { ["source_type"] = "text", ["content"] = PlaylistText };
                break;
            case 2:
                if (string.IsNullOrWhiteSpace(PlaylistPath)) throw new ArgumentException("请选择 NAS 上的播放列表文件。");
                playlist = new JsonObject { ["source_type"] = "local_file", ["local_file_path"] = PlaylistPath };
                break;
            case 3:
                if (_playlist is not { } file) throw new ArgumentException("请选择本地播放列表文件。");
                if (_uploadTempId is null)
                {
                    var id = await scope.ExecuteAsync((client, ct) => client.FnNas.UploadIptvSourceAsync(CurrentConnection(scope, client), file.Bytes, file.FileName, ct), token);
                    if (!Current(token)) return;
                    _uploadTempId = id;
                }
                playlist = new JsonObject { ["source_type"] = "upload", ["temp_file_id"] = _uploadTempId };
                break;
            default: throw new ArgumentException("请选择播放列表来源。");
        }
        draft["iptv"] = playlist;
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => !CanDelete ? Task.CompletedTask : RunAsync("移除媒体库失败", async (scope, token) =>
    {
        if (_original is not { } library) return;
        var name = FnNasJson.Text(library, "name");
        if (!await ConfirmAsync("移除媒体库", $"确定移除「{name}」吗？媒体库信息会从飞牛影视中移除，NAS 上的媒体文件会保留。", "移除") || !Current(token)) return;
        await scope.ExecuteAsync((client, ct) => client.FnNas.DeleteLibraryAsync(CurrentConnection(scope, client), FnNasJson.Text(library, "guid"), ct), token);
        if (!Current(token)) return;
        Editing = BrowsingDirectories = false;
        await ReadLibrariesAsync(scope, token);
        if (Current(token)) Notify("已移除", $"媒体库「{name}」已移除。", InfoBarSeverity.Success);
    });

    internal Task ScanAsync(FnNasLibraryRow row) => row.Category == "IPTV" ? Task.CompletedTask : RunAsync("扫描媒体库失败", async (scope, token) =>
    {
        await scope.ExecuteAsync((client, ct) => client.FnNas.ScanLibraryAsync(CurrentConnection(scope, client), row.Id, ct: ct), token);
        if (Current(token)) Notify("已提交扫描", $"正在检查「{row.Name}」的文件变更。", InfoBarSeverity.Success);
    });

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task ScanAllAsync() => Libraries.Count == 0 ? Task.CompletedTask : RunAsync("扫描媒体库失败", async (scope, token) =>
    {
        await scope.ExecuteAsync((client, ct) => client.FnNas.ScanAllLibrariesAsync(CurrentConnection(scope, client), ct), token);
        if (Current(token)) Notify("已提交扫描", "正在检查所有媒体库的文件变更。", InfoBarSeverity.Success);
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task BrowseAsync() => !CanSave ? Task.CompletedTask : RunAsync("读取授权目录失败", async (scope, token) =>
    {
        var authorized = await scope.ExecuteAsync((client, ct) => client.FnNas.GetAuthorizedDirectoriesAsync(CurrentConnection(scope, client), ct), token);
        if (!Current(token)) return;
        _authorizedRoots.Clear();
        if (authorized["authDirList"] is JsonArray roots)
            _authorizedRoots.AddRange(roots.OfType<JsonObject>().Select(root => FnNasJson.Text(root, "path")).Where(path => path.Length > 0));
        BrowsingDirectories = true;
        ShowRoots();
        if (_authorizedRoots.Count == 0) Notify("没有已授权的文件夹", "请先在 NAS 中为飞牛影视授权媒体文件夹，然后刷新。", InfoBarSeverity.Informational);
    });

    internal Task BrowseDirectoryAsync(FnNasChoice row)
    {
        if (!CanSave || !BrowsingDirectories || !AllowedPath(row.Id)) return Task.CompletedTask;
        if (!row.IsDirectory)
        {
            if (Category == "IPTV" && PlaylistMode == 2) { PlaylistPath = row.Id; BrowsingDirectories = false; }
            return Task.CompletedTask;
        }
        return RunAsync("读取文件夹失败", async (scope, token) =>
    {
        var entries = await scope.ExecuteAsync((client, ct) => client.FnNas.BrowseDirectoriesAsync(CurrentConnection(scope, client), row.Id, ct), token);
        if (!Current(token)) return;
        DirectoryPath = row.Id;
        DirectoryItems.Clear();
        foreach (var entry in entries.Where(entry => FnNasJson.Flag(entry, "is_dir") || Category == "IPTV" && PlaylistMode == 2))
        {
            var name = FnNasJson.Text(entry, "filename");
            var path = name.StartsWith('/') ? name : row.Id.TrimEnd('/') + "/" + name;
            if (name.Length > 0 && AllowedPath(path)) DirectoryItems.Add(new FnNasChoice(path, name, FnNasJson.Flag(entry, "is_dir")));
        }
    });
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task PickPlaylistAsync() => !CanSave ? Task.CompletedTask : RunAsync("读取播放列表失败", async (_, token) =>
    {
        if (PickPlaylist is null) return;
        var picked = await PickPlaylist();
        if (!Current(token) || picked is null) return;
        _playlist = picked;
        _uploadTempId = null;
        PlaylistFileName = picked.Value.FileName;
    });

    private bool AllowedPath(string path) => !path.Split('/').Contains("..", StringComparer.Ordinal) && _authorizedRoots.Any(root => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Roots() { if (CanSave) ShowRoots(); }

    private void ShowRoots()
    {
        DirectoryPath = "";
        DirectoryItems.Clear();
        foreach (var root in _authorizedRoots) DirectoryItems.Add(new FnNasChoice(root, root));
    }

    [RelayCommand(CanExecute = nameof(CanChooseDirectory))]
    private void ChooseDirectory()
    {
        if (!CanChooseDirectory || !AllowedPath(DirectoryPath)) return;
        if (!Folders.Any(folder => folder.Id == DirectoryPath)) Folders.Add(new FnNasChoice(DirectoryPath, DirectoryPath));
        BrowsingDirectories = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void CloseDirectories() { if (CanSave) BrowsingDirectories = false; }

    internal void RemoveFolder(FnNasChoice row) { if (CanSave) Folders.Remove(row); }
    [RelayCommand(CanExecute = nameof(CanSort))]
    private void Sort()
    {
        if (!CanSort) return;
        if (!Sorting) _orderBeforeEdit = Libraries.ToArray();
        else
        {
            Libraries.Clear();
            foreach (var row in _orderBeforeEdit) Libraries.Add(row);
            SelectedLibrary = Libraries.FirstOrDefault();
        }
        Sorting = !Sorting;
    }

    [RelayCommand(CanExecute = nameof(CanSort))]
    private void MoveUp() { if (SelectedLibrary is { } row) Move(row, -1); }

    [RelayCommand(CanExecute = nameof(CanSort))]
    private void MoveDown() { if (SelectedLibrary is { } row) Move(row, 1); }

    [RelayCommand(CanExecute = nameof(CanSort))]
    private Task SaveOrderAsync() => !CanSort || !Sorting ? Task.CompletedTask : RunAsync("保存媒体库排序失败", async (scope, token) =>
    {
        var order = Libraries.Select(row => row.Id).ToArray();
        await scope.ExecuteAsync((client, ct) => client.FnNas.SetLibraryOrderAsync(CurrentConnection(scope, client), order, ct), token);
        if (!Current(token)) return;
        Sorting = false;
        Notify("已保存", "媒体库顺序已更新。", InfoBarSeverity.Success);
    });

    internal void Move(FnNasLibraryRow row, int delta)
    {
        if (!CanSort || !Sorting) return;
        var index = Libraries.IndexOf(row);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < Libraries.Count) Libraries.Move(index, target);
    }

    partial void OnEditingChanged(bool value) => AnnounceEditor();
    partial void OnCategoryChanged(string value) => AnnounceEditor();
    partial void OnFilterSmallFilesChanged(bool value) => OnPropertyChanged(nameof(MinimumSizeVisibility));
    partial void OnSortingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortVisibility));
        OnPropertyChanged(nameof(SortLabel));
    }
    partial void OnAutoSubtitleChanged(bool value) => OnPropertyChanged(nameof(SubtitleLanguageVisibility));
    partial void OnBrowsingDirectoriesChanged(bool value)
    {
        OnPropertyChanged(nameof(DirectoryBrowserVisibility));
        BusyChanged();
    }
    partial void OnDirectoryPathChanged(string value) => BusyChanged();
    partial void OnPlaylistModeChanged(int value) => AnnounceEditor();
    partial void OnReplacePlaylistChanged(bool value) => AnnounceEditor();
    private void AnnounceEditor()
    {
        OnPropertyChanged(nameof(SubtitleLanguageVisibility));
        foreach (var name in new[] { nameof(EditorTitle), nameof(ListVisibility), nameof(EditorVisibility), nameof(DeleteVisibility), nameof(FilesVisibility), nameof(IptvVisibility), nameof(SubtitleVisibility), nameof(ViewTypeVisibility), nameof(KeepPlaylistVisibility), nameof(PlaylistEditorVisibility), nameof(PlaylistUrlVisibility), nameof(PlaylistTextVisibility), nameof(PlaylistNasVisibility), nameof(PlaylistUploadVisibility) }) OnPropertyChanged(name);
        BusyChanged();
    }
    protected override void BusyChanged()
    {
        base.BusyChanged();
        foreach (var name in new[] { nameof(CanSave), nameof(CanDelete), nameof(CanSort), nameof(CanChooseDirectory) }) OnPropertyChanged(name);
        NewLibraryCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ScanAllCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        RootsCommand.NotifyCanExecuteChanged();
        ChooseDirectoryCommand.NotifyCanExecuteChanged();
        CloseDirectoriesCommand.NotifyCanExecuteChanged();
        SortCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SaveOrderCommand.NotifyCanExecuteChanged();
        PickPlaylistCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        base.Cancel();
        _playlist = null;
        _uploadTempId = null;
        PlaylistText = "";
    }
}
