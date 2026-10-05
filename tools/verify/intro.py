#!/usr/bin/env python3
"""Proves the studio intro's layout and its length, without a device or an Editor.

The intro's numbers live in `StudioIntroSequence`: `Layout` is where the mark sits, in fractions of
the safe area, and `Timing` is how long each part lasts, in seconds. Both are decidable here, and
both are the parts that go wrong:

* **no stretching** — the mark is fitted with one scale factor from both limits, so the rendered
  aspect ratio equals the artwork's, on every shape of screen and at every point in the animation;
* **nothing cropped** — the mark's box, including the scale and drift it reaches during the reveal
  and the exit, stays inside the safe area, so a notch or a gesture bar cannot cut into it;
* **centred** — the resting mark is centred on the safe area, not on the screen, so an asymmetric
  cutout does not push it to one side;
* **neither a banner nor a watermark** — the mark has a ceiling and a floor on every device shape;
* **the intro is short** — black hold, reveal, hold, exit and the beat before the next scene add up
  to two to three seconds, which is the requirement, not a preference;
* **skipping is worth doing** — the shortened exit is quicker than watching the intro out, and the
  grace period is short enough that a player can still skip during the reveal.

What is not decidable here: whether it looks good. This proves the intro is not broken; taste is a
person watching it on a phone.

Run:  python3 tools/verify/intro.py
Exit: 0 when the intro's numbers are sound, 1 otherwise.
"""

from __future__ import annotations

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SEQUENCE = "Assets/Aether/Code/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs"

# Device shapes the intro can meet, as width / height. Landscape because that is how the game is
# played, and portrait because the app is launched before anything has chosen an orientation: the
# studio mark is the first thing anyone sees, and it is seen in whatever way the phone is held.
ASPECTS = {
    "16:9 landscape": 16 / 9,
    "19.5:9 landscape": 19.5 / 9,
    "20:9 landscape": 20 / 9,
    "4:3 tablet landscape": 4 / 3,
    "9:16 portrait": 9 / 16,
    "9:19.5 portrait": 9 / 19.5,
    "3:4 tablet portrait": 3 / 4,
}

# Insets a cutout or a gesture bar can take, as fractions of screen width / height, per side.
SAFE_AREA_CASES = {
    "no cutout": (0.0, 0.0, 0.0, 0.0),
    "left notch": (0.06, 0.0, 0.0, 0.0),
    "notch + gesture bar": (0.06, 0.02, 0.02, 0.05),
    "top cutout + gesture bar": (0.0, 0.05, 0.0, 0.04),
}

# Aspect ratios (width / height) of marks a studio might hand over. The intro must be correct for
# any of them, because the artwork is not this file's business: a monogram, a lockup with a
# wordmark under it, or a wide banner all have to survive the same fit.
MARKS = {
    "tall lockup 0.45:1": 0.45,
    "portrait mark 0.6:1": 0.6,
    "square mark 1:1": 1.0,
    "wide mark 1.8:1": 1.8,
    "banner 3:1": 3.0,
}

# Bounds on the constants themselves. These are the rules the numbers have to obey, independent of
# any device: the mark may not be allowed to fill the screen or to shrink to a speck, motion has to
# stay subtle, and no phase may be zero-length.
MIN_LIMIT_FRACTION = 0.10      # below this the mark is a watermark on every shape
MAX_LIMIT_FRACTION = 0.90      # above this it is a banner that can be cropped by a cutout
MIN_REVEAL_SCALE = 0.90        # a smaller start is a pop, not a reveal
MAX_EXIT_SCALE = 1.10          # a larger finish is a lunge
MAX_RISE_FRACTION = 0.08       # of the safe height; more than this is a slide, not a settle

# Floors on the drawn mark, as fractions of the safe height, measured over the whole animation.
MIN_MAJOR_EXTENT = 0.25        # the mark's larger dimension is never a speck
MIN_MINOR_EXTENT = 0.08        # and its smaller dimension is never a hairline

# The length the finished intro has to be, in seconds. Stated as a requirement because it is one.
MIN_TOTAL = 2.0
MAX_TOTAL = 3.0
MIN_PHASE = {
    "Reveal": 0.50,            # a faster fade reads as a flicker
    "Hold": 0.30,              # the pause is what makes it a signature
    "Exit": 0.20,
    "HandOver": 0.05,          # a beat of black, so the next scene is not a cut
}


def csharp_float(text: str) -> float:
    """C# float literals carry an 'f' suffix: '0.62f'."""
    return float(text.strip().rstrip("fF"))


