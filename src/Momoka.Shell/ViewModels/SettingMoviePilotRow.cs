using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// The MoviePilot card's working state: the switch, the two boxes, the button, and whatever the last test
/// said.
/// <para>
/// Unlike every other row on this page this one holds several controls that belong to one question, which is
/// why it is a single row rather than four. Four separate rows would each write the settings file on their
/// own and the button would have to live in a fifth — 「测试连接」 needs the address and the password that
/// were typed, and a button does not know what the boxes beside it contain unless something owns them all.
/// The <see cref="SettingHomeLayoutRow"/> precedent applies: when a setting is really one co-ordinated thing,
/// one row that owns the whole thing beats several rows that have to agree.
/// </para>
/// <para>
/// It writes through delegates handed in by the view model rather than touching settings itself, the same
/// read/write-pair shape every other row uses — a renamed setting stays a build error.
/// </para>
/// </summary>
public sealed partial class SettingMoviePilotRow : SettingRow
{
    private readonly Action<bool> _writeEnabled;
    private readonly Action<string> _writeUrl;
    private readonly Action<string> _writeUsername;
    private readonly Action<string> _writePassword;
    private readonly Action _save;
    private readonly Func<CancellationToken, Task<string>> _test;

    /// <summary>
    /// Set last in the constructor, after the seeded fields are in place, so handing the row its starting
    /// values does not write them straight back to the settings file. Same flag, same reason, as
    /// <see cref="SettingChoiceRow"/>'s.
    /// </summary>
    private readonly bool _seeded;

