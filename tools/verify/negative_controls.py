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

CODE = "Assets/Aether/Code"
INTRO_CODE = f"{CODE}/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs"
INTRO_SCENE = "Assets/Aether/Scenes/StudioIntro.unity"
BUILD_SETTINGS = "ProjectSettings/EditorBuildSettings.asset"
PLAYER_SETTINGS = "ProjectSettings/ProjectSettings.asset"
BOOT_SCENE = "Assets/Aether/Scenes/Boot.unity"
BRAND_DIR = "Assets/Aether/Resources/Brand"
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
    solver = ["python3", "tools/verify/levelcheck.py"]
    intro = ["python3", "tools/verify/intro.py"]

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
        undo = [("DELETE_TREE", folder)]
        if not folder_existed:
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
                              "CircleHit(position, Layout.JumpCentre, Layout.JumpRadius)",
                              "CircleHit(position, JumpCentre, Layout.JumpRadius)"),
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
            "intro: the mark lunges towards the screen as it leaves",
            intro,
            lambda root: retune(root, INTRO_CODE, "ExitScale", "1.60"),
            ["ExitScale"]),
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
            "intro: the wordmark starts after the reveal has finished",
            intro,
            lambda root: retune(root, INTRO_CODE, "RevealOverlap", "0.95"),
            ["RevealOverlap"]),
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
            lambda root: edit(root, INTRO_SCENE, "  _nextScene: Boot", "  _nextScene: MainMenu"),
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
    ]


def run_control(root: str, control: Control) -> bool:
    undo = control.mutate(root)
    try:
        proc = subprocess.run(control.tool, cwd=root, capture_output=True, text=True)
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