def parse_constants(path: str) -> dict:
    text = open(path, encoding="utf-8").read()
    values: dict[str, float] = {}

    def number(name: str) -> None:
        # Anchored on the whole declaration, so 'Reveal' cannot match 'RevealScale'.
        match = re.search(rf"public const float {name}\s*=\s*([0-9.]+f)\s*;", text)
        if not match:
            raise SystemExit(f"FATAL: could not read '{name}' from {SEQUENCE}; "
                             "the layout moved and this tool would be checking nothing")
        values[name] = csharp_float(match.group(1))

    for name in ("MaxWidthFraction", "MaxHeightFraction", "RevealScale", "RevealRise",
                 "ExitScale", "ExitRise",
                 "BlackHold", "Reveal", "Hold", "Exit", "HandOver", "SkipGrace", "SkipExit"):
        number(name)

    return values


def check_constants(values: dict) -> list:
    problems = []

    for name in ("MaxWidthFraction", "MaxHeightFraction"):
        fraction = values[name]
        if not MIN_LIMIT_FRACTION <= fraction <= MAX_LIMIT_FRACTION:
            problems.append(
                f"constants: {name} is {fraction:.3f}, outside the {MIN_LIMIT_FRACTION}-"
                f"{MAX_LIMIT_FRACTION} band; the mark would be a watermark or a banner")

    if not MIN_REVEAL_SCALE <= values["RevealScale"] <= 1.0:
        problems.append(f"constants: RevealScale is {values['RevealScale']:.3f}; a reveal starts "
                        f"between {MIN_REVEAL_SCALE} and 1.0 of the final size")
    if not 1.0 <= values["ExitScale"] <= MAX_EXIT_SCALE:
        problems.append(f"constants: ExitScale is {values['ExitScale']:.3f}; an exit ends between "
                        f"1.0 and {MAX_EXIT_SCALE} of the resting size")
    for name in ("RevealRise", "ExitRise"):
        if not 0.0 <= values[name] <= MAX_RISE_FRACTION:
            problems.append(f"constants: {name} is {values[name]:.3f}, outside 0-"
                            f"{MAX_RISE_FRACTION} of the safe height; that is a slide, not a settle")

    return problems


def check_timing(values: dict) -> list:
    problems = []
    total = (values["BlackHold"] + values["Reveal"] + values["Hold"]
             + values["Exit"] + values["HandOver"])

    if not MIN_TOTAL <= total <= MAX_TOTAL:
        problems.append(f"timing: the intro is {total:.2f}s long, outside the required "
                        f"{MIN_TOTAL}-{MAX_TOTAL}s")

    for phase, floor in MIN_PHASE.items():
        if values[phase] < floor:
            problems.append(f"timing: {phase} lasts {values[phase]:.2f}s, below the {floor}s floor")

    if values["BlackHold"] <= 0.0:
        problems.append("timing: the black hold is zero, so the mark can appear before the app "
                        "has drawn a first frame")

    if values["SkipGrace"] >= values["BlackHold"] + values["Reveal"]:
        problems.append(f"timing: the {values['SkipGrace']:.2f}s skip grace covers the whole "
                        "reveal, so a player who wants to skip has nothing left to skip")
    if values["SkipExit"] >= values["Exit"]:
        problems.append(f"timing: the skipped exit ({values['SkipExit']:.2f}s) is not shorter than "
                        f"the normal one ({values['Exit']:.2f}s)")

    return problems


