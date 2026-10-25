"""Failure-path tests for the offline support-document guard, never contacts a server."""
from pathlib import Path
import importlib.util
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("support_docs", ROOT / "tools/check-server-support-docs.py")
support = importlib.util.module_from_spec(spec)
spec.loader.exec_module(support)


class SupportDocsTests(unittest.TestCase):
    def fixture(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        import re
        pending = list(support.FILES)
        copied = set()
        while pending:
            relative = pending.pop()
            if relative in copied:
                continue
            path = (ROOT / relative).resolve()
            if not path.is_relative_to(ROOT) or not path.is_file():
                continue
            copied.add(relative)
            target = root / path.relative_to(ROOT)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
            if path.suffix != ".md":
                continue
            for link in re.findall(r"\]\(([^)]+)\)", path.read_text(encoding="utf-8")):
                if re.match(r"[a-z][a-z0-9+.-]*:", link, re.I) or link.startswith("#"):
                    continue
                source = (path.parent / link.split("#", 1)[0]).resolve()
                if source.is_relative_to(ROOT) and source.is_file():
                    pending.append(str(source.relative_to(ROOT)))
        return root

    def mutation(self, path, old, new, expected):
        root = self.fixture()
        target = root / path
        text = target.read_text(encoding="utf-8")
        self.assertIn(old, text)
        target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
        with self.assertRaisesRegex(ValueError, expected):
            support.check(root)

    def test_current_documents(self):
        support.check(ROOT)

    def test_management_not_promoted(self):
        self.mutation(support.REPORT, "管理写入未验证", "管理写入全部通过", "管理写入未验证")

    def test_date_and_pipeline_required(self):
        self.mutation(support.REPORT, "独占管线", "未注明管线", "backend/pipeline")

    def test_real_ui_gap_not_hidden(self):
        self.mutation(support.REPORT, "真实界面截图出现空白", "真实界面已经通过", "UI evidence")

    def test_jellyfin_header_only(self):
        self.mutation("docs/Jellyfin支持.md", "/socket?deviceId=", "/socket?api_key=", "credentials in URL")

    def test_private_address_rejected(self):
        root = self.fixture()
        path = root / support.REPORT
        path.write_text(path.read_text(encoding="utf-8") + "\nhttp://192.168.1.1:8096\n", encoding="utf-8", newline="\n")
        with self.assertRaisesRegex(ValueError, "private server address"):
            support.check(root)

    def test_crlf_rejected(self):
        root = self.fixture()
        path = root / support.REPORT
        path.write_bytes(path.read_bytes().replace(b"\n", b"\r\n"))
        with self.assertRaisesRegex(ValueError, "non-LF"):
            support.check(root)


if __name__ == "__main__":
    unittest.main(verbosity=2)
