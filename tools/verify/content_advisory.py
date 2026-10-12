#!/usr/bin/env python3
"""Static checks for the startup content warning and seven-second auto-continuation."""

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
        if len(strings) != 2:
            errors.append(f"{code}: expected warning heading and body; found {len(strings)} strings")
        elif any(not value.strip() for value in strings):
            errors.append(f"{code}: an advisory string is empty")
        elif len(strings[1]) < 180:
            errors.append(f"{code}: warning body should be a substantial content advisory")

    checks = [
        ("startup language selector file is removed", not (RUNTIME / "Menus/Screens/LanguageSelectionScreen.cs").exists()),
        ("normal startup can route to the warning before the main menu", "MenuScreenId.ContentAdvisory" in menu_root and "TakeShowAdvisoryRequest" in menu_root and "MenuScreenId.LanguageSelection" not in menu_root),
        ("advisory screen is registered in the enum", "ContentAdvisory = 9" in registry),
        ("advisory screen is constructed by the factory", "MenuScreen.Create<ContentAdvisoryScreen>" in registry),
        ("advisory automatically continues after seven seconds", "WarningDisplaySeconds = 7f" in advisory and "WaitForSecondsRealtime(WarningDisplaySeconds)" in advisory and "Host.GoTo(MenuScreenId.MainMenu)" in advisory),
        ("advisory has only a short entrance fade, not a countdown", "EntranceSeconds = 0.45f" in advisory and "FadeIn()" in advisory),
        ("advisory entrance fade starts only when shown", "protected override void OnShown(bool instant)" in advisory and "StartCoroutine(FadeIn())" in advisory),
        ("warning has no invisible tap surface or manual input handler", "Invisible Continue Hit Area" not in advisory and "UnityEngine.InputSystem" not in advisory and "public override bool OnBack()" not in advisory),
        ("play-mode test asserts direct-to-main-menu startup", "normal launch must skip the removed language selector" in tests and "MenuScreenId.MainMenu" in tests),
        ("play-mode test verifies the warning waits for input", "Content_advisory_auto_continues_after_seven_seconds_without_input" in tests),
        ("RTL preview is handled line by line", "RtlText.Visualize(lines[i])" in advisory),
        ("warning is pure black and contains no progress bar", "Color.black" in advisory and "Progress Fill" not in advisory and "Reading Progress" not in advisory),
    ]
    for label, passed in checks:
        if not passed:
            errors.append(label)

    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1

    print("content advisory: PASSED — 10 localized warning copies, seven-second auto-advancing warning screen, pure-black layout, RTL preview, and PlayMode coverage")
    return 0


if __name__ == "__main__":
    sys.exit(main())