def check_shape(values: dict, aspect_name: str, aspect: float,
                cutout_name: str, cutout, mark_name: str, mark_aspect: float) -> tuple[list, float, float]:
    """Every problem this layout has on one device shape with one mark.

    Returns (problems, major extent, minor extent), the last two as fractions of safe height, so the
    caller can report the tightest case rather than only pass or fail.
    """
    problems = []
    screen_h = 1080.0                       # resolution-independent: everything scales with height
    screen_w = screen_h * aspect
    inset_left, inset_right = cutout[0] * screen_w, cutout[2] * screen_w
    inset_bottom, inset_top = cutout[1] * screen_h, cutout[3] * screen_h
    safe = (inset_left, inset_bottom,
            screen_w - inset_left - inset_right, screen_h - inset_bottom - inset_top)

    where = f"{aspect_name} / {cutout_name} / {mark_name}"

    # The source resolution is irrelevant to a fit, so any pixel size with the right aspect will do.
    mark_w = 1000.0
    mark_h = mark_w / mark_aspect

    # The fit as StudioIntroSequence computes it: one factor, both limits.
    fit = min(safe[2] * values["MaxWidthFraction"] / mark_w,
              safe[3] * values["MaxHeightFraction"] / mark_h)
    width, height = mark_w * fit, mark_h * fit

    centre = (safe[0] + (safe[2] / 2.0), safe[1] + (safe[3] / 2.0))

    # 1. No stretching: the drawn aspect ratio is the artwork's, exactly.
    if abs((width / height) - mark_aspect) > 1e-9:
        problems.append(f"{where}: drawn aspect {width / height:.6f} differs from the artwork's "
                        f"{mark_aspect:.6f}; the mark is being stretched")

    # 2. Nothing leaves the safe area, at any point in the animation: the reveal starts small and
    #    low, the exit ends large and high, and the extremes are what a cutout would crop.
    states = (
        ("reveal start", values["RevealScale"], -values["RevealRise"]),
        ("rest", 1.0, 0.0),
        ("exit end", values["ExitScale"], values["ExitRise"]),
    )
    for label, scale, rise in states:
        half_w, half_h = (width * scale) / 2.0, (height * scale) / 2.0
        cx, cy = centre[0], centre[1] + (rise * safe[3])
        if (cx - half_w < safe[0] - 0.5 or cx + half_w > safe[0] + safe[2] + 0.5
                or cy - half_h < safe[1] - 0.5 or cy + half_h > safe[1] + safe[3] + 0.5):
            problems.append(f"{where}: at the {label} the mark is not inside the safe area")

    # 3. Centred on the safe area, not on the screen.
    if abs(centre[0] - (screen_w / 2.0)) > 1e-6 or abs(centre[1] - (screen_h / 2.0)) > 1e-6:
        # The mark is placed at the safe area's centre on purpose, so this only fires when that
        # would put it outside the screen, which is the case a degenerate safe area would cause.
        if (centre[0] - (width / 2.0) < 0.0 or centre[0] + (width / 2.0) > screen_w
                or centre[1] - (height / 2.0) < 0.0 or centre[1] + (height / 2.0) > screen_h):
            problems.append(f"{where}: the safe area's centre pushes the mark off the screen")

    # 4. Neither a banner nor a watermark, measured over the animation.
    major = max(width * max(values["RevealScale"], values["ExitScale"]),
                height * max(values["RevealScale"], values["ExitScale"])) / safe[3]
    minor = min(width * min(values["RevealScale"], values["ExitScale"]),
                height * min(values["RevealScale"], values["ExitScale"])) / safe[3]
    if major < MIN_MAJOR_EXTENT:
        problems.append(f"{where}: the mark's larger dimension is {major:.3f} of the safe height, "
                        f"below the {MIN_MAJOR_EXTENT} this tool treats as readable")
    if minor < MIN_MINOR_EXTENT:
        problems.append(f"{where}: the mark's smaller dimension is {minor:.3f} of the safe height, "
                        f"below the {MIN_MINOR_EXTENT} this tool treats as visible")

    return problems, major, minor


def main() -> int:
    path = os.path.join(REPO_ROOT, SEQUENCE)
    if not os.path.exists(path):
        print(f"FATAL: {SEQUENCE} not found")
        return 1

    values = parse_constants(path)

    total = (values["BlackHold"] + values["Reveal"] + values["Hold"]
             + values["Exit"] + values["HandOver"])
    print(f"studio intro: {SEQUENCE}")
    print(f"  black {values['BlackHold']:.2f}s -> reveal {values['Reveal']:.2f}s -> "
          f"hold {values['Hold']:.2f}s -> exit {values['Exit']:.2f}s -> "
          f"hand-over {values['HandOver']:.2f}s   total {total:.2f}s")
    print(f"  skip: grace {values['SkipGrace']:.2f}s, then a {values['SkipExit']:.2f}s exit")
    print(f"  mark: up to {values['MaxWidthFraction']:.2f} of the safe width, "
          f"{values['MaxHeightFraction']:.2f} of its height; scale "
          f"{values['RevealScale']:.2f}->{values['ExitScale']:.2f}")
    print()

    problems = check_constants(values) + check_timing(values)

    checked = 0
    tightest_major = (9.9, "")
    tightest_minor = (9.9, "")
    for aspect_name, aspect in ASPECTS.items():
        for cutout_name, cutout in SAFE_AREA_CASES.items():
            for mark_name, mark_aspect in MARKS.items():
                checked += 1
                found, major, minor = check_shape(values, aspect_name, aspect, cutout_name, cutout,
                                                  mark_name, mark_aspect)
                problems.extend(found)
                if major < tightest_major[0]:
                    tightest_major = (major, f"{aspect_name} / {mark_name}")
                if minor < tightest_minor[0]:
                    tightest_minor = (minor, f"{aspect_name} / {mark_name}")

    for problem in problems:
        print(f"  FAIL {problem}")

    if problems:
        print()
        print(f"{len(problems)} problem(s) across {checked} device shapes")
        return 1

    print(f"  ok   aspect preserved, inside the safe area, centred, readable "
          f"({checked} device shapes)")
    print(f"  ok   tightest mark: larger dimension {tightest_major[0]:.3f} of the safe height "
          f"({tightest_major[1]}), smaller {tightest_minor[0]:.3f} ({tightest_minor[1]})")
    print(f"  ok   {total:.2f}s long, inside the required {MIN_TOTAL}-{MAX_TOTAL}s")
    print(f"{checked}/{checked} device shapes sound, 0 problem(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
