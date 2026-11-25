"""Check that the static website source is mirrored exactly in website/dist.

Run: py tools/check-website-mirror.py
Test: py tools/check-website-mirror.py --self-test
"""
from __future__ import annotations

import argparse
from pathlib import Path
import tempfile

STATIC_FILES = ("index.html", "main.js", "styles.css")
ASSETS_DIRECTORY = "assets"
DIST_DIRECTORY = "dist"


def _asset_files(directory: Path) -> set[str]:
    if not directory.is_dir():
        return set()
    return {path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_file()}


def check(website: Path) -> list[str]:
    """Return missing, extra, and changed static deployment files."""
    dist = website / DIST_DIRECTORY
    source_assets = website / ASSETS_DIRECTORY
    dist_assets = dist / ASSETS_DIRECTORY

    source_files = {
        relative for relative in STATIC_FILES if (website / relative).is_file()
    } | {f"{ASSETS_DIRECTORY}/{name}" for name in _asset_files(source_assets)}
    dist_files = {
        path.relative_to(dist).as_posix() for path in dist.rglob("*") if path.is_file()
    } if dist.is_dir() else set()
    failures: list[str] = []

    for relative in STATIC_FILES:
        if not (website / relative).is_file():
            failures.append(f"源文件不存在：{relative}")
    for relative in sorted(source_files - dist_files):
        failures.append(f"dist 缺少文件：{relative}")
    for relative in sorted(dist_files - source_files):
        failures.append(f"dist 多余文件：{relative}")

    for relative in sorted(source_files & dist_files):
        source = website / relative
        deployed = dist / relative
        if not deployed.is_file():
            failures.append(f"dist 文件不存在：{relative}")
        elif source.read_bytes() != deployed.read_bytes():
            failures.append(f"文件内容不一致：{relative}")

    return failures


def _make_fixture(root: Path) -> Path:
    website = root / "website"
    dist = website / DIST_DIRECTORY
    for relative in (*STATIC_FILES, f"{ASSETS_DIRECTORY}/icons/sample.bin"):
        source = website / relative
        deployed = dist / relative
        source.parent.mkdir(parents=True, exist_ok=True)
        deployed.parent.mkdir(parents=True, exist_ok=True)
        content = f"fixture:{relative}".encode("utf-8")
        source.write_bytes(content)
        deployed.write_bytes(content)
    return website


def self_test() -> int:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        website = _make_fixture(root)
        if check(website):
            raise AssertionError(f"matching fixture was rejected: {check(website)}")

        source_js = website / "main.js"
        source_js.write_bytes(b"changed source")
        if not any("main.js" in failure for failure in check(website)):
            raise AssertionError("source-file changes were not detected")
        source_js.write_bytes(b"fixture:main.js")

        deployed_css = website / DIST_DIRECTORY / "styles.css"
        deployed_css.write_bytes(b"changed deployment")
        if not any("styles.css" in failure for failure in check(website)):
            raise AssertionError("deployment-file changes were not detected")
        deployed_css.write_bytes(b"fixture:styles.css")

        nested_asset = website / ASSETS_DIRECTORY / "icons/sample.bin"
        nested_asset.write_bytes(b"changed asset")
        if not any("assets/icons/sample.bin" in failure for failure in check(website)):
            raise AssertionError("nested asset changes were not detected")
        nested_asset.write_bytes(b"fixture:assets/icons/sample.bin")

        missing = website / DIST_DIRECTORY / ASSETS_DIRECTORY / "icons/sample.bin"
        missing.unlink()
        if not any("缺少文件" in failure for failure in check(website)):
            raise AssertionError("missing deployment files were not detected")
        missing.write_bytes(b"fixture:assets/icons/sample.bin")

        extra = website / DIST_DIRECTORY / "rogue.js"
        extra.write_bytes(b"extra")
        if not any("多余文件" in failure for failure in check(website)):
            raise AssertionError("extra deployment files were not detected")
        extra.unlink()

        (website / "README.md").write_text("deployment instructions", encoding="utf-8")
        worker = website / "worker" / "index.js"
        worker.parent.mkdir(parents=True, exist_ok=True)
        worker.write_text("export default {}", encoding="utf-8")
        if check(website):
            raise AssertionError("unrelated website files changed mirror detection")

    print("Website mirror self-test passed: matching files accepted; source, deployment, nested asset, missing, and extra files rejected.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()

    website = args.root.resolve() / "website"
    failures = check(website)
    for failure in failures:
        print(failure)
    print(f"Website deployment mirror: {len(STATIC_FILES)} root files and recursive assets; {len(failures)} mismatches.")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
