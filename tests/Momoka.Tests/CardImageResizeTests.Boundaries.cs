#if CARD_IMAGE_RESIZE_FIXTURE
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Media.Imaging;

// These are value/bitmap boundaries only. CardItem.cs and PosterCache.cs are compile-linked unchanged;
// the real Core image store, SharedWork, cache-key/width rules and source lifetime all execute.
namespace Microsoft.UI.Xaml
{
#if !PAGE_CONTRACT_FIXTURE
    public enum Visibility { Visible, Collapsed }
#endif
    public enum TextAlignment { Left, Center }
    public enum GridUnitType { Auto, Pixel, Star }
    public readonly record struct GridLength(double Value, GridUnitType GridUnitType);
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double value) : this(value, value, value, value) { }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public enum Stretch { None, Fill, Uniform, UniformToFill }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage
    {
        public int DecodePixelWidth { get; init; }
        public string FixtureImage { get; init; } = "";
    }
}

namespace Momoka.Shell.Media
{
    // No WinRT decoder or real image is used. The recorded argument is the width passed by CardItem.
    internal static class PosterLoader
    {
        public static bool Hold { get; set; }
        public static Func<string, Exception?>? ErrorFor { get; set; }
        public static List<CardImageDecode> Calls { get; } = [];

        public static Task<BitmapImage?> DecodeAsync(byte[] bytes, int logicalWidth)
        {
            var call = new CardImageDecode(Encoding.UTF8.GetString(bytes), logicalWidth);
            Calls.Add(call);
            if (ErrorFor?.Invoke(call.Image) is { } error) call.Fail(error);
            else if (!Hold) call.Complete();
            return call.Result;
        }

        public static void Reset()
        {
            foreach (var call in Calls) call.Complete();
            Calls.Clear();
            Hold = false;
            ErrorFor = null;
        }
    }

    internal sealed class CardImageDecode(string image, int width)
    {
        private readonly TaskCompletionSource<BitmapImage?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Image { get; } = image;
        public int Width { get; } = width;
        public Task<BitmapImage?> Result => _result.Task;
        public void Fail(Exception error) => _result.TrySetException(error);
        public void Complete() => _result.TrySetResult(new BitmapImage { DecodePixelWidth = Width, FixtureImage = Image });
    }
}

