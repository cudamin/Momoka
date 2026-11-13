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
LEDGER = "docs/待交付批次.md"
REQUIRED = {
    POLICY: (
        "只维护 `master` 主树，其他分支都是临时分支",
        "分支做完只合并，交付只在用户明确要求时执行",
        "分支做完只合并，我要求交付再交付",
        "临时分支完成后主动合入主树，不自行交付",
        "用户明确要求“先不合并”时保持待合并",
        "未经恢复指示不合并或交付",
        "待交付批次台账",
        "合并后的代码必须重新验证，通过后才能交付",
        "以主树最终整合代码为准",
        "分支上的通过报告只覆盖分支代码",
        "验证全部通过后，再发布到主树实际交付目录并核对桌面快捷方式",
        "分支收尾即删，本地只留 `master`",
        "立即删除该分支及其工作树",
        "判定以内容为准，不只看分支指针",
        "没有未保留的改动或提交",
        "不是其他聊天正在开发的树",
        "用探针等待释放后自动继续，不反复让用户关闭",
        "不得擅自结束其他聊天或用户的进程、绕过锁",
        "由正式操作重新取得锁，竞争失败时回到等待",
        "等待超时或证据表明必须由用户处理时",
    ),
    DOC: (
        "../CLAUDE.md#git-与协作",
        "../CLAUDE.md#四道闸门与日常交付",
        "](待交付批次.md)",
        "合并与日常收尾不跑这一闸",
        "py tools/check-main-tree-policy.py --self-test",
        "只检查文档，不合并、不构建、不运行应用或发布",
        "../CLAUDE.md#资源占用与等待",
        "使用只读轻量探针、短间隔等待释放后自动继续",
    ),
    SKILL: (
        "../../../CLAUDE.md#git-与协作",
        "../../../CLAUDE.md#四道闸门与日常交付",
        "../../../docs/待交付批次.md",
        "Merging closes a batch; publishing happens only on an explicit delivery request",
        "run it against the final integrated master tree",
        "Do not reuse branch-only reports",
        "explicit request to defer merging keeps that pending item open",
        "../../../CLAUDE.md#资源占用与等待",
        "short interruptible waits, then continue automatically after release",
        "the real operation must recheck its target and acquire its own lock",
    ),
    LEDGER: (
        "../CLAUDE.md#四道闸门与日常交付",
        "../CLAUDE.md#git-与协作",
        "只在合并完成时追加、只在交付完成时销账",
        "统一验证",
        "## 交付时怎么做",
        "## 已交付",
    ),
}
FORBIDDEN = {
    POLICY: (
        "文件锁须请用户关闭",
        "在所属交付批次的测试与适用验证全部通过后才交付",
        "批次收尾时更新桌面快捷方式指向",
    ),
    DOC: (
        "批次收尾再对最终代码跑闸门 2、受影响的运行检查/探针和闸门 3",
        "**闸门 3：刷新实际交付目录（批次收尾）。**",
    ),
    SKILL: ("At batch close, run gate 2, the affected runtime checks/probes and gate 3 once",),
    LEDGER: ("分支已发布即算交付",),
}


def validate(texts: dict[str, str]) -> None:
    for relative, phrases in REQUIRED.items():
        text = texts[relative]
        if "\r" in text or not text.endswith("\n"):
            raise ValueError(f"non-LF or missing final newline: {relative}")
        for phrase in phrases:
            if phrase not in text:
                raise ValueError(f"missing rule or synchronization: {relative}: {phrase}")
        for phrase in FORBIDDEN.get(relative, ()):
            if phrase in text:
                raise ValueError(f"superseded occupancy rule: {relative}: {phrase}")
    for heading in ("## Git 与协作", "## 四道闸门与日常交付", "### 资源占用与等待"):
        if f"{heading}\n" not in texts[POLICY]:
            raise ValueError(f"broken policy anchor: {heading}")


def check_links(root: Path, texts: dict[str, str]) -> None:
    for relative in (DOC, SKILL, LEDGER):
        # Validate this change's required links; unrelated existing policy anchors are not this check's scope.
        targets = [target for target in REQUIRED[relative] if "CLAUDE.md#" in target]
        actual = re.findall(r"\]\(([^)]+CLAUDE\.md(?:#[^)]+)?)\)", texts[relative])
        for target in targets:
            if target not in actual:
                raise ValueError(f"missing Markdown policy link: {relative}: {target}")
            path, _, anchor = target.partition("#")
            if (root / relative).parent.joinpath(path).resolve() != (root / POLICY).resolve():
                raise ValueError(f"wrong policy link: {relative}: {target}")
            if anchor not in {"git-与协作", "四道闸门与日常交付", "资源占用与等待"}:
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
    for relative, phrases in FORBIDDEN.items():
        for phrase in phrases:
            broken = dict(texts)
            broken[relative] += phrase + "\n"
            try:
                validate(broken)
            except ValueError:
                rejected += 1
            else:
                raise AssertionError(f"superseded rule incorrectly passed: {relative}: {phrase}")
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
