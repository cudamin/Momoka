from pathlib import Path
import hashlib
import json

import argparse
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
here = args.output.resolve()
here.mkdir(parents=True, exist_ok=True)
evidence = []

def extract(path, start, end):
    file = root / path
    text = file.read_text(encoding='utf-8')
    if text.count(start) != 1 or text.count(end) != 1:
        raise SystemExit(f'Unexpected source markers in {path}')
    block = text[text.index(start):text.index(end)].rstrip()
    evidence.append(dict(path=path, sourceSha256=hashlib.sha256(file.read_bytes()).hexdigest(),
                         blockSha256=hashlib.sha256(block.encode()).hexdigest(),
                         startLine=text[:text.index(start)].count('\n') + 1,
                         endLine=text[:text.index(end)].count('\n')))
    return block

panel = 'src/Momoka.Shell/Views/DanmakuPanel.xaml.cs'
methods = extract(panel, '    private async void OnSearch(', '    private void SeedSources(')
methods += '\n\n' + extract(panel, '    private void OnSourceChanged(', '    private void OnShowToggled(')
methods += '\n\n' + extract(panel, '    internal void Close()', '    private bool Current()')
methods += '\n\n' + extract(panel, '    private void OnPlayerChanged(', '    private void Seed()')
methods += '\n\n' + extract(panel, '    private void SeedSources(', '    private void OnSourceChanged(')
methods = methods.replace('PlayerViewModel', 'PanelEndpoint')
(here / 'Panel.g.cs').write_text('#nullable enable\nusing System.ComponentModel;\nusing Momoka.Danmaku;\nnamespace DanmakuReview;\ninternal sealed partial class Panel\n{\n' + methods + '\n}\n', encoding='utf-8', newline='\n')

host = 'src/Momoka.Shell/ViewModels/PlayerViewModel.Danmaku.cs'
methods = extract(host, '    private string DanmakuConnection() =>', '    private void RefreshDanmakuProperties()')
methods += '\n\n' + extract(host, '    private void DanmakuSettingsChanged()', '    private async Task ScheduleDanmakuPushAsync()')
methods += '\n\n' + extract('src/Momoka.Shell/ViewModels/PlayerViewModel.DanmakuSend.cs', '    private void ResetDanmakuInput(', '    private void RefreshDanmakuInput()')
(here / 'Host.g.cs').write_text('#nullable enable\nusing Momoka.Danmaku;\nnamespace DanmakuReview;\ninternal sealed partial class Host\n{\n' + methods + '\n}\n', encoding='utf-8', newline='\n')
settings = 'src/Momoka.Shell/ViewModels/SettingsViewModel.Danmaku.cs'
methods = extract(settings, '    private IReadOnlyList<SettingSection> DanmakuSections()', '    private SettingFontRow DanmakuFont(')
(here / 'Settings.g.cs').write_text('#nullable enable\nusing Momoka.Configuration;\nusing Momoka.Danmaku;\nnamespace DanmakuReview;\ninternal sealed partial class SettingsRows\n{\n' + methods + '\n}\n', encoding='utf-8', newline='\n')
(here / 'source-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print("Extracted current danmaku panel, host and settings boundaries.")
