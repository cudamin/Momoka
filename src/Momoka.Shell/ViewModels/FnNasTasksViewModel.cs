using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasTasksViewModel : FnNasAdminViewModel
{
    public ObservableCollection<FnNasTaskRow> Tasks { get; } = [];
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial bool Loaded { get; set; }
    public bool CanSave => CanUse && Loaded;

    protected override void ResetEditor()
    {
        Loaded = false;
        Tasks.Clear();
    }

    public override Task ReloadAsync() => RunAsync("读取任务计划失败", async (scope, token) =>
    {
        var settings = await scope.ExecuteAsync((client, ct) => client.FnNas.GetScheduleSettingsAsync(CurrentConnection(scope, client), ct), token);
        var tasks = await scope.ExecuteAsync((client, ct) => client.FnNas.GetScheduledTasksAsync(CurrentConnection(scope, client), ct), token);
        if (!Current(token)) return;
        Enabled = FnNasJson.Text(settings, "task_switch") == "1";
        Tasks.Clear();
        foreach (var task in tasks) Tasks.Add(new FnNasTaskRow(task));
        Loaded = true;
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("保存任务计划失败", async (scope, token) =>
    {
        var enabled = Enabled;
        await scope.ExecuteAsync((client, ct) => client.FnNas.SetScheduledTasksEnabledAsync(CurrentConnection(scope, client), enabled, ct), token);
        if (Current(token)) Notify("已保存", enabled ? "已开启每天凌晨的定时任务。" : "已关闭定时任务。", InfoBarSeverity.Success);
    });

    partial void OnLoadedChanged(bool value) => BusyChanged();
    protected override void BusyChanged()
    {
        base.BusyChanged();
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
