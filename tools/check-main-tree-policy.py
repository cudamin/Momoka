"""Offline checks for Momoka's main-tree, deferred-merge and delivery documentation.

Run: py tools/check-main-tree-policy.py --self-test
This only reads text and checks in-memory negative fixtures. It never merges, builds,
launches the application, publishes, or changes branches or settings.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

POLICY = "CLAUDE.md"
DOC = "docs/开发与验证.md"
SKILL = ".claude/skills/momoka-verification/SKILL.md"
REQUIRED = {
    POLICY: (
        "只维护 `master` 主树，其他分支都是临时分支",
        "临时分支完成后主动跟进合入主树",
        "用户明确要求“先不合并”时保持待合并",
        "未经恢复指示不合并或交付",
        "合并后的代码必须重新验证，通过后才能交付",
        "以主树最终整合代码为准",
        "不能直接沿用临时分支的通过报告",
        "验证全部通过后，再发布到主树实际交付目录并核对桌面快捷方式",
        "分支收尾即删，本地只留 `master`",
        "立即删除该分支及其工作树",
        "判定以内容为准，不只看分支指针",
        "没有未保留的改动或提交",
        "不是其他聊天正在开发的树",
    ),
    DOC: (
        "../CLAUDE.md#git-与协作",
        "../CLAUDE.md#四道闸门与日常交付",
        "py tools/check-main-tree-policy.py --self-test",
        "只检查文档，不合并、不构建、不运行应用或发布",
    ),
    SKILL: (
        "../../../CLAUDE.md#git-与协作",
        "../../../CLAUDE.md#四道闸门与日常交付",
        "final integrated master tree rather than reusing branch-only reports",
        "explicit request to defer merging keeps that pending item open",
    ),
}


def validate(texts: dict[str, str]) -> None:
    for relative, phrases in REQUIRED.items():
        text = texts[relative]
        if "\r" in text or not text.endswith("\n"):
            raise ValueError(f"non-LF or missing final newline: {relative}")
        for phrase in phrases:
            if phrase not in text:
                raise ValueError(f"missing rule or synchronization: {relative}: {phrase}")
    for anchor in ("Git 与协作", "四道闸门与日常交付"):
        if f"## {anchor}\n" not in texts[POLICY]:
            raise ValueError(f"broken policy anchor: {anchor}")


def check_links(root: Path, texts: dict[str, str]) -> None:
    for relative in (DOC, SKILL):
        # Validate this change's required links; unrelated existing policy anchors are not this check's scope.
        targets = [target for target in REQUIRED[relative] if "CLAUDE.md#" in target]
        actual = re.findall(r"\]\(([^)]+CLAUDE\.md(?:#[^)]+)?)\)", texts[relative])
        for target in targets:
            if target not in actual:
                raise ValueError(f"missing Markdown policy link: {relative}: {target}")
            path, _, anchor = target.partition("#")
            if (root / relative).parent.joinpath(path).resolve() != (root / POLICY).resolve():
                raise ValueError(f"wrong policy link: {relative}: {target}")
            if anchor not in {"git-与协作", "四道闸门与日常交付"}:
                raise ValueError(f"unknown policy anchor: {relative}: {target}")


def self_test(texts: dict[str, str]) -> int:
    rejected = 0
    for relative, phrases in REQUIRED.items():
        for phrase in phrases:
            broken = dict(texts)
            broken[relative] = broken[relative].replace(phrase, "")
            try:
                validate(broken)
            except ValueError:
                rejected += 1
            else:
                raise AssertionError(f"missing text incorrectly passed: {relative}: {phrase}")
    return rejected


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    texts = {relative: (root / relative).read_bytes().decode("utf-8-sig") for relative in REQUIRED}
    validate(texts)
    check_links(root, texts)
    count = self_test(texts) if args.self_test else 0
    print(f"Main-tree policy check passed: {len(texts)} documents, {count} rejected negative fixtures; no merge/build/app/publish.")


if __name__ == "__main__":
    main()
