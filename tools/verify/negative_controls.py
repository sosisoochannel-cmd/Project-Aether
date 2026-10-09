#!/usr/bin/env python3
"""Negative controls for the verification tools.

A verifier that has never been seen to fail is not evidence. This script copies the
repository to a temporary directory, injects one fault at a time, runs the gate or the
solver against the copy, and asserts that the tool reports the fault. Nothing in the real
working tree is touched, and the copy is deleted afterwards.

Run:  python3 tools/verify/negative_controls.py
Exit: 0 when every injected fault was detected, 1 otherwise.
"""

from __future__ import annotations

import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COPY_DIRS = ("Assets", "ProjectSettings", "Packages", "tools", "docs")
IGNORE = shutil.ignore_patterns(".git", "Library", "Temp", "obj", "__pycache__", "*.bak")
CONTROL_TIMEOUT_SECONDS = 30

CODE = "Assets/Aether/Code"
INTRO_CODE = f"{CODE}/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs"
INTRO_SCENE = "Assets/Aether/Scenes/StudioIntro.unity"
BUILD_SETTINGS = "ProjectSettings/EditorBuildSettings.asset"
PLAYER_SETTINGS = "ProjectSettings/ProjectSettings.asset"
BOOT_SCENE = "Assets/Aether/Scenes/Boot.unity"
BRAND_DIR = "Assets/Aether/Resources/Brand"
MENUS = f"{CODE}/Aether.Gameplay/Runtime/Menus"
CAMERA_FOLLOW = f"{CODE}/Aether.Gameplay/Runtime/Cameras/CameraFollow2D.cs"
CONFIRM_PANEL = f"{MENUS}/Panels/MenuConfirmPanel.cs"
MOTOR = f"{CODE}/Aether.Gameplay/Runtime/Enemies/EnemyMotor2D.cs"
PLAYER_MOTOR = f"{CODE}/Aether.Gameplay/Runtime/Player/PlayerMotor.cs"
MENU_FLOW_TESTS = f"{CODE}/Aether.Tests.PlayMode/Tests/MenuFlowTests.cs"
PLACEHOLDER = f"{CODE}/Aether.Gameplay/Runtime/Support/PlaceholderVisuals.cs"
MENU_BUTTON = f"{MENUS}/Components/MenuButton.cs"
INTERFACE = f"{CODE}/Aether.Gameplay/Runtime/Interface"
INTERFACE_HUD = f"{INTERFACE}/GameplayHud.cs"
INTERFACE_PAUSE = f"{INTERFACE}/GameplayPause.cs"
INTERFACE_SHELL = f"{INTERFACE}/GameplayShell.cs"
THEME = f"{MENUS}/MenuTheme.cs"
CATALOG = f"{CODE}/Aether.Core/Runtime/Settings/SettingsCatalog.cs"
LEVEL = "Assets/Aether/Resources/Levels/region1.greenway.level.txt"
STRIKE = "Assets/Aether/Resources/Content/Attack.Strike.asset"
ENEMY = "Assets/Aether/Resources/Content/Enemies/ForestStalker.asset"


class Control:
    """One injected fault and the outcome it must produce."""

    def __init__(self, name, tool, mutate, mentions, must_fail=True):
        self.name = name
        self.tool = tool
        self.mutate = mutate
        self.mentions = mentions
        # Some controls guard the opposite direction: an over-eager verifier that reports a
        # non-problem is just as broken as one that misses a problem.
        self.must_fail = must_fail


def edit(root: str, rel: str, old: str, new: str):
    """Replace text in a file, returning the undo action."""
    path = os.path.join(root, rel)
    original = open(path, encoding="utf-8").read()
    if old not in original:
        raise AssertionError(f"{rel}: pattern not found: {old[:60]!r}")
    open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
    return [(path, original)]


def append_line(root: str, rel: str, line: str):
    """Append one line to a file, returning the undo action."""
    path = os.path.join(root, rel)
    original = open(path, encoding="utf-8").read()
    separator = "" if not original or original.endswith("\n") else "\n"
    open(path, "w", encoding="utf-8").write(original + separator + line.rstrip("\n") + "\n")
    return [(path, original)]


def retune(root: str, rel: str, name: str, value: str):
    """Set a C# float constant by name, whatever it currently holds.

    The controls pin *behaviour*, not the number the theme is tuned to. A control that hard-codes
    the old value stops injecting anything the first time the value is legitimately retuned - and a
    control that silently stops injecting a fault is worse than no control at all, because it keeps
    reporting that the gate was caught failing something it never saw.
    """
    path = os.path.join(root, rel)
    original = open(path, encoding="utf-8").read()
    pattern = re.compile(rf"(public const float {name}\s*=\s*)[0-9.]+f;")
    if pattern.search(original) is None:
        raise AssertionError(f"{rel}: constant '{name}' not found")
    open(path, "w", encoding="utf-8").write(pattern.sub(rf"\g<1>{value}f;", original, count=1))
    return [(path, original)]


TILE_CHARACTERS = set(".#%=,\"")


def level_rows(root: str, edits):
    """Overwrite characters in several tile-map rows at once. `edits` is (row, column, count, text)."""
    path = os.path.join(root, LEVEL)
    original = open(path, encoding="utf-8").read()
    lines = original.split("\n")
    start = lines.index("[tiles]") + 1
    for index, column, count, text in edits:
        line = lines[start + index]
        if not set(line) <= TILE_CHARACTERS:
            raise AssertionError(f"row {index} is not a tile row: {line[:40]!r}")
        if len(text) != count:
            raise AssertionError(f"replacement for row {index} is {len(text)} wide, expected {count}")
        lines[start + index] = line[:column] + text + line[column + count:]
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    return [(path, original)]


