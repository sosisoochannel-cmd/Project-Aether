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
import shutil
import subprocess
import sys
import tempfile

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COPY_DIRS = ("Assets", "ProjectSettings", "Packages", "tools", "docs")
IGNORE = shutil.ignore_patterns(".git", "Library", "Temp", "obj", "__pycache__", "*.bak")

CODE = "Assets/Aether/Code"
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
            if entry[0] == "MISSING_META":
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
