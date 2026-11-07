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
        foreach (var test in new[] { false, true })
        {
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new UserNotificationInfo { NotifierKey = "anonymous" };
            var context = new NotificationEditorContext(entry, false, [], [], [], [], _ => pending.Task, async _ => { await pending.Task; return true; });
            var dialog = new NotificationEditDialog(context);
            if (test)
                typeof(NotificationEditDialog).GetMethod("OnTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new Microsoft.UI.Xaml.RoutedEventArgs()]);
            else dialog.RaisePrimary();
            dialog.Hide();
            if (dialog.Hidden) throw new InvalidOperationException("R15-004 in-flight notification dialog closed: " + test);
            pending.SetResult();
            await Task.Delay(20);
            dialog.Hide();
            if (!dialog.Hidden) throw new InvalidOperationException("R15-004 settled dialog cannot close: " + test);
        }
        var failures = new List<string>();
        foreach (var mode in new[] { "normal", "fail", "missing", "empty", "edit", "unknown-event" })
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
            if (mode == "unknown-event") entry.EventIds.Add("unlisted-event");
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
            var expectedEvents = mode == "edit" ? new[] { "start" } : mode == "unknown-event" ? ["stop", "unlisted-event"] : ["stop"];
            if (!saved["EventIds"]!.AsArray().Select(value => value?.GetValue<string>()).Order().SequenceEqual(expectedEvents.Order())) failures.Add(mode + " lost event identity");
            foreach (var (key, id) in new[] { ("UserIds", "u1"), ("LibraryIds", "l1"), ("DeviceIds", "d1") })
                if (saved[key]!.ToJsonString() != (mode == "edit" ? "[]" : "[\"" + id + "\"]")) failures.Add(mode + " changed " + key);
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("R30/R31: 6 linked notification editor/HTTP cases passed.");
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