def write_png(path: str, width: int, height: int, sample, alpha: bool = False) -> None:
    """Write an 8-bit PNG whose pixels come from `sample(x, y)`.

    The artwork controls need canvases the repository does not contain: a logo on a dark background,
    a canvas with nothing drawn on it, and one with a real alpha channel. Building them here keeps
    those cases testable without committing throwaway images, and this writer is the smallest thing
    that produces a file the gate's reader accepts.
    """
    channels = 4 if alpha else 3
    colour = 6 if alpha else 2
    raw = bytearray()
    for y in range(height):
        raw.append(0)                                   # filter type: none
        for x in range(width):
            pixel = sample(x, y)
            raw.extend(pixel[:4] if alpha else pixel[:3])

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + kind + payload
                + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF))

    body = (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, colour, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(body)


def plant_artwork(root: str, sample, alpha: bool = False, size: int = 32):
    """Replace the studio artwork in the copy with a canvas `sample` draws."""
    path = os.path.join(root, f"{BRAND_DIR}/VarellonLogo.png")
    original = open(path, "rb").read()
    write_png(path, size, size, sample, alpha)
    return [("WRITE_BYTES", (path, original))]


def unlink_artwork(root: str):
    """Take the studio artwork out of the copy, with its .meta, as a deletion would."""
    png = os.path.join(root, f"{BRAND_DIR}/VarellonLogo.png")
    undo = [("WRITE_BYTES", (png, open(png, "rb").read())),
            ("WRITE_BYTES", (png + ".meta", open(png + ".meta", "rb").read()))]
    os.remove(png)
    os.remove(png + ".meta")
    return undo


def empty_folder(root: str):
    """A folder with a valid .meta is what the emptiness check is about."""
    folder = os.path.join(root, f"{CODE}/Aether.Gameplay/Runtime/Empty")
    os.makedirs(folder)
    template_path = os.path.join(root, f"{CODE}/Aether.Gameplay/Runtime/Levels.meta")
    template = open(template_path, encoding="utf-8").read()
    meta_path = folder + ".meta"
    open(meta_path, "w", encoding="utf-8").write(
        template.replace(next(line for line in template.split("\n") if line.startswith("guid: ")),
                          "guid: 00112233445566778899aabbccddeeff")) 
    return [("DELETE_TREE", folder), ("DELETE", meta_path)]


def controls() -> list[Control]:
    gate = ["python3", "tools/verify/verify.py"]
    strings = ["python3", "tools/verify/localization.py"]
    solver = ["python3", "tools/verify/levelcheck.py"]
    intro = ["python3", "tools/verify/intro.py"]
    menu = ["python3", "tools/verify/mainmenu.py"]

    def bad_brand_texture(root):
        """A brand PNG imported without alpha transparency.

        Written into the copy rather than mutating a real mark's .meta, so this control keeps
        testing the rule even if the studio's artwork or its import settings change.
        """
        folder = os.path.join(root, BRAND_DIR)
        folder_existed = os.path.isdir(folder)
        os.makedirs(folder, exist_ok=True)
        # A new folder needs its own .meta, or the gate fails for that reason instead of the one
        # this control is about, and the evidence line would name the wrong problem.
        if not folder_existed:
            with open(folder + ".meta", "w", encoding="utf-8") as fh:
                fh.write("fileFormatVersion: 2\n"
                         "guid: fedcba9876543210fedcba9876543210\n"
                         "folderAsset: yes\n"
                         "DefaultImporter:\n"
                         "  externalObjects: {}\n"
                         "  userData: \n"
                         "  assetBundleName: \n"
                         "  assetBundleVariant: \n")
        png = os.path.join(folder, "ControlMark.png")
        meta = png + ".meta"
        for path in (png, meta):
            if os.path.exists(path):
                raise AssertionError(f"{path} already exists; the control would overwrite it")
        # The bytes are never decoded by the gate; only the importer settings are read.
        with open(png, "wb") as fh:
            fh.write(b"\x89PNG\r\n\x1a\n" + b"\x00" * 32)
        with open(meta, "w", encoding="utf-8") as fh:
            fh.write("fileFormatVersion: 2\n"
                     "guid: 0123456789abcdef0123456789abcdef\n"
                     "TextureImporter:\n"
                     "  serializedVersion: 13\n"
                     "  mipmaps:\n"
                     "    enableMipMap: 0\n"
                     "  isReadable: 1\n"
                     "  textureType: 8\n"
                     "  spriteMode: 1\n"
                     "  spriteMeshType: 0\n"
                     "  alphaIsTransparency: 0\n"
                     "  filterMode: 1\n"
                     "  wrapU: 1\n"
                     "  wrapV: 1\n"
                     "  textureCompression: 0\n")
        undo = [("DELETE", png), ("DELETE", meta)]
        # Only remove the folder when this control created it. The tree delete the first version
        # used removed the studio's real mark along with the planted one and left the folder's
        # .meta behind, which failed every control that ran after it.
        if not folder_existed:
            undo.append(("DELETE_TREE", folder))
            undo.append(("DELETE", folder + ".meta"))
        return undo

    def demote_the_intro(root):
        """Boot listed before the studio intro: the app would skip its own intro."""
        path = os.path.join(root, BUILD_SETTINGS)
        original = open(path, encoding="utf-8").read()
        intro = ("  - enabled: 1\n"
                 "    path: Assets/Aether/Scenes/StudioIntro.unity\n"
                 "    guid: 8e727f3605223dbc5e7912d5b4306283\n")
        boot = ("  - enabled: 1\n"
                "    path: Assets/Aether/Scenes/Boot.unity\n"
                "    guid: 87a6078f48c05d93e39d2b60816f6b93\n")
        if intro + boot not in original:
            raise AssertionError("the two scene entries are not in the order this control expects")
        open(path, "w", encoding="utf-8").write(original.replace(intro + boot, boot + intro, 1))
        return [(path, original)]

    def hide_meta(root):
        path = os.path.join(root, f"{CODE}/Aether.Gameplay/Runtime/Levels/LevelDirector.cs.meta")
        original = open(path, encoding="utf-8").read()
        os.remove(path)
        return [("MISSING_META", (path, original))]

    return [
        Control(
            "verify: a member read on a project type that does not have it",
            # The exact second-compile error: a row is a Selectable, a Selectable is not a Graphic,
            # and rectTransform therefore does not exist on it. The read is reached through a local,
            # which is what made it invisible before locals counted as receivers.
            gate,
            lambda root: edit(root, CONFIRM_PANEL,
                              "MenuUi.Corner(panel._cancel.Rect, new Vector2(0f, 0f),",
                              "MenuUi.Corner(panel._cancel.rectTransform, new Vector2(0f, 0f),"),
            ["rectTransform", "no member"]),
        Control(
            "verify: a type named by simple name without importing its namespace",
            # The exact first-compile error: CameraSettings lives in Aether.Core.Settings, and this
            # file used the bare name with no using directive to reach it.
            gate,
            lambda root: edit(root, CAMERA_FOLLOW,
                              "using Aether.Core.Settings;\nusing Aether.Gameplay.Settings;",
                              "using Aether.Gameplay.Settings;"),
            ["CameraSettings", "without importing it"]),
        Control(
            "verify: a nested type of the same name elsewhere does not capture an engine type",
            # UnityEngine.Bounds is a struct with centre, extents and size. A private nested struct
            # called Bounds lives in the intro sequence. A name is not a type: this must stay silent,
            # and it was not silent until the model learned to ask whether a name can be seen at all.
            gate,
            lambda root: edit(root, MOTOR,
                              "            float halfHeight = bounds.extents.y;",
                              "            float halfHeight = bounds.extents.y;\n"
                              "            float slop = bounds.center.y - bounds.min.y;"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a name being declared is not a type being referenced",
            # A field called Haptics, in a file that does not import the Haptics type. The name is
            # an identifier here, and a rule about imports must not read it as a reference.
            gate,
            lambda root: edit(root, PLAYER_MOTOR,
                              "        private float _groundProbeInset = 0.03f;",
                              "        private float _groundProbeInset = 0.03f;\n"
                              "        private bool Haptics = true;"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a uGUI type named without the directive that brings it in",
            # The last error the editor found in this change set: the play-mode tests named
            # CanvasScaler with only `using UnityEngine;` in scope. It is the mirror of the
            # missing-import rule above, on the other side of the assembly boundary.
            gate,
            lambda root: edit(root, MENU_FLOW_TESTS,
                              "using UnityEngine.TestTools;\nusing UnityEngine.UI;",
                              "using UnityEngine.TestTools;"),
            ["CanvasScaler", "never imports 'UnityEngine.UI'"]),
        Control(
            "verify: a qualified uGUI name needs no directive at all",
            # A name written out in full - UnityEngine.UI.Image - compiles without any using, and
            # the rule must not demand one. Without this control the check could be satisfied by
            # banning the names outright.
            gate,
            lambda root: edit(root, PLACEHOLDER,
                              "        private const int TextureSize = 64;",
                              "        private const int TextureSize = 64;\n"
                              "        private UnityEngine.UI.Image _qualified;"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a type declares the same member twice",
            # The exact shape the Unity compiler caught after every gate was green: the camera had
            # grown a second OnEnable, one of them subscribing to the settings service.
            gate,
            lambda root: edit(root, CAMERA_FOLLOW,
                              "        private void OnEnable()\n        {\n",
                              "        private void OnEnable()\n        {\n"
                              "            _currentLookAhead = Vector2.zero;\n        }\n\n"
                              "        private void OnEnable()\n        {\n"),
            ["duplicate", "OnEnable"]),
        Control(
            "verify: an override of a member the base does not declare",
            # Selectable implements neither IPointerClickHandler nor ISubmitHandler - only Button
            # does - so this override compiles nowhere, and nothing else in the gate can see it.
            gate,
            lambda root: edit(root, MENU_BUTTON,
                              "public void OnPointerClick(PointerEventData eventData)",
                              "public override void OnPointerClick(PointerEventData eventData)"),
            ["override", "OnPointerClick"]),
        Control(
            "verify: a legal overload is not reported as a duplicate",
            # Proves the duplicate rule compares signatures rather than names. A second Activate
            # that takes an argument is an overload, which C# allows, and a rule that keyed on the
            # name alone would call it a compile error.
            gate,
            lambda root: edit(root, MENU_BUTTON,
                              "        public void Activate()\n",
                              "        /// <summary>An overload, for the control.</summary>\n"
                              "        internal void Activate(int times)\n"
                              "        {\n"
                              "            for (int i = 0; i < times; i++) Activate();\n"
                              "        }\n\n"
                              "        public void Activate()\n"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: overriding a member object declares is silent",
            # The same rule, looking the other way: ToString is virtual on System.Object and every
            # Unity type inherits it, so this override is exactly what the rule must accept.
            gate,
            lambda root: edit(root, CAMERA_FOLLOW,
                              "        private void Awake()\n        {\n"
                              "            _camera = GetComponent<Camera>();\n        }",
                              "        private void Awake()\n        {\n"
                              "            _camera = GetComponent<Camera>();\n        }\n\n"
                              "        /// <summary>An override of an inherited member, for the control.</summary>\n"
                              "        public override string ToString()\n        {\n"
                              "            return name + \" camera\";\n        }"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: an asset points at a GUID that does not exist",
            gate,
            lambda root: edit(root, STRIKE, "4f1ae941f1f47e81b43fd98b116aa044",
                              "00000000000000000000000000000000"),
            ["unknown guid"]),
        Control(
            "verify: a script lost its .meta file",
            gate,
            hide_meta,
            ["missing .meta"]),
        Control(
            "verify: a call to a member that does not exist",
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Gameplay/Runtime/Levels/LevelDirector.cs",
                              "_player.Health.Died += OnPlayerDied;",
                              "_player.Health.Diedd += OnPlayerDied;"),
            ["no member 'Diedd'"]),
        Control(
            "verify: a project method called with the wrong number of arguments",
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Gameplay/Runtime/Levels/LevelDirector.cs",
                              "_player.ResetForRespawn(RespawnFeet + new Vector2(0f, halfHeight));",
                              "_player.ResetForRespawn();"),
            ["ResetForRespawn"]),
        Control(
            "verify: an empty folder under Assets (it would vanish on clone)",
            gate,
            empty_folder,
            ["is empty"]),
        Control(
            "verify: Aether.Data uses a namespace owned by Aether.Gameplay",
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Data/Runtime/Levels/PlayableNode.cs",
                              "namespace Aether.Data.Levels",
                              "using Aether.Gameplay.Levels;\n\nnamespace Aether.Data.Levels"),
            ["Aether.Gameplay", "does not reference"]),
        Control(
            "verify: the same illegal dependency, written inline instead of with 'using'",
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Data/Runtime/Levels/PlayableNode.cs",
                              "    public readonly struct PlayableNode\n",
                              "    public readonly struct PlayableNode : "
                              "Aether.Gameplay.Levels.LevelDirector\n"),
            ["Aether.Gameplay", "does not reference"]),
        Control(
            "verify: a bare type name crosses an assembly boundary with no using and no qualifier",
            # The exact shape that reached the first real Unity build: AttackRunner lived in
            # Aether.Core (which references nothing) and read AttackDefinition out of Aether.Data,
            # with no using directive and no qualified name, so neither of the two existing
            # namespace rules had anything to look at.
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Core/Runtime/Combat/Damage.cs",
                              "    public readonly struct DamageInfo",
                              "    internal sealed class Borrowed\n    {\n"
                              "        private AttackDefinition _definition;\n    }\n\n"
                              "    public readonly struct DamageInfo"),
            ["AttackDefinition", "does not reference"]),
        Control(
            "verify: the same reference is silent when the assembly may legally see it",
            # Aether.Gameplay references Aether.Data, so the identical line there is correct and
            # must not be reported. Without this control the rule could be satisfied by simply
            # flagging the type name everywhere.
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Gameplay/Runtime/Player/PlayerCombat.cs",
                              "        private readonly AttackRunner _runner = new AttackRunner();",
                              "        private readonly AttackRunner _runner = new AttackRunner();\n"
                              "        private AttackDefinition _legallyVisible;"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a file names Mathf without importing UnityEngine",
            # Exactly what the second licensed build stopped on, ten minutes into a container.
            # A single balanced member, so the syntax check has nothing to say and the engine
            # rule is the only thing that can catch it.
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Data/Runtime/Levels/PlayableNode.cs",
                              "        public PlayableNode(int col, int row)",
                              "        private static readonly float Half = Mathf.Abs(0.5f);\n\n"
                              "        public PlayableNode(int col, int row)"),
            ["Mathf", "never imports UnityEngine"]),
        Control(
            "verify: the same name is silent in a file that does import UnityEngine",
            gate,
            lambda root: edit(root,
                              f"{CODE}/Aether.Gameplay/Runtime/Support/PlaceholderVisuals.cs",
                              "        private const int TextureSize = 64;",
                              "        private const int TextureSize = 64;\n"
                              "        private static readonly float HalfSize = Mathf.Abs(0.5f);"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a nested type's member is used without its container",
            # The third licensed build's failure: three lines, six errors, all of them
            # 'The name JumpCentre does not exist in the current context'. JumpCentre belongs to
            # the nested Layout class and the enclosing view cannot see it unqualified.
            gate,
            lambda root: edit(root,
                              f"{CODE}/Aether.Gameplay/Runtime/Controls/TouchControlsView.cs",
                              "Layout.Centre(Layout.JumpCentre)",
                              "Layout.Centre(JumpCentre)"),
            ["scope:", "JumpCentre", "not in scope"]),
        Control(
            "verify: a local variable that shadows a nested member name is silent",
            # Proves the rule is not simply 'the name is banned'. A local called JumpRadius is
            # perfectly legal C#, and the tool must not report it.
            gate,
            lambda root: edit(root,
                              f"{CODE}/Aether.Gameplay/Runtime/Controls/TouchControlsView.cs",
                              "            float radius = radiusFraction * Screen.height;",
                              "            float JumpRadius = radiusFraction * Screen.height;\n"
                              "            float radius = JumpRadius;"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "levelcheck: a wide gap makes the exit genuinely unreachable",
            solver,
            lambda root: level_rows(root, [(20, 94, 6, "......"),
                                           (21, 94, 6, "......"),
                                           (22, 94, 6, "......")]),
            ["exit"]),
        Control(
            "levelcheck: a step appears on a route that must be walkable",
            solver,
            lambda root: level_rows(root, [(19, 8, 1, "#")]),
            ["must=walk"]),
        Control(
            "levelcheck: the canopy route to the first secret is removed",
            solver,
            lambda root: level_rows(root, [(16, 76, 3, "..."), (17, 76, 3, "...")]),
            ["secret"]),
        Control(
            "levelcheck: an entity placed inside solid geometry",
            solver,
            lambda root: edit(root, LEVEL, "x=117, y=20", "x=117, y=23"),
            ["exit"]),
        Control(
            "levelcheck: a misspelled entity attribute is rejected",
            solver,
            lambda root: edit(root, LEVEL,
                              "type=forest_stalker, x=47, y=20, patrol=4",
                              "type=forest_stalker, x=47, y=20, patrl=4"),
            ["unknown attribute 'patrl'"]),
        Control(
            "levelcheck: a tile character that is not in the legend",
            solver,
            lambda root: level_rows(root, [(20, 50, 1, "?")]),
            ["?"]),
        Control(
            "levelcheck: a platform inside the play space that nothing can reach",
            solver,
            lambda root: level_rows(root, [(17, 50, 1, "=")]),
            ["looks reachable"]),
        Control(
            "levelcheck: wall crowns stay reported as out of reach by design, not as problems",
            solver,
            lambda root: level_rows(root, [(12, 0, 1, "#"), (11, 0, 1, "#"), (10, 0, 1, "#")]),
            ["out of reach by design", "8/8 traversal claims proven"],
            must_fail=False),
        Control
        (
            "verify: a bogus member on a foreach-typed variable",
            gate,
            # `stalker` is used nowhere else, so the tool can resolve it; a name that is used
            # with two different types on purpose (`other`) is skipped by design and would prove
            # nothing here.
            lambda root: edit(root, f"{CODE}/Aether.Gameplay/Runtime/Levels/LevelDirector.cs",
                              "            for (int i = 0; i < _enemies.Count; i++)",
                              "            foreach (EnemyController stalker in _enemies)\n"
                              "            {\n"
                              "                stalker.NotAMember();\n"
                              "            }\n"
                              "\n"
                              "            for (int i = 0; i < _enemies.Count; i++)"),
            ["NotAMember"]),
        Control(
            "levelcheck: an archetype that points at no attack",
            solver,
            lambda root: edit(root, ENEMY, "  _attack: {fileID: 11400000",
                              "  _attack: {fileID: 0"),
            ["points at no attack"]),
        Control(
            "levelcheck: an enemy open for less time than it warned for",
            solver,
            lambda root: edit(root, ENEMY, "  _recoveryPause: 0.65", "  _recoveryPause: 0.05"),
            ["open for less time"]),
        Control(
            "verify: an asset key that does not exist on its script (Unity ignores it silently)",
            gate,
            lambda root: edit(root, "Assets/Aether/Resources/Content/PlayerTuning.asset",
                              "  _runSpeed: 7", "  _runSpeedd: 7"),
            ["_runSpeedd", "silently"]),
        Control(
            "levelcheck: two encounters close enough to see the same ground",
            solver,
            lambda root: edit(root, LEVEL, "enemy        = id=enemy.greenway.stalker.02, type=forest_stalker, x=91, y=18",
                              "enemy        = id=enemy.greenway.stalker.02, type=forest_stalker, x=94, y=20"),
            ["both see"]),
        Control(
            "levelcheck: a checkpoint that respawns the player inside an enemy's senses",
            solver,
            lambda root: (edit(root, LEVEL,
                               "enemy        = id=enemy.greenway.stalker.02, type=forest_stalker, x=91, y=18",
                               "enemy        = id=enemy.greenway.stalker.02, type=forest_stalker, x=91, y=20")
                          + edit(root, LEVEL, "respawn=86:20", "respawn=88:20")),
            ["punished twice"]),
        Control(
            "levelcheck: an exit inside an enemy's senses",
            solver,
            lambda root: edit(root, LEVEL, "exit         = id=exit.greenway.north, x=117, y=20",
                              "exit         = id=exit.greenway.north, x=105, y=20"),
            ["senses"]),
        Control(
            "levelcheck: a secret a player reaches without jumping",
            solver,
            lambda root: edit(root, LEVEL,
                              "discovery    = id=secret.greenway.overhang, x=81, y=15",
                              "discovery    = id=secret.greenway.overhang, x=8, y=20"),
            ["without jumping"]),
        Control(
            "verify: a bogus member through a type whose name is shared by nested classes",
            gate,
            lambda root: edit(root, f"{CODE}/Aether.Gameplay/Runtime/Player/PlayerController.cs",
                              "        private PlayerTuningData _tuning;",
                              "        private PlayerTuningData _tuning;\n"
                              "        private DeadState _probe;\n"
                              "\n"
                              "        private void ProbeMergedType()\n"
                              "        {\n"
                              "            _probe.NotAMember();\n"
                              "        }"),
            ["NotAMember"]),

        # -- the studio intro's numbers ------------------------------------------------------
        Control(
            "intro: the mark is allowed to grow past the safe area",
            intro,
            lambda root: retune(root, INTRO_CODE, "MaxHeightFraction", "1.50"),
            ["MaxHeightFraction"]),
        Control(
            "intro: the mark shrinks to a watermark",
            intro,
            lambda root: (retune(root, INTRO_CODE, "MaxWidthFraction", "0.12")
                          + retune(root, INTRO_CODE, "MaxHeightFraction", "0.10")),
            ["larger dimension"]),
        Control(
            "intro: the light that crosses the logo is a flare",
            intro,
            lambda root: retune(root, INTRO_CODE, "HighlightPeak", "1.60"),
            ["HighlightPeak"]),
        Control(
            "intro: the light is a wash wider than the lockup it crosses",
            intro,
            lambda root: retune(root, INTRO_CODE, "BandHalfWidth", "2.00"),
            ["BandHalfWidth"]),
        Control(
            "intro: the light takes the logo down like a flicker",
            intro,
            lambda root: retune(root, INTRO_CODE, "DimWhilePassing", "0.60"),
            ["DimWhilePassing"]),
        Control(
            "intro: the light is still crossing when the logo should be still",
            intro,
            lambda root: retune(root, INTRO_CODE, "SheenDuration", "1.50"),
            ["SheenDuration"]),
        Control(
            "intro: the light starts before the mark has resolved",
            intro,
            lambda root: retune(root, INTRO_CODE, "SheenStartsAt", "0.60"),
            ["SheenStartsAt"]),
        Control(
            "intro: the logo is cut to black instead of fading",
            intro,
            lambda root: retune(root, INTRO_CODE, "Exit", "0.05"),
            ["Exit"]),
        Control(
            "intro: the sequence runs far longer than the two-to-three seconds it must",
            intro,
            lambda root: retune(root, INTRO_CODE, "Hold", "6.00"),
            ["outside the required"]),
        Control(
            "intro: skipping takes longer than watching it out",
            intro,
            lambda root: retune(root, INTRO_CODE, "SkipExit", "0.90"),
            ["not shorter"]),
        Control(
            "intro: a size limit that is still inside the rules stays silent",
            intro,
            lambda root: retune(root, INTRO_CODE, "MaxHeightFraction", "0.30"),
            ["sound"],
            must_fail=False),

        # -- the artwork the intro actually draws --------------------------------------------
        Control(
            "intro: the artwork the scene names is not in the project",
            intro,
            unlink_artwork,
            ["no image is at"]),
        Control(
            "intro: the artwork is a canvas with nothing drawn on it",
            intro,
            lambda root: plant_artwork(root, lambda x, y: (255, 255, 255, 255)),
            ["keys down to", "would draw nothing"]),
        Control(
            "intro: the artwork is a light logo on a dark canvas, which the key cannot remove",
            intro,
            lambda root: plant_artwork(
                root,
                lambda x, y: ((255, 255, 255, 255) if 8 <= x < 24 and 8 <= y < 24
                              else (40, 40, 40, 255))),
            ["border luminance", "grey rectangle"]),
        Control(
            "intro: the split leaves no mark above it",
            intro,
            lambda root: retune(root, INTRO_CODE, "WordmarkSplit", "0.22"),
            ["no ink above"]),
        Control(
            "intro: the wordmark arrives after the light has already passed",
            intro,
            lambda root: retune(root, INTRO_CODE, "WordmarkDelay", "0.90"),
            ["WordmarkDelay"]),
        Control(
            "intro: the key gap is too narrow to key without smearing",
            intro,
            lambda root: retune(root, INTRO_CODE, "BackgroundLuminance", "0.30"),
            ["separates ink from canvas"]),
        Control(
            "intro: a logo exported with a real alpha channel stays silent",
            intro,
            lambda root: plant_artwork(
                root,
                lambda x, y: ((255, 255, 255, 255) if 8 <= x < 24 and 8 <= y < 24
                              else (255, 255, 255, 0)),
                alpha=True),
            ["sound"],
            must_fail=False),
        Control(
            "intro: a looser split that still cuts between mark and wordmark stays silent",
            intro,
            lambda root: retune(root, INTRO_CODE, "WordmarkSplit", "0.55"),
            ["sound"],
            must_fail=False),

        # -- how the app launches, before any of our code runs --------------------------------
        Control(
            "verify: portrait becomes an allowed orientation again",
            gate,
            lambda root: edit(root, PLAYER_SETTINGS,
                              "  allowedAutorotateToPortrait: 0",
                              "  allowedAutorotateToPortrait: 1"),
            ["allowedAutorotateToPortrait"]),
        Control(
            "verify: rotation follows the device's own lock instead of the sensor",
            gate,
            lambda root: edit(root, PLAYER_SETTINGS,
                              "  androidAutoRotationBehavior: 1",
                              "  androidAutoRotationBehavior: 0"),
            ["Sensor"]),
        Control(
            "verify: the Unity splash comes back ahead of the studio intro",
            gate,
            lambda root: edit(root, PLAYER_SETTINGS,
                              "  m_ShowUnitySplashScreen: 0",
                              "  m_ShowUnitySplashScreen: 1"),
            ["m_ShowUnitySplashScreen"]),
        Control(
            "verify: the intro hands over to a bright page instead of to black",
            gate,
            lambda root: edit(root, BOOT_SCENE,
                              "  m_BackGroundColor: {r: 0.019607844, g: 0.019607844, b: 0.019607844, a: 1}",
                              "  m_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 1}"),
            ["hands over to a bright page"]),
        Control(
            "verify: a ground that is still dark enough stays silent",
            gate,
            lambda root: edit(root, PLAYER_SETTINGS,
                              "  m_SplashScreenBackgroundColor: {r: 0.019607844, g: 0.019607844, b: 0.019607844, a: 1}",
                              "  m_SplashScreenBackgroundColor: {r: 0.04, g: 0.04, b: 0.04, a: 1}"),
            ["landscape only"],
            must_fail=False),

        # -- the scene the app starts in -----------------------------------------------------
        Control(
            "verify: the studio intro is not the first scene in the build order",
            gate,
            demote_the_intro,
            ["not the first enabled scene"]),
        Control(
            "verify: a scene is listed in the build order under the wrong guid",
            gate,
            lambda root: edit(root, BUILD_SETTINGS,
                              "    guid: 8e727f3605223dbc5e7912d5b4306283",
                              "    guid: 00000000000000000000000000000000"),
            ["is listed with guid"]),
        Control(
            "verify: the intro hands over to a scene the build does not contain",
            gate,
            lambda root: edit(root, INTRO_SCENE, "  _nextScene: Boot", "  _nextScene: Nowhere"),
            ["is not an enabled"]),
        Control(
            "verify: a component in a scene names a field that does not exist",
            gate,
            lambda root: edit(root, INTRO_SCENE, "  _playOnAwake: 1", "  _playOnAwakee: 1"),
            ["does not exist on"]),
        Control(
            "verify: the intro scene has no studio-intro component in it",
            gate,
            lambda root: edit(root, INTRO_SCENE,
                              "guid: d2ab92089ed7c270787404d95bb62195",
                              "guid: 87a6078f48c05d93e39d2b60816f6b93"),
            ["does not contain a component"]),
        Control(
            "verify: the intro scene's camera draws the skybox instead of a black screen",
            gate,
            lambda root: edit(root, INTRO_SCENE, "  m_ClearFlags: 2", "  m_ClearFlags: 1"),
            ["instead of Solid Color"]),
        Control(
            "verify: the intro scene's camera background is not black",
            gate,
            lambda root: edit(root, INTRO_SCENE,
                              "  m_BackGroundColor: {r: 0, g: 0, b: 0, a: 1}",
                              "  m_BackGroundColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}"),
            ["is not black"]),
        Control(
            "verify: brand art is in the project but nothing draws it",
            gate,
            lambda root: (edit(root, INTRO_SCENE, "  _markResourcePath: Brand/VarellonLogo",
                               "  _markResourcePath: Brand/OtherMark")
                          + edit(root, INTRO_CODE, '_markResourcePath = "Brand/VarellonLogo"',
                                 '_markResourcePath = "Brand/OtherMark"')),
            ["does not name it"]),
        Control(
            "verify: a brand texture is imported without alpha transparency",
            gate,
            bad_brand_texture,
            ["alphaIsTransparency"]),

        # -- the repairs the menu work needed, each guarded in both directions -----------------
        #
        # Three of these cover rules that were *wrong* rather than missing, and a wrong rule is the
        # more dangerous kind: it fails correct code, and the failure looks like a real problem.
        # The pair below is the shape to use for that — one mutation that must be reported and one
        # that must not, so a rule that simply stopped checking cannot pass both.
        Control(
            "verify: a member inherited from a base class resolves",
            # `class MenuSettingsPanel : MenuPanel`, written with a space before the colon, lost its
            # base entirely — so every inherited member looked missing and the rule failed correct
            # code. Show() is declared in MenuPanel and reached here through a derived-typed field.
            gate,
            lambda root: edit(root, f"{MENUS}/Screens/SettingsScreen.cs",
                              "            _categories.Hide();",
                              "            _categories.Hide();\n            _categories.Show(false);"),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: a member that exists on neither a type nor its base is caught",
            gate,
            lambda root: edit(root, f"{MENUS}/Screens/SettingsScreen.cs",
                              "            _categories.Hide();",
                              "            _categories.Hide();\n            _categories.NotAMember();"),
            ["MenuSettingsPanel", "NotAMember"]),
        Control(
            "verify: a call whose first argument is a string literal is counted correctly",
            # The argument reader used to run over the view of the source with string literals
            # blanked. A blanked first argument is an empty argument, and the empty ones were
            # dropped — so every correct call whose first argument was a literal looked one
            # argument short, and the arity rule reported a failure that did not exist.
            gate,
            lambda root: edit(root, f"{MENUS}/Panels/MenuTitlePanel.cs",
                              "            float y = 0f;",
                              '            MenuUi.CreateImage("Probe", Rect, MenuArt.Glow, '
                              'MenuTheme.Palette.Accent);\n\n            float y = 0f;'),
            ["VERIFICATION PASSED"],
            must_fail=False),
        Control(
            "verify: an over-long call is caught",
            gate,
            lambda root: edit(root, f"{MENUS}/Panels/MenuCreditsPanel.cs",
                              'MenuUi.CreateParagraph("Body " + block.BodyKey, _content,',
                              'MenuUi.CreateParagraph("Body " + block.BodyKey, _content, 1f,'),
            ["CreateParagraph", "called with"]),
        Control(
            "verify: a variadic method takes as many arguments as it is given",
            # `params object[] arguments` accepts any number past the first; a range that stopped
            # at the declaration's own count reported every formatted string as an error.
            gate,
            lambda root: edit(root, f"{MENUS}/Panels/MenuCategoryPanel.cs",
                              'MenuStrings.Format("data.info.run", summary.AbilitesOwned, summary.FlagsEstablished)',
                              'MenuStrings.Format("data.info.run", 1, 2, 3, 4)'),
            ["VERIFICATION PASSED"],
            must_fail=False),

        # -- the menu's own gate ---------------------------------------------------------------
        Control(
            "mainmenu: the menu scene is not in the build order",
            menu,
            lambda root: edit(root, BUILD_SETTINGS,
                              "  - enabled: 1\n"
                              "    path: Assets/Aether/Scenes/MainMenu.unity\n"
                              "    guid: d2c67318bdf89dd04b5d9c73766abe15\n",
                              ""),
            ["is not in the build order"]),
        Control(
            "mainmenu: a taller row pitch pushes the main menu off the screen",
            # The composition is arithmetic, and this is the arithmetic failing: two bands of
            # secondary rows a hundred units taller each do not fit a 1080-unit canvas.
            menu,
            lambda root: edit(root, THEME,
                              "            public const float SecondaryPitch = 148f;",
                              "            public const float SecondaryPitch = 248f;"),
            ["would scroll at the default interface size"]),
        Control(
            "mainmenu: a screen shows a literal instead of a string key",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              'MenuButton row = panel.AddEntry(destination.ToString(), '
                              'MenuStrings.Get(labelKey),',
                              'MenuButton row = panel.AddEntry(destination.ToString(), "CONTINUE",'),
            ["shows the literal"]),
        Control(
            "mainmenu: the main menu drops one of its entry points",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              "AddDestination(_explore, MenuScreenId.Achievements, "
                              "\"menu.achievements\");",
                              ""),
            ["no row for Achievements"]),
        Control(
            "mainmenu: a setting row names a value the string table does not hold",
            menu,
            lambda root: edit(root, CATALOG,
                              '\"setting.haptics.help\"', '\"setting.haptics.helpx\"'),
            ["no 'setting.haptics.helpx'"]),
        Control(
            "gameplay: the region's strip shows a literal instead of a key",
            menu,
            lambda root: edit(root, INTERFACE_HUD,
                              'MenuButton.Create("Pause", parent, MenuStrings.Get("hud.pause"),',
                              'MenuButton.Create("Pause", parent, "PAUSE",'),
            ["shows the literal"]),
        Control(
            "gameplay: the pause menu asks for a string key the table does not hold",
            menu,
            lambda root: edit(root, INTERFACE_PAUSE,
                              'MenuStrings.Get("pause.resume")',
                              'MenuStrings.Get("pause.resumee")'),
            ["which the string table does not hold"]),
        Control(
            "gameplay: the region's shell searches the scene every frame",
            menu,
            lambda root: edit(root, INTERFACE_SHELL,
                              "            _clock += Time.unscaledDeltaTime;",
                              "            _clock += Time.unscaledDeltaTime;\n"
                              "            var _ = Camera.main;"),
            ["a per-frame search or allocation"]),
        Control(
            "gameplay: a second place in the region loads a scene",
            menu,
            lambda root: edit(root, INTERFACE_SHELL,
                              "            ReadPauseKey();",
                              "            ReadPauseKey();\n"
                              "            if (UnityEngine.SceneManagement.SceneManager."
                              "GetActiveScene().name == \"never\") return;"),
            ["scene loading happens in"]),
        Control(
            "localization: a generated table ends its last argument like an initialiser",
            # The build caught this one before any tool did: brackets balance, and the file still
            # does not compile. The generator now refuses to write it, and this proves it refuses.
            ["python3", "tools/verify/localization.py", "--write"],
            lambda root: edit(root, "tools/verify/localization.py",
                              'comma = "," if index + 1 < len(pairs) else ""',
                              'comma = "," if index + 1 < len(pairs) else ";"'),
            ["has a ';' between its arguments"]),
        Control(
            "localization: a translation drops a placeholder English has",
            strings,
            lambda root: edit(root, "tools/localization/strings_es.txt",
                              "menu.slots.used={0} DE {1} RANURAS USADAS",
                              "menu.slots.used={0} RANURAS USADAS"),
            ["placeholders do not match English"]),
        Control(
            "localization: a sheet invents a key English does not have",
            strings,
            lambda root: append_line(root, "tools/localization/strings_es.txt",
                                     "menu.titulo=PROYECTO AETHER"),
            ["key(s) that English does not have"]),
        Control(
            "localization: a sheet is left incomplete",
            strings,
            lambda root: edit(root, "tools/localization/strings_es.txt",
                              "ach.unbroken.body=Llega a la salida norte sin morir ni una vez.\n",
                              ""),
            ["not translated"]),
        # The two controls for the screens that used to be placeholders. The first is the fault
        # that actually happened: a label key that the table does not hold, which the player would
        # have seen as the key itself. The second is the opposite fault, and the more dangerous
        # one — a screen that exists, is reachable, and reads nothing real.
        Control(
            "mainmenu: a label key the table does not hold is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              'MenuScreenId.Chapters, "menu.chapters"',
                              'MenuScreenId.Chapters, "menu.chapterz"'),
            ["passes 'menu.chapterz' as a label key"]),
        Control(
            "mainmenu: a catalogue screen that reads nothing real is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/AchievementsScreen.cs",
                              "AchievementCatalog.All.Length",
                              "3"),
            ["never reads AchievementCatalog.All.Length"]),
        # The fault that reached CI twice: an alignment where a tracking metric belongs. The call
        # still parses, still balances, and would not compile — which is exactly the class of
        # mistake this gate has to be able to see.
        Control(
            "mainmenu: a builder called with its arguments out of order is caught",
            menu,
            lambda root: edit(root, f"{INTERFACE_HUD}",
                              "MenuTheme.Metrics.SubtitleTracking, TextAnchor.UpperLeft",
                              "TextAnchor.UpperLeft, MenuTheme.Metrics.SubtitleTracking"),
            ["argument 6"]),
        Control(
            "mainmenu: a format call with the wrong number of arguments is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              'MenuStrings.Format("about.version", Application.version)',
                              'MenuStrings.Format("about.version", Application.version, 1)'),
            ["placeholder(s)"]),
        # A ninth row: the composition was designed for eight, and the arithmetic that proves it fits
        # counts the rows the screen builds. Adding one has to fail here rather than on a phone.
        Control(
            "mainmenu: one row too many for the composition is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              'AddDestination(_system, MenuScreenId.Credits, "menu.credits")',
                              'AddDestination(_system, MenuScreenId.Credits, "menu.credits")\n'
                              '            AddDestination(_system, MenuScreenId.Chapters, "menu.chapters")'),
            ["would scroll at the default interface size"]),
        # The two faults that reached CI in the save-slot screen: a call to a method that was never
        # written, and an assignment to a name that was never declared. Both parse, both balance, and
        # the compiler is the only other thing that sees them.
        Control(
            "mainmenu: a call to a method the file does not declare is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/SaveSlotScreen.cs",
                              "Dialog.Cancel();", "DismissTheQuestion();"),
            ["calls DismissTheQuestion(...)"]),
        Control(
            "mainmenu: an assignment to a name that is declared nowhere is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/SaveSlotScreen.cs",
                              "_note = _notes.AddEntry(", "note = _notes.AddEntry("),
            ["assigns to 'note'"]),
        Control(
            "mainmenu: a member handed to a row that was never written is caught",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/SaveSlotScreen.cs",
                              "_header.Back.Activated = Leave;",
                              "_header.Back.Activated = DepartToMenu;"),
            ["passes 'DepartToMenu' as a value"]),
        Control(
            "mainmenu: a legitimate change to the composition is silent",
            menu,
            lambda root: edit(root, f"{MENUS}/Screens/MainMenuScreen.cs",
                              "        private const float FooterHeight = 40f;",
                              "        private const float FooterHeight = 44f;"),
            ["checks passed"],
            must_fail=False),
    ]


