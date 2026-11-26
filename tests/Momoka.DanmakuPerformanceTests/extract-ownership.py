"""Compile the actual host load method, with only presentation/control boundaries substituted."""
import argparse
import hashlib
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source = args.root.resolve() / 'src/Momoka.Shell/ViewModels/PlayerViewModel.Danmaku.cs'
text = source.read_text(encoding='utf-8')
def between(text, start, end):
    if text.count(start) != 1 or text.count(end) != 1:
        raise SystemExit(f'Production danmaku ownership boundaries changed; inspect marker: {start}')
    first = text.index(start)
    last = text.index(end, first + len(start))
    return text[first:last].rstrip()

methods = [
    between(text, '    private int CancelDanmakuSearch()', '    internal void RequestDanmaku()'),
    between(text, '    private string DefaultDanmakuStatus', '    private void RefreshDanmakuProperties()'),
    between(text, '    private void DanmakuSettingsChanged()', '    private async Task ScheduleDanmakuPushAsync()'),
    between(text, '    private DanmakuSource DefaultSearchSource()', '    private async Task PushDanmakuAsync()'),
]

interaction_source = source.with_name('PlayerViewModel.Interaction.cs')
interaction = interaction_source.read_text(encoding='utf-8')
methods += [
    between(interaction, '    internal readonly record struct InteractionContext(', '    internal bool IsCurrentInteraction('),
    between(interaction, '    internal bool IsCurrentPlayback(', '    internal readonly record struct FreezeContext('),
]
output = args.output.resolve()
output.parent.mkdir(parents=True, exist_ok=True)
hashes = '\n'.join('// Production source SHA256: ' + hashlib.sha256(path.read_bytes()).hexdigest()
                   for path in (source, interaction_source))
output.write_text(hashes + '\n#nullable enable\nusing Momoka.Danmaku;\nusing Momoka.Diagnostics;\nusing Momoka.Emby;\nnamespace Momoka.Tests;\n'
                  + 'internal sealed partial class PlayerViewModel\n{\n' + '\n\n'.join(methods) + '\n}\n',
                  encoding='utf-8', newline='\n')
