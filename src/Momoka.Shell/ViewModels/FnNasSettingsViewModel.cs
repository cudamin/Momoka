using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasSettingsViewModel : FnNasAdminViewModel
{
    private JsonObject? _original;
    public ObservableCollection<FnNasChoice> Languages { get; } = [];
    public ObservableCollection<FnNasChoice> Volumes { get; } = [];
    [ObservableProperty] public partial string ServerName { get; set; } = "";
    [ObservableProperty] public partial string Language { get; set; } = "";
    [ObservableProperty] public partial bool FileMonitor { get; set; }
    [ObservableProperty] public partial string MetadataVolume { get; set; } = "";
    [ObservableProperty] public partial string PlaybackCacheVolume { get; set; } = "";
    [ObservableProperty] public partial bool Rebuilding { get; set; }
    public bool CanSave => CanUse && _original is not null;
    public bool CanRebuild => CanSave && !Rebuilding;
    public string IndexStatus => Rebuilding ? "正在重建索引，可稍后刷新查看进度。" : "重新生成搜索索引，不会删除媒体文件。";

    protected override void ResetEditor() => _original = null;

    public override Task ReloadAsync() => RunAsync("读取飞牛影视设置失败", async (scope, token) =>
    {
        var settings = await scope.ExecuteAsync((client, ct) => client.FnNas.GetServerSettingsAsync(CurrentConnection(scope, client), ct), token);
        var configuration = await scope.ExecuteAsync((client, ct) => client.FnNas.GetConfigurationAsync(client.Connection.ApiBase, ct), token);
        var storage = await scope.ExecuteAsync((client, ct) => client.FnNas.GetStorageVolumesAsync(CurrentConnection(scope, client), ct), token);
        var index = await scope.ExecuteAsync((client, ct) => client.FnNas.GetSearchIndexStatusAsync(CurrentConnection(scope, client), ct), token);
        if (!Current(token)) return;
        _original = FnNasJson.Copy(settings);
        ServerName = FnNasJson.Text(settings, "name");
        Language = FnNasJson.Text(settings, "lan");
        FileMonitor = FnNasJson.Flag(settings, "file_monitor");
        Rebuilding = FnNasJson.Flag(index, "rebuilding");
        MetadataVolume = FnNasJson.Text(settings, "meta_dir_vol_index");
        PlaybackCacheVolume = FnNasJson.Text(settings, "mediasrv_cache_vol_index");
        Volumes.Clear();
        if (storage["vols"] is JsonArray volumes)
        {
            foreach (var volume in volumes.OfType<JsonObject>())
            {
                var id = FnNasJson.Text(volume, "index");
                var state = FnNasJson.Number(volume, "state");
                var stateText = state == 0 ? "" : state == -1 ? "（已损坏）" : "（降级）";
                Volumes.Add(new FnNasChoice(id, $"存储空间 {id}{stateText}"));
            }
        }
        PreserveVolume(MetadataVolume);
        PreserveVolume(PlaybackCacheVolume);
        Languages.Clear();
        foreach (var language in FnNasJson.Strings(configuration["supported_languages"]).Append(Language).Distinct())
            if (language.Length > 0) Languages.Add(new FnNasChoice(language, FnNasJson.LanguageName(language)));
    });

    private void PreserveVolume(string value)
    {
        if (value.Length > 0 && !Volumes.Any(choice => choice.Id == value)) Volumes.Add(new FnNasChoice(value, $"存储空间 {value}（当前设置）"));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("保存设置未完成，请核对后重试", async (scope, token) =>
    {
        if (_original is not { } original) return;
        if (string.IsNullOrWhiteSpace(ServerName) || ServerName.Trim().Length > 32) throw new ArgumentException("服务器名称必须为 1～32 个字符。");
        var draft = FnNasJson.Copy(original);
        // 服务端要求完整配置。保留所有未知和未展示字段，尤其是转码选项。
        draft["name"] = ServerName.Trim();
        draft["lan"] = Language;
        draft["file_monitor"] = FileMonitor ? 1 : 0;
        if (int.TryParse(MetadataVolume, CultureInfo.InvariantCulture, out var metadata)) draft["meta_dir_vol_index"] = metadata;
        if (int.TryParse(PlaybackCacheVolume, CultureInfo.InvariantCulture, out var cache)) draft["mediasrv_cache_vol_index"] = cache;
        await scope.ExecuteAsync((client, ct) => client.FnNas.UpdateServerSettingsAsync(CurrentConnection(scope, client), draft, ct), token);
        if (!Current(token)) return;
        _original = draft;
        Notify("已保存", "飞牛影视服务器设置已更新。", InfoBarSeverity.Success);
    });

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private Task RebuildAsync() => !CanRebuild ? Task.CompletedTask : RunAsync("重建索引失败", async (scope, token) =>
    {
        if (!await ConfirmAsync("重建搜索索引", "确定重建飞牛影视的搜索索引吗？重建期间搜索结果可能暂时不完整。", "重建") || !Current(token)) return;
        await scope.ExecuteAsync((client, ct) => client.FnNas.RebuildSearchIndexAsync(CurrentConnection(scope, client), ct), token);
        if (!Current(token)) return;
        Rebuilding = true;
        Notify("已开始重建索引", "可稍后刷新查看状态。", InfoBarSeverity.Success);
    });

    partial void OnRebuildingChanged(bool value)
    {
        OnPropertyChanged(nameof(IndexStatus));
        BusyChanged();
    }
    protected override void BusyChanged()
    {
        base.BusyChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRebuild));
        SaveCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
    }
}
