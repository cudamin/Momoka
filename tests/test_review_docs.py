"""Content regressions and negative checks; no service calls or real account data."""
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("review_docs", ROOT / "tools/check-review-docs.py")
docs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(docs)


class ReviewDocsTests(unittest.TestCase):
    def test_current_documents(self):
        for finding, passed in docs.check(ROOT).items():
            with self.subTest(finding=finding):
                self.assertTrue(passed, finding)

    def mutate(self, path, old, new, finding):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in docs.FILES:
                source = ROOT / name
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
            # The referenced playback skill is already one of the exact fixture inputs.
            target = root / path
            content = target.read_text(encoding="utf-8")
            self.assertIn(old, content)
            target.write_text(content.replace(old, new), encoding="utf-8", newline="\n")
            self.assertFalse(docs.check(root)[finding])

    def test_external_data_disclosure(self):
        self.mutate("PRIVACY.md", "Translation", "REDACTED", "R18-001")

    def test_shared_folder_deletion(self):
        self.mutate("PRIVACY.md", "shared folder", "folder", "R18-002")

    def test_private_address_rejected_without_echo(self):
        self.mutate("PROGRESS.md", "#", "# https://192.168.2.5:8096/Items/fixture", "R18-003")

    def test_historical_webhook_credential_rejected(self):
        self.mutate("PROGRESS.md", "#", "# https://fixture.invalid/webhook?api_key=fixture-secret", "R18-003")

    def test_realtime_token_in_query(self):
        self.mutate("docs/开发与验证.md", "Handshake", "`api_key` 与 `deviceId` 进查询串", "R18-004")

    def test_retired_credentials(self):
        self.mutate(docs.FILES[8], "未过期缓存继续用", "用户可改用自定义凭据", "R18-005")

    def test_search_navigation_exception(self):
        self.mutate("docs/MoviePilot订阅.md", "四个来源始终显示", "来源隐藏", "R18-006")

    def test_plex_fnnas_scope(self):
        self.mutate("docs/Plex支持.md", "Plex 没有", "与飞牛影视同样待遇；Plex 没有", "R18-007")

    def test_whole_series_playback(self):
        self.mutate(docs.FILES[10], "whole-series", "one-season", "R18-008")

    def test_view_coverage(self):
        self.mutate(docs.FILES[11], "seven Emby", "all nine library", "R18-009")

    def test_reference_link_target(self):
        self.mutate(docs.FILES[12], "../../momoka-playback/SKILL.md", "../momoka-playback/SKILL.md", "R18-010")


if __name__ == "__main__":
    unittest.main()