def run_control(root: str, control: Control) -> bool:
    undo = control.mutate(root)
    try:
        try:
            proc = subprocess.run(
                control.tool,
                cwd=root,
                capture_output=True,
                text=True,
                timeout=CONTROL_TIMEOUT_SECONDS,
            )
        except subprocess.TimeoutExpired:
            print(
                f"  FAIL  ${control.name}\\n"
                f"          timed out after {CONTROL_TIMEOUT_SECONDS}s; "
                "the verifier must fail fast on injected faults",
                flush=True,
            )
            return False

        output = proc.stdout + proc.stderr
        # The solver reports structural problems under their own prefix, and a raised Python
        # exception would mean the tool crashed rather than reported.
        failures = [line.strip() for line in output.splitlines()
                    if line.strip().startswith(("FAIL", "FAILED", "STRUCTURAL", "error", "Traceback"))]

        if control.must_fail:
            mentioned = all(any(word in line for line in failures) for word in control.mentions)
            ok = proc.returncode != 0 and bool(failures) and mentioned
            evidence = failures[0][:140] if failures else "nothing"
        else:
            # The mutation is a legitimate level or code shape; the tool must stay quiet.
            mentioned = all(word in output for word in control.mentions)
            ok = proc.returncode == 0 and not failures and mentioned
            evidence = next((line.strip() for line in output.splitlines()
                             if any(word in line for word in control.mentions)), "nothing")

        print(f"  {'PASS' if ok else 'FAIL'}  {control.name}")
        print(f"          {'detected' if control.must_fail else 'declared clean'}: {evidence}")
        if not ok:
            print(f"          exit {proc.returncode}; expected mentions {control.mentions}")
        return ok
    finally:
        # Reverse order: a control that edits the same file twice returns both originals, and the
        # *first* one is the clean copy. Restoring forwards would write the intermediate text back.
        for entry in reversed(undo):
            if entry[0] == "WRITE_BYTES":
                with open(entry[1][0], "wb") as fh:
                    fh.write(entry[1][1])
            elif entry[0] == "MISSING_META":
                with open(entry[1][0], "w", encoding="utf-8") as fh:
                    fh.write(entry[1][1])
            elif entry[0] == "DELETE_TREE":
                shutil.rmtree(entry[1], ignore_errors=True)
            elif entry[0] == "DELETE":
                if os.path.exists(entry[1]):
                    os.remove(entry[1])
            else:
                path, original = entry
                with open(path, "w", encoding="utf-8") as fh:
                    fh.write(original)


def main() -> int:
    temporary = tempfile.mkdtemp(prefix="aether-negative-controls-")
    root = os.path.join(temporary, "repo")
    os.makedirs(root)
    for name in COPY_DIRS:
        shutil.copytree(os.path.join(REPO_ROOT, name), os.path.join(root, name), ignore=IGNORE)

    try:
        print(f"Negative controls against a copy of the repository in {root}\n")
        outcomes = [run_control(root, control) for control in controls()]
        passed = sum(outcomes)
        print(f"\n{passed}/{len(outcomes)} injected faults detected")
        return 0 if passed == len(outcomes) else 1
    finally:
        shutil.rmtree(temporary, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
