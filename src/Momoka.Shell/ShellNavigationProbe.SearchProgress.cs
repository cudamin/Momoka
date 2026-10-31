using System.Net;
using System.Text;
using System.Threading.Channels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSearchProgressAsync()
    {
        var app = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var originalBounds = new Windows.Graphics.RectInt32(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
        var server = new SearchProgressServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var view = new MoviePilotVersionsView();
        _root.Children.Add(view);
        Task? search = null;
        try
        {
            view.Attach(service, fixture.Launcher, sharedInput: true);
            search = view.SearchAsync("罗小黑战记");
            await UntilAsync(() => server.Stream is not null && view.ViewModel.SearchProgress.IsActive);
            await LayoutAsync(view);
            var card = Get<MoviePilotSearchProgressView>(view, "SearchProgress");
            var ring = Get<ProgressRing>(card, "SearchRing");
            var bar = Get<ProgressBar>(card, "SearchBar");
            Require(ring.IsActive && ring.IsIndeterminate && bar.IsIndeterminate, "服务端进度到达前应显示不确定动画");
            server.Stream!.Send("{\"type\":\"progress\",\"value\":54,\"text\":\"已完成 9 / 15 个搜索来源\"}");
            await UntilAsync(() => view.ViewModel.SearchProgress.Value == 54);
            await LayoutAsync(view);
            Require(!ring.IsIndeterminate && ring.Value == 54 && bar.Value == 54, "真实环形和横向进度没有同步服务端数值");
            Require(Get<TextBlock>(card, "ProgressPercent").Text == "54%"
                && Get<TextBlock>(card, "ProgressStatus").Text.Contains("9 / 15", StringComparison.Ordinal), "进度文案未更新");
            var scale = _root.XamlRoot?.RasterizationScale ?? 1;
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                ResizeInspect((int)Math.Round(1040 * scale), (int)Math.Round(760 * scale));
                await LayoutAsync(view);
                await SaveProbeScreenAsync($"mp-search-progress-{theme.Id}-1040-screen.png");
            }
            ResizeInspect((int)Math.Round(520 * scale), (int)Math.Round(760 * scale));
            await LayoutAsync(view);
            var width = view.ActualWidth;
            var percent = Get<TextBlock>(card, "ProgressPercent");
            var edge = percent.TransformToVisual(view).TransformPoint(default).X + percent.ActualWidth;
            Require(edge <= width && edge > 0, "窄窗百分比超出卡片边界");
            await SaveProbeScreenAsync("mp-search-progress-520-screen.png");
            // A known bad geometry must be rejected by the same boundary check.
            Require(!(edge + width <= width), "进度边界守卫未拒绝越界读数");
            server.Stream.Send("{\"type\":\"done\",\"value\":100,\"text\":\"搜索完成\",\"items\":[],\"total_items\":0}");
            server.Stream.Complete();
            await search;
            await LayoutAsync(view);
            Require(!view.ViewModel.SearchProgress.IsActive && !ring.IsActive && view.ViewModel.ShowEmptyNotice,
                "完成后应停止动画并切到空结果状态");
            Require(fixture.Actions.Plays == 0 && server.Writes == 0, "搜索进度不应写服务器或播放");
        }
        finally
        {
            view.Release();
            server.Stream?.Complete();
            if (search is not null) await search;
            _root.Children.Remove(view);
            app.MoveAndResize(originalBounds);
            ThemeHost.Apply(_options.Theme ?? "midnight");
        }
    }

    private sealed class SearchProgressServer : HttpMessageHandler
    {
        internal ProgressEventStream? Stream { get; private set; }
        internal int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "进度探针拒绝真实服务器");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal))
                return Task.FromResult(Json(new { access_token = "fixture" }));
            if (request.Method != HttpMethod.Get) Writes++;
            if (path.EndsWith("/search/title/stream", StringComparison.Ordinal))
            {
                Stream = new ProgressEventStream();
                var content = new StreamContent(Stream);
                content.Headers.ContentType = new("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ProgressEventStream : Stream
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _offset;
        internal void Send(string json) => _messages.Writer.TryWrite(Encoding.UTF8.GetBytes("data: " + json + "\n\n"));
        internal void Complete() => _messages.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current is null || _offset == _current.Length)
            {
                if (!await _messages.Reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!_messages.Reader.TryRead(out _current)) continue;
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