namespace Momoka.Tests
{
    internal sealed class CardImageFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "fixtures", "r012", Guid.NewGuid().ToString("N"));
        private readonly EmbySession _session;
        private readonly SharedWork<byte[]?> _downloads;
        private readonly List<Task> _loads = [];
        private static readonly FieldInfo LoadingField = typeof(CardItem).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public CardImageHandler Handler { get; } = new();
        public EmbySessionScope Scope { get; }
        public EmbyImageStore Images { get; }
        public CardItem Card { get; }
        public object Container { get; } = new();
        public EmbyItem Item { get; }
        public List<string> Changes { get; } = [];

        public CardImageFixture(bool artwork = true)
        {
            Directory.CreateDirectory(_root); // Current test output only; retain files for inspection.
            PosterCache.Clear();
            PosterLoader.Reset();
            var device = new DeviceIdentity("image-fixture", "image-fixture", "image-fixture", "1");
            var settings = new AppSettings();
            var protector = new NoSecrets();
            _session = new EmbySession(settings, new SettingsStore(new AppPaths(_root), protector),
                new CredentialVault(protector), device, Handler);

            // Register a synthetic available source directly. No SignIn/Restore/Adopt, saved config,
            // server detection, account lifecycle or credential exchange is executed by this fixture.
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("http://card-image.invalid/emby/"), "",
                "image-fixture", "image-fixture", "image-fixture", device);
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
                new ServerProfile { Id = "image-server" }, new AccountProfile { Id = "image-account" });
            Scope = new EmbySessionScope(_session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
                .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            sources.Add(Scope.Key, Scope);

            Images = new EmbyImageStore(_session, Path.Combine(_root, "images"));
            _downloads = (SharedWork<byte[]?>)typeof(EmbyImageStore)
                .GetField("_downloads", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Images)!;
            Item = Scope.Bind(new EmbyItem
            {
                Id = "image-item",
                Name = "image-fixture",
                Type = EmbyItemType.Movie,
                ImageTags = artwork ? new()
                {
                    [EmbyImageStore.Primary] = "image-tag"
                } : new()
            });
            Card = new CardItem(Item, Images, 170);
            Card.PropertyChanged += (_, args) => Changes.Add(args.PropertyName ?? "");
        }

        public Task Begin()
        {
            Card.ClaimCard(Container);
            return Ensure();
        }

        public Task Ensure()
        {
            var load = Card.EnsurePosterAsync();
            _loads.Add(load);
            return load;
        }

        public CancellationToken? LoadingToken => (LoadingField.GetValue(Card) as CancellationTokenSource)?.Token;

        public string Key(int width) => PosterCache.Key(Scope.Connection, Item.Id, EmbyImageStore.Primary, "image-tag", width);

        public string CacheFile(int width) => Path.Combine(_root, "images",
            EmbyImageStore.CacheKey(Scope.Connection, Item.Id, EmbyImageStore.Primary, "image-tag", width));

        public async Task DeliverAsync(CardImageRequest request, string image)
        {
            request.Complete(image);
            // Join the real shared store task so late download success/disk-cache completion is observed,
            // even when Resize already cancelled the original CardItem waiter.
            await Images.GetAsync(Scope, Item.Id, EmbyImageStore.Primary, "image-tag", request.Width, CancellationToken.None);
        }

        public void RevokeSource() => Scope.IdentityState.Lifetime.Cancel();

        public async ValueTask DisposeAsync()
        {
            Card.AbandonPoster();
            PosterLoader.Hold = false;
            foreach (var decode in PosterLoader.Calls) decode.Complete();
            Handler.ReleaseAll();
            await Task.WhenAll(_loads).WaitAsync(TimeSpan.FromSeconds(3));
            await UntilAsync(() => _downloads.Running == 0, "shared synthetic image downloads did not finish");
            _session.Dispose();
            PosterCache.Clear();
            PosterLoader.Reset();
            // Keep this test-output-only directory; no recursive deletion of computed paths.
        }

        public static async Task UntilAsync(Func<bool> condition, string message)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new AssertionException(message);
                await Task.Delay(5);
            }
        }

        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string plainText) => throw new InvalidOperationException("Image fixture must not protect credentials");
            public string Unprotect(string cipherText) => throw new InvalidOperationException("Image fixture must not read credentials");
        }
    }

    internal sealed class CardImageHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<CardImageRequest> _requests = [];
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count { get { lock (_gate) return _requests.Count; } }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (request.Method != HttpMethod.Get || uri.Host != "card-image.invalid"
                || !uri.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                throw new InvalidOperationException("Only in-memory synthetic image GETs are allowed");
            var widthText = uri.Query.TrimStart('?').Split('&')
                .Select(pair => pair.Split('=', 2)).Single(pair => pair[0].Equals("maxWidth", StringComparison.OrdinalIgnoreCase))[1];
            var pending = new CardImageRequest(int.Parse(widthText, System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            TaskCompletionSource changed;
            lock (_gate)
            {
                _requests.Add(pending);
                changed = _changed;
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            changed.TrySetResult();
            // Ignore the handler token deliberately: tests release late successes. CardItem's waiter
            // cancellation and the real Core lifetime checks still have to prevent stale publication.
            return pending.Response;
        }

        public async Task<CardImageRequest> AtAsync(int index)
        {
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    if (_requests.Count > index) return _requests[index];
                    changed = _changed.Task;
                }
                try { await changed.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { throw new AssertionException($"Expected automatic image request #{index + 1}, got {Count}"); }
            }
        }

        public void ReleaseAll()
        {
            CardImageRequest[] requests;
            lock (_gate) requests = _requests.ToArray();
            foreach (var request in requests) request.Complete("fixture-cleanup");
        }
    }

    internal sealed class CardImageRequest(int width, CancellationToken token)
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Width { get; } = width;
        public CancellationToken Token { get; } = token;
        public Task<HttpResponseMessage> Response => _response.Task;

        public void Complete(string image) => Reply(HttpStatusCode.OK, Encoding.UTF8.GetBytes(image));
        public void Missing() => Reply(HttpStatusCode.NotFound, Encoding.UTF8.GetBytes("{}"));

        private void Reply(HttpStatusCode status, byte[] bytes)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            if (!_response.TrySetResult(response)) response.Dispose();
        }
    }

    // CardItem's ConfigureAwait(true) runs on one controlled, windowless thread, like its UI owner.
    internal sealed class CardImageContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Call, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_queue.IsAddingCompleted) return;
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) { }
        }

        public static void Run(Func<Task> run)
        {
            var previous = Current;
            using var context = new CardImageContext();
            SetSynchronizationContext(context);
            try
            {
                var task = run().WaitAsync(TimeSpan.FromSeconds(12));
                while (!task.IsCompleted)
                    if (context._queue.TryTake(out var work, 50)) work.Call(work.State);
                while (context._queue.TryTake(out var work)) work.Call(work.State);
                // The queue has already driven the task to completion: inspect its outcome without
                // synchronously waiting on an async task (the same production analyzer runs here).
                if (task.IsCanceled) throw new TaskCanceledException(task);
                if (task.Exception is { } failure)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException ?? failure).Throw();
            }
            finally { SetSynchronizationContext(previous); }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
#endif
