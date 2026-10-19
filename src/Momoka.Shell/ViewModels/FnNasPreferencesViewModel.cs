using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 飞牛影视「帐号 - 播放偏好」。官方页面只有首选字幕语言与首选音频语言两项，保存在服务器用户资料里；
/// 值为空串表示「任意语言」。页面一次提交两项，避免像网页那样每改一格就发一趟请求。
/// </summary>
public sealed partial class FnNasPreferencesViewModel : FnNasSessionViewModel
{
    public ObservableCollection<FnNasChoice> Languages { get; } = [];
    [ObservableProperty] public partial string SubtitleLanguage { get; set; } = "any";
    [ObservableProperty] public partial string AudioLanguage { get; set; } = "any";
    [ObservableProperty] public partial bool Loaded { get; set; }
    public bool CanSave => CanUse && Loaded;
    protected override string DeniedTitle => "需要连接飞牛影视";
    protected override string DeniedMessage => "请在服务器中连接飞牛影视后打开帐号设置。";

    protected override void ResetEditor()
    {
        Loaded = false;
        SubtitleLanguage = AudioLanguage = "any";
        Languages.Clear();
    }

    public override Task ReloadAsync() => RunAsync("读取播放偏好失败", async (scope, token) =>
    {
        var user = await scope.ExecuteAsync((client, ct) => client.FnNas.GetUserAsync(client.FnNasConnection, ct), token);
        var config = await scope.ExecuteAsync((client, ct) => client.FnNas.GetConfigurationAsync(client.FnNasConnection.ApiBase, ct), token);
        if (!Current(token)) return;

        var subtitle = LanguageCode(FnNasJson.Text(user, "lan"));
        var audio = LanguageCode(FnNasJson.Text(user, "audio_lan"));
        var supported = FnNasJson.Strings(config["supported_languages"]);
        Languages.Clear();
        Languages.Add(new FnNasChoice("any", FnNasJson.LanguageName("any")));
        foreach (var code in supported.Append(subtitle).Append(audio).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal))
        {
            if (!Languages.Any(choice => string.Equals(choice.Id, code, StringComparison.Ordinal)))
                Languages.Add(new FnNasChoice(code, FnNasJson.LanguageName(code)));
        }

        SubtitleLanguage = subtitle;
        AudioLanguage = audio;
        Loaded = true;
    });

    private static string LanguageCode(string value) =>
        value.Length == 0 || string.Equals(value, "any", StringComparison.OrdinalIgnoreCase) ? "any" : value;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("保存播放偏好失败", async (scope, token) =>
    {
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["lan"] = SubtitleLanguage is null or "any" ? "" : SubtitleLanguage,
            ["audio_lan"] = AudioLanguage is null or "any" ? "" : AudioLanguage
        };
        await scope.ExecuteAsync((client, ct) => client.FnNas.UpdateUserInfoAsync(client.FnNasConnection, body, ct), token);
        if (Current(token)) Notify("已保存", "首选字幕和音频语言已更新到飞牛影视服务器。", InfoBarSeverity.Success);
    });

    partial void OnLoadedChanged(bool value) => BusyChanged();

    protected override void BusyChanged()
    {
        base.BusyChanged();
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
