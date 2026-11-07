using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

/// <summary>
/// 飞牛的「编辑元数据」表。四格的值和校验归 <see cref="FnNasMetadataEdit"/>，这里只把文字在框和
/// 记录之间搬来搬去；保存时整份 <c>getEditDetail</c> 对象原样交回，只改这四格。
/// </summary>
public sealed partial class FnNasMetadataDialog : ContentDialog
{
    private readonly JsonObject _original;
    private readonly Func<JsonObject, Task> _save;
    private bool _saving;
    public bool Changed { get; private set; }

    public FnNasMetadataDialog(JsonObject detail, Func<JsonObject, Task> save)
    {
        InitializeComponent();

        // 对话框摆在 XamlRoot 的浮层根上，继承不到提出它的那棵树；不点明就按 Windows 应用模式来。
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _original = (JsonObject)detail.DeepClone();
        _save = save;
        Closing += (_, args) => { if (_saving) args.Cancel = true; };

        Edit = FnNasMetadataEdit.Read(detail);
        NameBox.Text = Edit.Name;
        OverviewBox.Text = Edit.Overview;
        RatingBox.Text = Edit.Rating;
        ReleaseBox.Text = Edit.ReleaseDate;
    }

    /// <summary>表上最后通过校验的那一份；保存前读到的一定是用户眼下看到的。</summary>
    public FnNasMetadataEdit Edit { get; private set; }

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_saving) return;
        var edit = new FnNasMetadataEdit(NameBox.Text, OverviewBox.Text, RatingBox.Text, ReleaseBox.Text, Edit.ReleaseKey);
        if (edit.Problem is { } problem)
        {
            // 校验不过时别关表：已经关掉的表会把用户刚敲的字一起扔掉。
            args.Cancel = true;
            Problem.Message = problem;
            Problem.IsOpen = true;
            return;
        }

        Problem.IsOpen = false;
        var body = (JsonObject)_original.DeepClone();
        if (!edit.ApplyTo(body)) { Hide(); return; }
        var deferral = args.GetDeferral();
        _saving = true;
        IsPrimaryButtonEnabled = false;
        try
        {
            await _save(body).ConfigureAwait(true);
            Edit = edit;
            Changed = true;
            _saving = false;
            Hide();
        }
        catch (Exception error)
        {
            Problem.Message = $"保存失败：{Momoka.Diagnostics.Failure.Describe(error)}。输入已保留，可以重试或取消。";
            Problem.IsOpen = true;
        }
        finally
        {
            _saving = false;
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }
}
