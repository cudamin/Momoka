using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

internal static class NotificationEditor
{
    public static async Task RunAsync()
    {
        var failures = new List<string>();
        foreach (var mode in new[] { "normal", "fail", "missing", "empty", "edit" })
        {
            using var handler = new Capture();
            using var http = new EmbyHttp(handler);
            var client = new EmbyClient(http, new EmbyConnection(new Uri("https://notification.invalid/emby/"), "anonymous", "user", "anonymous", "anonymous", DeviceIdentity.Create("anonymous", "1")));
            async Task<IReadOnlyList<T>?> Load<T>(List<T> values) => await NotificationFetch.Fetch<T>(_ => mode == "fail"
                ? Task.FromException<List<T>>(new HttpRequestException("anonymous failure")) : Task.FromResult(mode == "empty" ? new List<T>() : values));
            var suffix = mode == "missing" ? "2" : "1";
            var users = await Load<NotificationUser>([new() { Id = "u" + suffix, Name = "User" }]);
            var libraries = await Load<NotificationLibrary>([new() { Guid = "l" + suffix, Name = "Library" }]);
            var devices = await Load<NotificationDevice>([new() { Id = "d" + suffix, Name = "Device" }]);
            var entry = new UserNotificationInfo { NotifierKey = "anonymous", EventIds = ["stop"], UserIds = ["u1"], LibraryIds = ["l1"], DeviceIds = ["d1"] };
            var context = new NotificationEditorContext(entry.Clone(), false,
                [new() { Id = "playback", Events = [new() { Id = "stop", Name = "Stop" }, new() { Id = "start", Name = "Start" }] }],
                users!, libraries!, devices!, value => client.SaveNotificationAsync(value, default), _ => Task.FromResult(true));
            var dialog = new NotificationEditDialog(context);
            dialog.NameBox.Text = "renamed";
            if (mode == "edit")
            {
                var checks = dialog.EventList.Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<CheckBox>().ToList();
                checks[1].IsChecked = false;
                checks[2].IsChecked = true;
                foreach (var check in dialog.FilterList.Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<CheckBox>()) check.IsChecked = false;
            }
            dialog.RaisePrimary();
            await handler.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var saved = handler.Body!;
            if (saved["EventIds"]!.ToJsonString() != (mode == "edit" ? "[\"start\"]" : "[\"stop\"]")) failures.Add(mode + " lost event identity");
            foreach (var (key, id) in new[] { ("UserIds", "u1"), ("LibraryIds", "l1"), ("DeviceIds", "d1") })
                if (saved[key]!.ToJsonString() != (mode == "edit" ? "[]" : "[\"" + id + "\"]")) failures.Add(mode + " changed " + key);
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("R30/R31: 5 linked notification editor/HTTP cases passed.");
    }
    private sealed class Capture : HttpMessageHandler
    {
        public JsonObject? Body { get; private set; }
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "notification.invalid" || request.Method != HttpMethod.Post) throw new InvalidOperationException("Unexpected request");
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Saved.TrySetResult();
            return new(HttpStatusCode.NoContent) { Content = new StringContent("", Encoding.UTF8) };
        }
    }
}
