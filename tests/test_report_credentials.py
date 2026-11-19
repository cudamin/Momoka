"""Failure-path tests for the offline report credential scanner."""
from pathlib import Path
import importlib.util
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("report_credentials", ROOT / "tools/check-report-credentials.py")
scanner = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scanner
spec.loader.exec_module(scanner)


class ReportCredentialTests(unittest.TestCase):
    def fixture(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        return Path(directory.name)

    def scan(self, text: str, name: str = "PROGRESS.md"):
        root = self.fixture()
        (root / name).parent.mkdir(parents=True, exist_ok=True)
        (root / name).write_text(text, encoding="utf-8")
        return scanner.scan_paths(root, [root / name])

    def test_placeholders_and_clean_reports_pass(self):
        text = (
            "请求头：Authorization: MediaBrowser Token=\"<access token>\"\n"
            "X-Plex-Token: [redacted]\n"
            "地址：https://example.invalid/item?api_key=${API_KEY}\n"
            "示例：Authorization: Bearer ${TOKEN}\n"
        )
        self.assertEqual([], self.scan(text))

    def test_url_query_credential_rejected_without_echo(self):
        findings = self.scan("GET https://example.invalid/items?api_key=fixture-secret-value\n")
        self.assertEqual(["query-credential"], [item.kind for item in findings])
        self.assertNotIn("fixture-secret-value", "\n".join(item.render() for item in findings))

    def test_header_and_json_credentials_rejected(self):
        text = (
            "X-Plex-Token: fixture-header-secret\n"
            "{\"password\": \"fixture-json-secret\"}\n"
        )
        findings = self.scan(text)
        self.assertEqual(["header-credential", "json-credential"], [item.kind for item in findings])
        output = "\n".join(item.render() for item in findings)
        self.assertNotIn("fixture-header-secret", output)
        self.assertNotIn("fixture-json-secret", output)

    def test_proxy_userinfo_rejected(self):
        findings = self.scan("proxy = http://user:fixture-proxy-secret@example.invalid:8080\n")
        self.assertEqual(["url-userinfo"], [item.kind for item in findings])

    def test_archive_directory_is_scanned_recursively(self):
        root = self.fixture()
        archive = root / "docs" / "archive"
        archive.mkdir(parents=True)
        (root / "PROGRESS.md").write_text("干净\n", encoding="utf-8")
        (archive / "PROGRESS-2026-09.md").write_text(
            "https://example.invalid/item?token=fixture-archive-secret\n", encoding="utf-8"
        )
        findings = scanner.scan_defaults(root)
        self.assertEqual(1, len(findings))
        self.assertTrue(findings[0].path.endswith("PROGRESS-2026-09.md"))

    def test_redaction_preserves_context_and_placeholders(self):
        text = (
            "https://example.invalid/item?api_key=fixture-secret#frag\n"
            "proxy=http://user:fixture-proxy@example.invalid:8080\n"
            "https://example.invalid/item?token=${TOKEN}\n"
        )
        updated, count = scanner.redact_text(text)
        self.assertEqual(2, count)
        self.assertIn("?api_key=[redacted]#frag", updated)
        self.assertIn("http://user:[redacted]@example.invalid", updated)
        self.assertIn("?token=${TOKEN}", updated)
        self.assertNotIn("fixture-secret", updated)
        self.assertNotIn("fixture-proxy", updated)
        self.assertEqual([], scanner.scan_paths(Path("."), []))

    def test_self_test_cli(self):
        self.assertEqual(0, scanner.self_test())


if __name__ == "__main__":
    unittest.main(verbosity=2)
