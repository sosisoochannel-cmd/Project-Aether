#!/usr/bin/env python3
"""Static checks for AETHER's language -> content advisory -> main menu flow."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets/Aether/Code/Aether.Gameplay/Runtime"
ADVISORY = RUNTIME / "Menus/Screens/ContentAdvisoryScreen.cs"
SELECTOR = RUNTIME / "Menus/Screens/LanguageSelectionScreen.cs"
REGISTRY = RUNTIME / "Menus/MenuScreens.cs"
TESTS = ROOT / "Assets/Aether/Code/Aether.Tests.PlayMode/Tests/MenuFlowTests.cs"

EXPECTED = {"en", "es", "fa", "fr", "de", "it", "pt", "pt-BR", "ru", "tr"}


def read(path: Path) -> str:
    if not path.is_file():
        raise SystemExit(f"FAIL: required file missing: {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


def main() -> int:
    advisory = read(ADVISORY)
    selector = read(SELECTOR)
    registry = read(REGISTRY)
    tests = read(TESTS)
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
        if len(strings) != 4:
            errors.append(f"{code}: expected title, body, footer and continue label; found {len(strings)} strings")
        elif any(not value.strip() for value in strings):
            errors.append(f"{code}: an advisory string is empty")
        elif "\\n" not in strings[1]:
            errors.append(f"{code}: advisory body should have an intentional line break for readability")

    checks = [
        ("selector hands off to the advisory", "Host.GoTo(MenuScreenId.ContentAdvisory)" in selector),
        ("selector fades fully to black before handoff", selector.index("_blackout.color = Color.black;") < selector.index("Host.GoTo(MenuScreenId.ContentAdvisory)")),
        ("advisory screen is registered in the enum", "ContentAdvisory = 9" in registry),
        ("advisory screen is constructed by the factory", "MenuScreen.Create<ContentAdvisoryScreen>" in registry),
        ("advisory continue routes to main menu", "Host.GoTo(MenuScreenId.MainMenu)" in advisory),
        ("back cannot skip the advisory", "public override bool OnBack()" in advisory and "return true;" in advisory.split("public override bool OnBack()", 1)[1]),
        ("play-mode test asserts language selector as startup", "BuildMenu(true)" in tests and "MenuScreenId.LanguageSelection" in tests),
        ("play-mode test checks advisory continue route", "Content_advisory_continue_opens_the_main_menu" in tests),
        ("RTL preview is handled line by line", "RtlText.Visualize(lines[i])" in advisory),
    ]
    for label, passed in checks:
        if not passed:
            errors.append(label)

    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1

    print("content advisory: PASSED — 10 localized copies, language → advisory → main-menu flow, RTL preview, and PlayMode coverage")
    return 0


if __name__ == "__main__":
    sys.exit(main())
