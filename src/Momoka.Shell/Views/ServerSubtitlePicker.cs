using Microsoft.UI;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Momoka.Shell.Views;

/// <summary>由当前对话框的窗口挑选字幕；保留原字节，大小在分配前和读取完成后分别核对。</summary>
internal static class ServerSubtitlePicker
{
    internal static async Task<ServerSubtitleFile?> PickAsync(XamlRoot root, Func<bool> isCurrent)
    {
        if (!isCurrent()) return null;
        if (root.ContentIslandEnvironment.AppWindowId is not { Value: not 0 } windowId)
            throw new InvalidOperationException("找不到字幕管理所属窗口，请重新打开。");
        var picker = new FileOpenPicker();
        foreach (var extension in ServerSubtitleFile.Extensions) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(windowId));
        var file = await picker.PickSingleFileAsync();
        if (file is null || !isCurrent()) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (!isCurrent()) return null;
        if (properties.Size is 0 or > ServerSubtitleFile.MaxBytes)
            throw new InvalidOperationException("字幕文件须大于 0 且不超过 10 MiB。");
        using var stream = await file.OpenReadAsync();
        if (!isCurrent()) return null;
        if (stream.Size is 0 or > ServerSubtitleFile.MaxBytes)
            throw new InvalidOperationException("字幕文件须大于 0 且不超过 10 MiB。");
        var bytes = new byte[(int)stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        if (!isCurrent()) return null;
        var loaded = await reader.LoadAsync((uint)bytes.Length);
        if (!isCurrent()) return null;
        if (loaded != bytes.Length || stream.Size != (ulong)bytes.Length)
            throw new InvalidOperationException("字幕文件在读取时已变化，请重新选择。");
        reader.ReadBytes(bytes);
        if (!isCurrent()) return null;
        return new ServerSubtitleFile(file.Name, bytes);
    }
}
