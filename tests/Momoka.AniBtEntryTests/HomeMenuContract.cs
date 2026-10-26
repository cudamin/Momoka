using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

// Only the realized-card/owner value boundary. MenuRead itself is extracted from the real page.
internal sealed partial class HomeMenuContract
{
    private EmbySession? _session;
    private IShellActions? _actions;
    private List<PosterCard> _cards = [];
    internal HomeMenuContract() { }
    internal HomeMenuContract(EmbySession session, IShellActions actions, CardItem card)
    {
        _session = session;
        _actions = actions;
        _cards = [new PosterCard { Card = card }];
    }
    private List<PosterCard> RealisedCards() => _cards;
}
