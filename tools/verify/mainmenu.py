#!/usr/bin/env python3
"""Verify the deliberate clean-slate state of Aether's former main-menu scene.

The old runtime menu is intentionally disconnected while the new direction is being decided.
This gate proves the scene cannot instantiate the old menu, tap-anywhere title overlay, settings
screen, or menu UI event system. It also keeps the studio-intro -> boot -> main-scene route intact.

Run: python3 tools/verify/mainmenu.py
"""

from __future__ import annotations

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MAIN_SCENE = "Assets/Aether/Scenes/MainMenu.unity"
TITLE_START_SCREEN = "Assets/Aether/Code/Aether.Gameplay/Runtime/Menus/TitleStartScreen.cs"
MENU_ROOT = "Assets/Aether/Code/Aether.Gameplay/Runtime/Menus/MenuRoot.cs"
BUILD_SETTINGS = "ProjectSettings/EditorBuildSettings.asset"
INTRO_SCENE = "Assets/Aether/Scenes/StudioIntro.unity"
BOOT_SCENE = "Assets/Aether/Scenes/Boot.unity"


def read(path: str) -> str:
    full = os.path.join(ROOT, path)
    if not os.path.isfile(full):
        raise SystemExit(f"FAIL: required project file is missing: {path}")
    with open(full, encoding="utf-8") as stream:
        return stream.read()


def check(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"FAIL: {message}")


def main() -> int:
    scene = read(MAIN_SCENE)
    settings = read(BUILD_SETTINGS)

    # The reset scene must be a real Unity scene, but it must contain only the camera root.
    check("m_Name: Main Camera" in scene, "MainMenu must retain its camera.")
    check("SceneRoots:" in scene, "MainMenu has no SceneRoots block.")
    roots_block = scene.split("SceneRoots:", 1)[1]
    roots = re.findall(r"- \{fileID: (\d+)\}", roots_block)
    check(roots == ["100001"],
          f"MainMenu must have only the camera root while awaiting new direction; found {roots}.")
    check(not re.search(r"m_Name: Menu(?:\s|$)", scene),
          "The old Menu GameObject is still present.")
    check("6c14e975393997fe2e541979b04832cc" not in scene,
          "The old MenuRoot component is still attached to the scene.")
    check("Title Start Screen" not in scene and "Touch To Start" not in scene,
          "The tap-anywhere title overlay is still authored in the scene.")
    check(not os.path.exists(os.path.join(ROOT, TITLE_START_SCREEN)),
          "The obsolete tap-anywhere title-screen implementation still exists.")
    check("TitleStartScreen" not in read(MENU_ROOT),
          "The old menu root still references the removed tap-anywhere screen.")
    check("EventSystem" not in scene and "Button:" not in scene,
          "The reset scene still contains an interactive UI component.")

    entries = re.findall(
        r"- enabled: (\d)\n\s+path: (\S+)\n\s+guid: ([0-9a-f]{32})",
        settings,
    )
    enabled = [path for flag, path, _guid in entries if flag == "1"]
    check(len(enabled) >= 3, "The build order is missing required scenes.")
    check(enabled[0] == INTRO_SCENE, "The Varellon studio intro is no longer first.")
    check(len(enabled) > 1 and enabled[1] == BOOT_SCENE, "Boot is no longer second.")
    check(MAIN_SCENE in enabled, "MainMenu is no longer in the build order.")

    print("PASS: MainMenu is a clean camera-only scene.")
    print("PASS: No old menu root, tap-anywhere overlay, settings UI, or menu input system can start from this scene.")
    print("PASS: StudioIntro -> Boot -> MainMenu build order is preserved.")
    print("NOTE: This is an intentional temporary clean slate; no APK build is requested by this change.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
