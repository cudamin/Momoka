using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

internal static class MetadataPlan
{
    public static Func<ContentDialog, Task<ContentDialogResult>> OnShow { get; set; } = _ => Task.FromResult(ContentDialogResult.None);
}
internal static class MetadataEditor
{
    public static async Task RunAsync()
    {
        var errors = new List<string>();
        foreach (var mode in new[] { "unchanged", "title", "list", "failure", "cancel", "permission" })
        {
            using var fixture = new Fixture();
            MetadataDialog? shown = null;
            MetadataPlan.OnShow = async dialog =>
            {
                shown = (MetadataDialog)dialog;
                if (mode == "cancel") return ContentDialogResult.None;
                if (mode is "title" or "failure") shown.NameBox.Text = "User draft";
                if (mode == "permission")
                {
                    shown.NameBox.Text = "Rejected draft";
                    fixture.Permissions = new(IsAdministrator: false);
                    await shown.SaveAsync();
                    if (!shown.IsOpen || shown.NameBox.Text != "Rejected draft" || !shown.Problem.IsOpen)
                        errors.Add("permission rejection discarded draft");
                    return ContentDialogResult.None;
                }
                if (mode == "list") shown.TagsBox.Text = "\"Sci-Fi, Fantasy\"、Other";
                if (mode == "failure")
                {
                    fixture.Transport.Fail = true;
                    await shown.SaveAsync();
                    if (!shown.IsOpen || shown.NameBox.Text != "User draft" || !shown.Problem.IsOpen) errors.Add("failed save discarded draft");
                    fixture.Transport.Fail = false;
                    return ContentDialogResult.Primary;
                }
                await Task.CompletedTask;
                return ContentDialogResult.Primary;
            };
            await ItemCommands.Run(fixture.Scope, fixture, new FrameworkElement(), new CardItem { Item = new EmbyItem { Id = "item", Type = EmbyItemType.Movie } }, () => fixture.Changed++);
            var expectedWrites = mode is "unchanged" or "cancel" or "permission" ? 0 : mode == "failure" ? 2 : 1;
            if (fixture.Transport.Writes != expectedWrites) errors.Add(mode + " unexpected write count " + fixture.Transport.Writes);
            if (mode == "permission" && (fixture.Changed != 0 || fixture.PermissionScopes.Count != 1
                || !ReferenceEquals(fixture.PermissionScopes.Single(), fixture.Scope))) errors.Add("permission check lost command source");
            if (mode is "title" or "list" or "failure")
            {
                var expectedTags = mode == "list" ? "[\"Sci-Fi, Fantasy\",\"Other\"]" : "[\"Sci-Fi, Fantasy\"]";
                if (fixture.Transport.Submitted?["Tags"]?.ToJsonString() != expectedTags) errors.Add(mode + " changed punctuation inside a tag");
                if (fixture.Changed != 1 || shown!.IsOpen) errors.Add(mode + " did not finish one confirmed save");
            }
        }
        foreach (var text in new[] { "with, comma", "with、separator", "quote \"inside\"", "semicolon;inside" })
        {
            var item = new JsonObject { ["Name"] = "item", ["Tags"] = new JsonArray(text) };
            var form = ItemMetadataEdit.Read(item);
            if (form.ApplyTo(item)) errors.Add("punctuation no-op changed JSON");
            var edit = form with { Tags = form.Tags + "、added" };
            edit.ApplyTo(item);
            if (item["Tags"]!.AsArray().Count != 2 || item["Tags"]![0]!.GetValue<string>() != text) errors.Add("quoted list edit lost punctuation");
        }
        if ((new ItemMetadataEdit { Name = "item", Tags = "\"unfinished" }).Problem is null) errors.Add("unclosed list quote was accepted");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        Console.WriteLine("R24/R25: 6 actual command and linked metadata dialog cases passed.");
    }
    private sealed class Fixture : IDisposable, IShellActions
    {
        public Capture Transport { get; } = new();
        public EmbySession Session { get; }
        public EmbySessionScope Scope { get; }
        public int Changed;
        internal ItemMenuPermissions Permissions { get; set; }
        internal List<EmbySessionScope> PermissionScopes { get; } = [];
        public ItemMenuPermissions MenuPermissionsFor(EmbySessionScope scope)
        {
            PermissionScopes.Add(scope);
            return Permissions;
        }
        public Fixture()
        {
            var device = DeviceIdentity.Create("anonymous", "1");
            var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N")));
            Session = new EmbySession(new AppSettings(), new SettingsStore(paths, PassthroughSecretProtector.Instance), new CredentialVault(PassthroughSecretProtector.Instance), device, Transport);
            var connection = new EmbyConnection(new Uri("https://metadata.invalid/"), "anonymous", "user", "anonymous", "anonymous", device) { ServerKind = MediaServerKind.Jellyfin, ServerVersion = "10.10.7", IsAdministrator = true };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "server", Url = "https://metadata.invalid/", ServerKind = MediaServerKind.Jellyfin }, new AccountProfile { Id = "account" });
            Scope = new EmbySessionScope(Session, identity);
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Scope);
        }
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) { }
        public void Dispose() => Session.Dispose();
    }
    private sealed class Capture : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public int Writes { get; private set; }
        public JsonObject? Submitted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "metadata.invalid") throw new InvalidOperationException("Unexpected metadata host");
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent("{\"Id\":\"item\",\"Name\":\"Original\",\"Tags\":[\"Sci-Fi, Fantasy\"]}", Encoding.UTF8, "application/json") };
            Writes++;
            Submitted = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return new(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
        }
    }
}
