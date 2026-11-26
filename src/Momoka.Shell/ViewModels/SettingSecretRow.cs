using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Shell.Localization;

namespace Momoka.Shell.ViewModels;

/// <summary>密钥输入只接收新值，不解密回填。保存失败保留草稿，成功立即清掉输入框。</summary>
public sealed partial class SettingSecretRow : SettingRow
{
    private const string StorageNote = "密钥用 Windows DPAPI 加密保存在本机，不包含在配置备份中；保存后不会回填明文。";
    private readonly Action<string> _write;
    private readonly Func<bool> _stored;
    private readonly Func<bool> _save;
    private readonly Action _after;
    private readonly Func<bool>? _canWrite;
    private bool _unsaved;
    private string? _protectionError;

    internal SettingSecretRow(string label, Action<string> write, Func<bool> stored, Func<bool> save, Action after)
        : this(label, write, stored, save, after, null)
    {
    }

    internal SettingSecretRow(string label, Action<string> write, Func<bool> stored, Func<bool> save, Action after, Func<bool>? canWrite)
        : base(label, StorageNote)
    {
        _write = write;
        _stored = stored;
        _save = save;
        _after = after;
        _canWrite = canWrite;
    }

    [ObservableProperty]
    public partial string Draft { get; set; } = "";

    public string Status => UiText.Translate(_protectionError ?? (_unsaved ? "尚未保存到磁盘，请重试；当前运行保留本次修改" : _stored() ? "已保存密钥；输入新值可替换" : "尚未保存密钥"));

    /// <summary>密钥行的两颗按钮不再写死「弹弹play」：这一行属于哪张卡，自动化名就跟着那行的标签走。</summary>
    public string SaveAutomationName => UiText.Format("保存{0}", DisplayLabel);

    public string ClearAutomationName => UiText.Format("清除{0}", DisplayLabel);

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
        if (_canWrite?.Invoke() == false) return;
        try
        {
            _write(value);
            _protectionError = null;
            Restate(StorageNote);
            _unsaved = !_save();
            if (!_unsaved) Draft = "";
            OnPropertyChanged(nameof(Status));
            _after();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            _protectionError = "无法加密密钥，尚未保存。请检查 Windows 用户环境后重试。";
            Restate(_protectionError);
            OnPropertyChanged(nameof(Status));
        }
    }
}
