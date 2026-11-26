"""Offline regressions for the Fluent XAML auditor; no app build, no network."""

from __future__ import annotations

import importlib.util
import io
import json
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

SPEC = importlib.util.spec_from_file_location(
    "audit_fluent", Path(__file__).with_name("audit_fluent.py")
)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)

BAD_XAML = """<UserControl
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <Grid Background="#1F1F1F">
        <!-- <TextBlock FontSize="13" Foreground="White" /> -->
        <TextBlock Text="Title" FontSize="13" Foreground="White" />
        <TextBlock Text="Tiny" FontSize="10" />
        <Border CornerRadius="6" Background="Black">
            <TextBlock Text="Bold" FontWeight="Bold" FontStyle="Italic" />
        </Border>
        <Color x:Key="AllowedToken">#FF112233</Color>
    </Grid>
</UserControl>
"""

GOOD_XAML = """<UserControl
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <Grid Background="{ThemeResource LayerFillColorDefaultBrush}">
        <TextBlock
            Text="Title"
            Style="{StaticResource TitleTextBlockStyle}"
            Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
        <Border
            CornerRadius="{ThemeResource ControlCornerRadius}"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1">
            <TextBlock Text="Strong" FontWeight="SemiBold" />
        </Border>
    </Grid>
</UserControl>
"""

ALL_RULES = {"F001", "F002", "F003", "F004", "F005"}


class AuditFluentTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        self.bad = self.root / "Bad.xaml"
        self.good = self.root / "Good.xaml"
        self.bad.write_text(BAD_XAML, encoding="utf-8")
        self.good.write_text(GOOD_XAML, encoding="utf-8")

    def tearDown(self) -> None:
        self._temp.cleanup()

    def test_bad_fixture_hits_each_rule(self) -> None:
        findings = audit.scan_file(self.bad, ALL_RULES)
        counts: dict[str, int] = {}
        for item in findings:
            counts[item["rule"]] = counts.get(item["rule"], 0) + 1
        self.assertEqual(
            {"F001": 3, "F002": 1, "F003": 1, "F004": 1, "F005": 1}, counts
        )

    def test_comments_and_token_definitions_are_ignored(self) -> None:
        findings = audit.scan_file(self.bad, ALL_RULES)
        lines = {item["line"] for item in findings}
        self.assertNotIn(4, lines)  # commented-out TextBlock
        self.assertNotIn(10, lines)  # <Color x:Key="..."> token definition

    def test_clean_fixture_has_no_findings(self) -> None:
        self.assertEqual([], audit.scan_file(self.good, ALL_RULES))

    def test_json_summary_and_exit_code(self) -> None:
        output = io.StringIO()
        with redirect_stdout(output), redirect_stderr(io.StringIO()):
            code = audit.main([str(self.bad), "--format", "json", "--quiet"])
        self.assertEqual(1, code)
        payload = json.loads(output.getvalue())
        self.assertEqual(7, payload["summary"]["findings"])
        self.assertEqual(1, payload["summary"]["files"])
        self.assertEqual("F001", payload["findings"][0]["rule"])

    def test_rule_filter(self) -> None:
        output = io.StringIO()
        with redirect_stdout(output), redirect_stderr(io.StringIO()):
            code = audit.main(
                [str(self.bad), "--rules", "F004", "--quiet"]
            )
        self.assertEqual(1, code)
        self.assertIn("F004=1", output.getvalue())

    def test_clean_directory_exits_zero(self) -> None:
        output = io.StringIO()
        with redirect_stdout(output), redirect_stderr(io.StringIO()):
            code = audit.main([str(self.good), "--quiet"])
        self.assertEqual(0, code)
        self.assertIn("clean", output.getvalue())

    def test_unknown_rule_exits_two(self) -> None:
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code = audit.main([str(self.bad), "--rules", "F999"])
        self.assertEqual(2, code)

    def test_missing_path_exits_two(self) -> None:
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code = audit.main([str(self.root / "missing")])
        self.assertEqual(2, code)


if __name__ == "__main__":
    unittest.main(verbosity=2)
