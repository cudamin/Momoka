from pathlib import Path
import argparse
parser=argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
source=(root/'src/Momoka.Shell/ViewModels/MoviePilotMediaViewModel.cs').read_text(encoding='utf-8')
start='public sealed partial class MoviePilotMediaEpisodeRow('
end='public sealed partial class MoviePilotMediaViewModel : PageViewModel'
if source.count(start)!=1 or source.count(end)!=1: raise SystemExit('Season row extraction boundaries changed')
text=source[:source.index(end)]
# Exclude the XML documentation belonging to the following media view model.
text=text[:text.rfind('}')+1]
args.output.mkdir(parents=True,exist_ok=True)
(args.output/'MediaRows.g.cs').write_text('#nullable enable\n'+text,encoding='utf-8',newline='\n')

start='    internal void SyncSubscriptionIdentity('
end='    public override async Task ReloadAsync()'
if source.count(start)!=1 or source.count(end)!=1: raise SystemExit('Subscription sync extraction boundaries changed')
method=source[source.index(start):source.index(end)]
(args.output/'SubscriptionSync.g.cs').write_text('#nullable enable\nusing Momoka.MoviePilot;\nnamespace Momoka.Shell.ViewModels;\npublic sealed partial class MoviePilotMediaViewModel\n{\n'+method+'\n}\n',encoding='utf-8',newline='\n')
page=(root/'src/Momoka.Shell/Views/MoviePilotSubscriptionPage.xaml.cs').read_text(encoding='utf-8')
if 'args.PropertyName == nameof(MoviePilotSubscriptionsViewModel.SelectedIdentity)' not in page or 'MediaViewModel.SyncSubscriptionIdentity(selected.Subscription);' not in page:
    raise SystemExit('Subscription identity change is not connected to the real page')
