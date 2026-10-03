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


class Control:
    """One injected fault and the outcome it must produce."""

    def __init__(self, name, tool, mutate, mentions):
        self.name = name
        self.tool = tool
        self.mutate = mutate
        self.mentions = mentions


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
    ]


def run_control(root: str, control: Control) -> bool:
    undo = control.mutate(root)
    try:
        proc = subprocess.run(control.tool, cwd=root, capture_output=True, text=True)
        output = proc.stdout + proc.stderr
        failures = [line.strip() for line in output.splitlines()
                    if line.strip().startswith(("FAIL", "FAILED", "error"))]
        reported = bool(failures)
        mentioned = all(any(word in line for line in failures) for word in control.mentions)
        ok = proc.returncode != 0 and reported and mentioned
        print(f"  {'PASS' if ok else 'FAIL'}  {control.name}")
        if failures:
            print(f"          detected: {failures[0][:140]}")
        else:
            print("          detected: nothing")
        if ok is False:
            print(f"          exit {proc.returncode}; expected mentions {control.mentions}")
        return ok
    finally:
        for entry in undo:
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
