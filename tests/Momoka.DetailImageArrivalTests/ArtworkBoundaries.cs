using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels
{
    // The complete LoadArtworkAsync/Fresh methods are generated from the current production file.
    // Only UI/decode/cache endpoints and the surrounding page lifetime are inert here.
    internal sealed partial class DetailArtworkFixture : CancelBoundary
    {
        private const string Category = "fixture";
        private const int PlateDecodeWidth = 240;
        private const int HeroDecodeWidth = 1920;
        private const int CornerDecodeWidth = 400;
        private const int FooterDecodeWidth = 760;
        private const int StillDecodeWidth = 260;
        private DetailHeroCache? _heroCache = null;
        private readonly FakeScope _scope = new();
        private readonly FakeImages _images = new();
        private CancellationTokenSource? _art;
        private EmbyItem? _detail;
        private object? _heroTiming;
        private HeroPictureLoader.HeroPixels? _heroPixels;
        private DetailHeroCache.Ticket? _heroCacheTicket;
        private int _heroBlurGeneration;
        private int _heroBlurRadius;
        private bool _heroRendering;
        private int HeroBlurTarget => 0;
        private bool _active = true;
        private bool _actionsReady;
        private FakeSubjectCard? _subjectStateCard = null;
        private CancellationTokenSource? _versionLoad = null;
        private CancellationTokenSource? _blurCancel = null;
        private int _lifetime;
        private FakeShelf? EpisodeShelf => null;
        private bool Attached => _active;
        public int DecodeRequests { get; private set; }
        public int ShowRequests { get; private set; }
        public bool HoldDecode { get; set; }
        public TaskCompletionSource<BitmapImage?> DecodeRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BitmapImage? PlateImage { get; private set; }
        public BitmapImage? HeroImage { get; private set; }
        public BitmapImage? FooterImage { get; private set; }
        public BitmapImage? StillImage { get; private set; }
        public BitmapImage? CornerImage { get; private set; }
        public ArtworkRef? FooterPick { get; private set; }
        public double HeroHeightRatio { get; private set; }
        public string Title => _detail?.Name ?? "";
        internal bool HeroRendering => _heroRendering;
        internal bool ActionsReady => _actionsReady;
        public void Reactivate() => _active = true;
        public Task Start(EmbyItem item)
        {
            _detail = item;
            _art = new();
            return LoadArtworkAsync(item, _art);
        }
        public void DetachWithoutCancellingTransport() => _active = false;
        public void ReplaceOwner() => _art = new();
        private void BusyChanged() { }
        private void AnnounceAffordances() { }
        private void OnSubjectStateChanged(object? sender, PropertyChangedEventArgs args) { }
        private Task<EmbyItem?> LoadShowAsync(EmbyItem item, CancellationTokenSource art)
        {
            ShowRequests++;
            return Task.FromResult<EmbyItem?>(item);
        }
        private Task<BitmapImage?> DecodeFirstAsync(EmbyItem item, IReadOnlyList<string> types, int width, CancellationToken token) => Decode(token);
        private Task<BitmapImage?> DecodeFirstAsync(IReadOnlyList<ArtworkRef> refs, int width, CancellationToken token)
            => refs.Count == 0 ? Task.FromResult<BitmapImage?>(null) : Decode(token);
        private Task<BitmapImage?> Decode(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DecodeRequests++;
            // Deliberately permit a late result after cancellation to exercise publication ownership.
            return HoldDecode ? DecodeRelease.Task : Task.FromResult<BitmapImage?>(new());
        }
        private void ShowStill(BitmapImage picture) => StillImage = picture;
        private void ShowCorner(BitmapImage picture) => CornerImage = picture;
        private Task<BitmapImage?> RenderHeroAsync(HeroPictureLoader.HeroPixels pixels, int radius, CancellationToken token,
            DetailHeroCache.Ticket? ticket, object? timing) => Task.FromResult<BitmapImage?>(new());
    }
    internal class CancelBoundary { public virtual void Cancel() { } }
    internal sealed class FakeShelf { public void Clear() { } }
    internal sealed class FakeSubjectCard
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Change() => PropertyChanged?.Invoke(this, new("fixture"));
    }
    internal sealed class FakeScope { public void ThrowIfNotAvailable() { } }
    internal sealed class FakeImages
    {
        public Task<HeroPictureLoader.HeroPixels?> GetDecodedAsync(FakeScope scope, string itemId, string type, string? tag, int width,
            Func<byte[], Task<HeroPictureLoader.HeroPixels?>> decode, CancellationToken token) => decode([]);
    }
    internal sealed class DetailHeroCache
    {
        public sealed class Ticket;
        public int Generation => 0;
        public Ticket? Begin(FakeScope scope, ArtworkRef picture, int width, int? generation) => null;
        public bool TryGetPixels(Ticket ticket, out HeroPictureLoader.HeroPixels? pixels) { pixels = null; return false; }
        public void RememberPixels(Ticket ticket, HeroPictureLoader.HeroPixels pixels) { }
    }
    internal static class HeroPictureLoader
    {
        public sealed class HeroPixels { public int Width => 1920; public int Height => 1080; }
        public static Task<HeroPixels?> DecodePixelsAsync(byte[] bytes, int width, CancellationToken token) => Task.FromResult<HeroPixels?>(new());
    }
}
