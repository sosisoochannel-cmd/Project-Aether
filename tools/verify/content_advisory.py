#!/usr/bin/env python3
"""Static checks for the optional content advisory and direct-to-menu startup flow."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets/Aether/Code/Aether.Gameplay/Runtime"
ADVISORY = RUNTIME / "Menus/Screens/ContentAdvisoryScreen.cs"
REGISTRY = RUNTIME / "Menus/MenuScreens.cs"
TESTS = ROOT / "Assets/Aether/Code/Aether.Tests.PlayMode/Tests/MenuFlowTests.cs"

EXPECTED = {"en", "es", "fa", "fr", "de", "it", "pt", "pt-BR", "ru", "tr"}


def read(path: Path) -> str:
    if not path.is_file():
        raise SystemExit(f"FAIL: required file missing: {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


def main() -> int:
    advisory = read(ADVISORY)
    registry = read(REGISTRY)
    tests = read(TESTS)
    menu_root = read(RUNTIME / "Menus/MenuRoot.cs")
    errors: list[str] = []

    block_match = re.search(
        r"Dictionary<string,\s*string\[\]>\s+Copy\s*=\s*new Dictionary<string,\s*string\[\]>\s*\{(.*?)\n\s*\};",
        advisory,
        re.S,
    )
    if not block_match:
        errors.append("could not read the advisory's localized copy catalogue")
        entries: dict[str, list[str]] = {}
    else:
        entries = {}
        for line in block_match.group(1).splitlines():
            match = re.search(r'\{\s*"([^"]+)"\s*,\s*new\[\]\s*\{(.*?)\}\s*\}', line)
            if not match:
                continue
            code, raw = match.groups()
            entries[code] = re.findall(r'"((?:\\.|[^"\\])*)"', raw)

    missing = EXPECTED - set(entries)
    extra = set(entries) - EXPECTED
    if missing:
        errors.append("missing language copy: " + ", ".join(sorted(missing)))
    if extra:
        errors.append("unexpected language copy: " + ", ".join(sorted(extra)))
    for code, strings in entries.items():
        if len(strings) != 3:
            errors.append(f"{code}: expected eyebrow, body and footer; found {len(strings)} strings")
        elif any(not value.strip() for value in strings):
            errors.append(f"{code}: an advisory string is empty")
        elif "\\n" not in strings[1]:
            errors.append(f"{code}: advisory body should have an intentional line break for readability")

    checks = [
        ("startup language selector file is removed", not (RUNTIME / "Menus/Screens/LanguageSelectionScreen.cs").exists()),
        ("normal launch goes directly to main menu", "MenuScreenId.MainMenu;" in menu_root and "MenuScreenId.LanguageSelection" not in menu_root),
        ("advisory screen is registered in the enum", "ContentAdvisory = 9" in registry),
        ("advisory screen is constructed by the factory", "MenuScreen.Create<ContentAdvisoryScreen>" in registry),
        ("advisory automatically advances to main menu", "AutoAdvanceAfterReadingPeriod" in advisory and "Host.GoTo(MenuScreenId.MainMenu)" in advisory),
        ("advisory has a seven-second reading period", "ReadingDurationSeconds = 7f" in advisory),
        ("advisory timer starts only when shown", "protected override void OnShown(bool instant)" in advisory),
        ("back cannot skip the advisory", "public override bool OnBack()" in advisory and "return true;" in advisory.split("public override bool OnBack()", 1)[1]),
        ("play-mode test asserts direct-to-main-menu startup", "normal launch must skip the removed language selector" in tests and "MenuScreenId.MainMenu" in tests),
        ("play-mode test checks timed auto-advance without a button", "Content_advisory_auto_advances_to_the_main_menu_without_a_continue_button" in tests),
        ("RTL preview is handled line by line", "RtlText.Visualize(lines[i])" in advisory),
    ]
    for label, passed in checks:
        if not passed:
            errors.append(label)

    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1

    print("content advisory: PASSED — 10 localized copies, direct-to-menu startup, timed advisory, RTL preview, and PlayMode coverage")
    return 0


if __name__ == "__main__":
    sys.exit(main())
