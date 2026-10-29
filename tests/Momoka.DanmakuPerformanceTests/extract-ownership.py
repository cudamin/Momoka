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
start = '    internal async Task LoadDanmakuAsync(DandanPlayEpisode episode)'
end = '    private async Task PushDanmakuAsync()'
if text.count(start) != 1 or text.count(end) != 1:
    raise SystemExit('Production danmaku ownership method boundaries changed; inspect before updating fixture.')
method = text[text.index(start):text.index(end)].rstrip()
if not method.endswith('}'):
    raise SystemExit('Incomplete production ownership method')
output = args.output.resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('// Production source SHA256: ' + hashlib.sha256(source.read_bytes()).hexdigest()
                  + '\nusing Momoka.Danmaku;\nnamespace Momoka.Tests;\n'
                  + 'internal sealed partial class PlayerViewModel\n{\n' + method + '\n}\n',
                  encoding='utf-8', newline='\n')
