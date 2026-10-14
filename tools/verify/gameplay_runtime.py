#!/usr/bin/env python3
"""Static regression checks for Aether's camera, pause controls, and live RTL text.

These narrow contracts guard previously observed failure modes. They are not a Unity compile,
scene render, physics simulation, or substitute for hands-on device testing.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]

def source(path: str) -> str:
    file = ROOT / path
    if not file.is_file():
        raise SystemExit(f"FAIL: required source file is missing: {path}")
    return file.read_text(encoding="utf-8")

def contains(path: str, text: str) -> None:
    if text not in source(path):
        raise SystemExit(f"FAIL: {path} is missing required contract: {text}")

def excludes(path: str, text: str) -> None:
    if text in source(path):
        raise SystemExit(f"FAIL: {path} contains forbidden regression: {text}")

def main() -> int:
    bootstrap = "Assets/Aether/Code/Aether.Gameplay/Runtime/Levels/LevelBootstrap.cs"
    camera = "Assets/Aether/Code/Aether.Gameplay/Runtime/Cameras/CameraFollow2D.cs"
    controls = "Assets/Aether/Code/Aether.Gameplay/Runtime/Controls/TouchControlsView.cs"
    pause = "Assets/Aether/Code/Aether.Gameplay/Runtime/Interface/GameplayPause.cs"
    confirm = "Assets/Aether/Code/Aether.Gameplay/Runtime/Menus/Panels/MenuConfirmPanel.cs"
    menu = "Assets/Aether/Code/Aether.Gameplay/Runtime/Menus/Screens/MainMenuScreen.cs"

    for contract in (
        "camera.orthographic = true;",
        "camera.clearFlags = CameraClearFlags.SolidColor;",
        "camera.backgroundColor = _cameraBackground;",
        "camera.cullingMask |= gameplayMask;",
        "GameplayLayers.Ground.value",
        "GameplayLayers.Player.value",
        "GameplayLayers.Enemy.value",
        "GameplayLayers.Interactable.value",
    ):
        contains(bootstrap, contract)

    contains(camera, "SetTarget(target, snap: true);")
    contains(controls, "public void SetGameplayVisible(bool visible)")
    contains(controls, "if (!_gameplayVisible) return;")
    contains(controls, "bool show = _visible && _gameplayVisible;")
    contains(controls, "if (_source != null) _source.Enabled = visible;")
    for renderer in ("_leftRenderer", "_rightRenderer", "_jumpRenderer",
                     "_attackRenderer", "_dodgeRenderer"):
        contains(controls, f"if ({renderer} != null) {renderer}.enabled = show;")

    contains(pause, "_touchControls.SetGameplayVisible(false);")
    contains(pause, "_touchControls.SetGameplayVisible(true);")
    excludes(pause, "_touchControls.enabled = false")
    excludes(pause, "_touchControls.enabled = true")
    contains(pause, "0.76f")

    contains(confirm, "_title.text = MenuUi.PrepareText(MenuUi.Track(title, MenuTheme.Metrics.ScreenTitleTracking));")
    contains(confirm, "_body.text = MenuUi.PrepareText(body);")
    contains(confirm, "0.80f")
    if source(menu).count("_version.text = MenuUi.PrepareText(MenuUi.Track(") != 2:
        raise SystemExit("FAIL: both main-menu version assignments must apply RTL preparation")

    print("PASS: gameplay camera, pause touch visibility/input, and live RTL text contracts")
    print("NOTE: Unity compilation, rendering, physics and real-device touch still require runtime validation.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
