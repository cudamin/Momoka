using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Momoka.Shell.ViewModels;

/// <summary>密钥输入只接收新值，不解密回填。保存失败保留草稿，成功立即清掉输入框。</summary>
public sealed partial class SettingSecretRow : SettingRow
{
    private readonly Action<string> _write;
    private readonly Func<bool> _stored;
    private readonly Func<bool> _save;
    private readonly Action _after;
    private bool _unsaved;

    internal SettingSecretRow(string label, Action<string> write, Func<bool> stored, Func<bool> save, Action after)
        : base(label, "密钥用 Windows DPAPI 加密保存在本机，不包含在配置备份中；保存后不会回填明文。")
    {
        _write = write;
        _stored = stored;
        _save = save;
        _after = after;
    }

    [ObservableProperty]
    public partial string Draft { get; set; } = "";

    public string Status => _unsaved ? "尚未保存到磁盘，请重试；当前运行保留本次修改" : _stored() ? "已保存密钥；输入新值可替换" : "尚未保存密钥";

    /// <summary>密钥行的两颗按钮不再写死「弹弹play」：这一行属于哪张卡，自动化名就跟着那行的标签走。</summary>
    public string SaveAutomationName => $"保存{Label}";

    public string ClearAutomationName => $"清除{Label}";

    [RelayCommand]
    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(Draft)) return;
        Write(Draft);
    }

    [RelayCommand]
    private void Clear() => Write("");

    private void Write(string value)
    {
        try
        {
            _write(value);
            _unsaved = !_save();
            if (!_unsaved) Draft = "";
            OnPropertyChanged(nameof(Status));
            _after();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Restate("无法加密密钥，尚未保存。请检查 Windows 用户环境后重试。");
        }
    }
}
