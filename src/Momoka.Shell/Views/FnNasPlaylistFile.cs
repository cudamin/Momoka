using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace Momoka.Shell.Views;

internal readonly record struct PickedFnNasPlaylist(byte[] Bytes, string FileName);

internal static class FnNasPlaylistFile
{
    internal static async Task<PickedFnNasPlaylist?> PickAsync(XamlRoot root)
    {
        var window = root.ContentIslandEnvironment.AppWindowId;
        if (window.Value == 0) throw new InvalidOperationException("无法打开这个窗口的文件选择器。");
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".m3u", ".m3u8", ".txt" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(window));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size is 0 or > 10 * 1024 * 1024) throw new ArgumentException("播放列表文件须大于 0 且不超过 10 MB。");
        using var stream = await file.OpenReadAsync();
        if (stream.Size is 0 or > 10 * 1024 * 1024) throw new ArgumentException("播放列表文件须大于 0 且不超过 10 MB。");
        var bytes = new byte[(int)stream.Size];
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return new PickedFnNasPlaylist(bytes, file.Name);
    }
}