    /// <summary>True while a test is in flight, so the button disables itself instead of queueing calls.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestLabel))]
    [NotifyPropertyChangedFor(nameof(CanTest))]
    public partial bool Testing { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>
    /// The protocol picker, as the index the ComboBox binds its <c>SelectedIndex</c> to: 0 = http, 1 = https.
    /// An index rather than a string because an inline-item ComboBox selects by position, and this stays the
    /// one place that maps the two back to a scheme — see <see cref="Url"/>.
    /// </summary>
    [ObservableProperty]
    public partial int SchemeIndex { get; set; }

    /// <summary>The host box — just the address, no scheme and no port.</summary>
    [ObservableProperty]
    public partial string Host { get; set; }

    /// <summary>
    /// The API-port box（标注为「API 端口」，不是网页界面那个端口）. Empty means 「用默认 3001」 —
    /// <see cref="MoviePilotAddress"/> fills the default in when the address is actually called, so a blank box
    /// and 3001 reach the same server.
    /// </summary>
    [ObservableProperty]
    public partial string Port { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>
    /// The password as typed. Never read back out of the settings file to fill this in — the stored form is
    /// wrapped and the point of a password box is that the text is not on screen. The card says whether one
    /// is saved instead, which is the fact the user actually needs.
    /// </summary>
    [ObservableProperty]
    public partial string Password { get; set; }

    /// <summary>The last test's verdict, already written for a person to read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultVisibility))]
    public partial string Result { get; set; }

    /// <summary>Whether the last test succeeded, which only decides how the verdict is coloured.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultBrush))]
    public partial bool ResultOk { get; set; }

    /// <summary>
    /// Told by the view model after it wraps a password, because the wrap is what decides this and the row
    /// never touches the settings object itself.
    /// </summary>
    internal void MarkPasswordSaved(bool saved) => HasSavedPassword = saved;

    /// <summary>
    /// True when a wrapped password is on file. Shown as 「已保存」 beside the box, because an empty password
    /// box is otherwise ambiguous between 「none saved」 and 「I cleared it」.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedPasswordVisibility))]
    public partial bool HasSavedPassword { get; set; }

    public SettingMoviePilotRow(
        string label,
        string note,
        bool enabled,
        string url,
        string username,
        bool hasSavedPassword,
        Action<bool> writeEnabled,
        Action<string> writeUrl,
        Action<string> writeUsername,
        Action<string> writePassword,
        Action save,
        Func<CancellationToken, Task<string>> test)
        : base(label, note)
    {
        Enabled = enabled;

        // The one stored string arrives split into the three boxes; composing it back is the Url getter's job.
        // http unless the stored scheme is explicitly https — the same default MoviePilotAddress applies everywhere.
        var parts = MoviePilotAddress.Split(url);
        SchemeIndex = parts.Scheme == "https" ? 1 : 0;
        Host = parts.Host;
        Port = parts.Port;

        Username = username;
        Password = "";
        HasSavedPassword = hasSavedPassword;
        Result = "";
        _writeEnabled = writeEnabled;
        _writeUrl = writeUrl;
        _writeUsername = writeUsername;
        _writePassword = writePassword;
        _save = save;
        _test = test;
        _seeded = true;
    }

    /// <summary>What the button says. While a test runs it is the progress, because a button that still says
    /// 「测试连接」 with a spinner somewhere else is a button the user presses twice.</summary>
    public string TestLabel => Testing ? "正在测试…" : "测试连接";

    /// <summary>
    /// The button greys out while a test is in flight. A property rather than a converter, because the page
    /// has no inverse-bool converter and inventing one to save four lines is not a trade this page makes.
    /// </summary>
    public bool CanTest => !Testing;

    /// <summary>Whether the 「已保存一个密码」 line shows, as the <c>Visibility</c> a template can bind to.</summary>
    public Visibility SavedPasswordVisibility => HasSavedPassword ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultVisibility => Result.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The verdict's colour. Two brushes chosen here rather than a converter, which is the rule this page
    /// follows everywhere else — see <see cref="SettingSubtitlePreviewRow.BrushFor"/>.
    /// </summary>
    public Microsoft.UI.Xaml.Media.Brush ResultBrush => ResultOk
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x5D, 0xC4, 0x8A))
        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE0, 0x6C, 0x75));

    /// <summary>
    /// The three boxes composed back into the one stored address — what 测试连接 normalises and what the
    /// settings file keeps. Named <c>Url</c> still because that is the one setting behind it; the view model
    /// reads it to run a test and never has to know it is three controls on screen.
    /// </summary>
    public string Url => MoviePilotAddress.Join(SchemeIndex == 1 ? "https" : "http", Host, Port);

    partial void OnEnabledChanged(bool value)
    {
        if (!_seeded) return;
        _writeEnabled(value);
        _save();
    }

    partial void OnSchemeIndexChanged(int value) => WriteAddress();

    partial void OnHostChanged(string value) => WriteAddress();

    partial void OnPortChanged(string value) => WriteAddress();

    /// <summary>
    /// Any of the three address boxes changed: compose them into the stored string and write it, the same
    /// seam the single box used before it became three — the setting is still one string.
    /// </summary>
    private void WriteAddress()
    {
        if (!_seeded) return;
        _writeUrl(Url);
        _save();
    }

    partial void OnUsernameChanged(string value)
    {
        if (!_seeded) return;
        _writeUsername(value);
        _save();
    }

    partial void OnPasswordChanged(string value)
    {
        if (!_seeded) return;
        _writePassword(value);
        _save();

        // Typing a password makes 「已保存」 true a moment later, when the wrap lands — the row is told by
        // the view model rather than guessing, because only the view model knows the wrap succeeded.
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (Testing) return;

        Testing = true;
        ResultOk = false;
        Result = "";

        try
        {
            Result = await _test(CancellationToken.None).ConfigureAwait(true);
            ResultOk = true;
        }
        catch (Exception error)
        {
            // Every failure this can produce is already phrased for a person — the client's exceptions
            // carry the server's own message. A crash here would be a bug; an error message would not.
            Result = error.Message;
            ResultOk = false;
        }
        finally
        {
            Testing = false;
        }
    }
}
