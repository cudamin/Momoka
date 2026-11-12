using Microsoft.UI;
using Microsoft.UI.Xaml;
using Momoka.FnNas;
using Windows.Storage.Pickers;

namespace Momoka.Shell.Views;

internal static class FnNasSubtitleFile
{
    internal static async Task<FnNasSubtitleUpload?> PickAsync(XamlRoot root, Func<bool> isCurrent)
    {
        void CheckCurrent()
        {
            if (!isCurrent()) throw new OperationCanceledException("字幕上传所属的页面或媒体版本已改变");
        }
        CheckCurrent();
        var window = root.ContentIslandEnvironment.AppWindowId;
        if (window.Value == 0) throw new InvalidOperationException("无法打开这个窗口的文件选择器");
        var picker = new FileOpenPicker();
        foreach (var extension in FnNasSubtitleInput.Extensions) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(window));
        var file = await picker.PickSingleFileAsync();
        CheckCurrent();
        if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        CheckCurrent();
        if (properties.Size is 0 or > FnNasSubtitleInput.MaxUploadBytes)
            throw new ArgumentException("字幕文件须大于 0 且不超过 32 MiB");
        using var stream = await file.OpenReadAsync();
        CheckCurrent();
        if (stream.Size is 0 or > FnNasSubtitleInput.MaxUploadBytes)
            throw new ArgumentException("字幕文件须大于 0 且不超过 32 MiB");
        var bytes = new byte[(int)stream.Size];
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        var read = await reader.LoadAsync((uint)bytes.Length);
        CheckCurrent();
        if (read != bytes.Length) throw new IOException("字幕文件未读取完整，请重新选择");
        reader.ReadBytes(bytes);
        var upload = new FnNasSubtitleUpload(file.Name, bytes);
        FnNasSubtitleInput.ValidateUpload(upload);
        return upload;
    }
}
