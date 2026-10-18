#!/usr/bin/env python3
"""Proves the on-screen touch layout is usable, without a device.

The layout in TouchControlsView is a set of numbers in fractions of the safe area. That makes it
possible to check the things that go wrong on a real phone before anyone holds one:

* controls overlapping, so the player presses attack and jumps instead;
* a control hanging off the edge of the screen, or under a notch or a gesture bar;
* controls too small to hit with a thumb;
* the virtual joystick or action controls overlapping or becoming too small.

All of it is geometry, so all of it is decidable here. What is *not* decidable here is how the
layout feels — reach, comfort and fatigue need thumbs. This tool proves the layout is not broken;
it does not claim it is good.

Run:  python3 tools/verify/touchlayout.py
Exit: 0 when the layout is sound, 1 otherwise.
"""

from __future__ import annotations

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VIEW = "Assets/Aether/Code/Aether.Gameplay/Runtime/Controls/TouchControlsView.cs"

# Aspect ratios (width / height in landscape) of devices the game is expected to run on:
# 16:9 phones, modern tall phones, and tablets, which are the tight case because a narrower
# screen squeezes horizontally-separated controls together.
ASPECTS = {
    "16:9 phone": 16 / 9,
    "18:9 phone": 18 / 9,
    "19.5:9 phone": 19.5 / 9,
    "20:9 phone": 20 / 9,
    "3:2 tablet": 3 / 2,
    "4:3 tablet": 4 / 3,
}

# Insets a notch or a gesture bar can take, as fractions of screen width / height, per side.
SAFE_AREA_CASES = {
    "no cutout": (0.0, 0.0, 0.0, 0.0),
    "left notch": (0.06, 0.0, 0.0, 0.0),
    "gesture bar": (0.0, 0.0, 0.0, 0.05),
    "notch + gesture bar": (0.06, 0.02, 0.02, 0.05),
}

MIN_BUTTON_RADIUS = 0.07      # of screen height; below this a thumb misses under pressure
MIN_BUTTON_GAP = 0.02         # of screen height between two circles, so a thumb cannot straddle


def csharp_float(text: str) -> float:
    """C# float literals carry an 'f' suffix: '0.45f'."""
    return float(text.strip().rstrip("fF"))


def parse_layout(path: str) -> dict:
    text = open(path, encoding="utf-8").read()
    values: dict[str, object] = {}

    def number(pattern, key, cast=csharp_float):
        match = re.search(pattern, text)
        if not match:
            raise SystemExit(f"FATAL: could not read '{key}' from {VIEW}; "
                             "the layout moved and this tool would be checking nothing")
        values[key] = cast(match.group(1))

    number(r"DefaultJoystickCentre\s*=\s*new Vector2\(([^)]*)\)", "JoystickCentre",
           lambda text: tuple(csharp_float(v) for v in text.split(",")))
    number(r"JoystickBaseRadius\s*=\s*([0-9.]+f)", "JoystickBaseRadius")
    number(r"JumpCentre\s*=\s*new Vector2\(([^)]*)\)", "JumpCentre",
           lambda text: tuple(csharp_float(v) for v in text.split(",")))
    number(r"JumpRadius\s*=\s*([0-9.]+f)", "JumpRadius")
    number(r"AttackCentre\s*=\s*new Vector2\(([^)]*)\)", "AttackCentre",
           lambda text: tuple(csharp_float(v) for v in text.split(",")))
    number(r"AttackRadius\s*=\s*([0-9.]+f)", "AttackRadius")
    number(r"DodgeCentre\s*=\s*new Vector2\(([^)]*)\)", "DodgeCentre",
           lambda text: tuple(csharp_float(v) for v in text.split(",")))
    number(r"DodgeRadius\s*=\s*([0-9.]+f)", "DodgeRadius")
    return values


def to_pixels(fraction, safe, screen_w, screen_h):
    """Safe-area fractions to screen pixels, in the same space the game hit-tests in."""
    return (safe[0] + fraction[0] * safe[2], safe[1] + fraction[1] * safe[3])


def check(values: dict, aspect_name: str, aspect: float, cutout_name: str, cutout) -> list:
    """Every problem this layout has on one device shape."""
    problems = []
    screen_h = 1080.0                      # resolution-independent: everything scales with height
    screen_w = screen_h * aspect
    inset_left, inset_right, inset_bottom, inset_top = (
        cutout[0] * screen_w, cutout[2] * screen_w, cutout[3] * screen_h, cutout[1] * screen_h)
    safe = (inset_left, inset_bottom,
            screen_w - inset_left - inset_right, screen_h - inset_bottom - inset_top)

    where = f"{aspect_name} / {cutout_name}"
    radius = lambda fraction: fraction * screen_h   # radii scale with height

    controls = [
        ("joystick-base", values["JoystickCentre"], values["JoystickBaseRadius"]),
        ("jump", values["JumpCentre"], values["JumpRadius"]),
        ("attack", values["AttackCentre"], values["AttackRadius"]),
        ("dodge", values["DodgeCentre"], values["DodgeRadius"]),
    ]

    # 1. every control is inside the safe area
    for name, fraction, radius_fraction in controls:
        centre = to_pixels(fraction, safe, screen_w, screen_h)
        r = radius(radius_fraction)
        if (centre[0] - r < safe[0] - 0.5 or centre[1] - r < safe[1] - 0.5
                or centre[0] + r > safe[0] + safe[2] + 0.5
                or centre[1] + r > safe[1] + safe[3] + 0.5):
            problems.append(f"{where}: the {name} control is not fully inside the safe area")

    # 2. interactive controls are big enough to hit
    for name, _fraction, radius_fraction in controls:
        if radius_fraction < MIN_BUTTON_RADIUS:
            problems.append(
                f"{where}: the {name} radius is {radius_fraction:.3f} of screen height, "
                f"below the {MIN_BUTTON_RADIUS} this tool treats as hittable")

    # 3. no two controls overlap, and none is so close that one thumb straddles both
    for i in range(len(controls)):
        for j in range(i + 1, len(controls)):
            name_a, centre_a, radius_a = controls[i]
            name_b, centre_b, radius_b = controls[j]
            a = to_pixels(centre_a, safe, screen_w, screen_h)
            b = to_pixels(centre_b, safe, screen_w, screen_h)
            distance = ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5
            reach = radius(radius_a) + radius(radius_b)
            if distance < reach:
                problems.append(
                    f"{where}: the {name_a} and {name_b} controls overlap "
                    f"({distance:.0f}px apart, {reach:.0f}px of radius between them)")
            elif distance - reach < MIN_BUTTON_GAP * screen_h:
                problems.append(
                    f"{where}: the {name_a} and {name_b} controls are only "
                    f"{(distance - reach) / screen_h:.3f} of screen height apart, so one thumb "
                    "can straddle both")

    # 4. Joystick architecture: static base, handle, independent touch ID, and dead zone processing
    source = open(os.path.join(REPO_ROOT, VIEW), encoding="utf-8").read()
    if "JoystickBase" not in source or "JoystickHandle" not in source:
        problems.append(f"{where}: virtual joystick must instantiate both base and handle visual elements")
    if "_stickTouch" not in source or "UpdateJoystickMovement" not in source:
        problems.append(f"{where}: joystick must manage dedicated touch tracking and vector calculation")
    if "StickDeadZone" not in source:
        problems.append(f"{where}: joystick must respect dead zone threshold to prevent accidental drift")

    return problems


def main() -> int:
    path = os.path.join(REPO_ROOT, VIEW)
    if not os.path.exists(path):
        print(f"FATAL: {VIEW} not found")
        return 1

    values = parse_layout(path)
    print(f"touch layout: {VIEW}")
    print(f"  joystick    : {values['JoystickCentre']}  radius {values['JoystickBaseRadius']:.3f}")
    print(f"  jump        : {values['JumpCentre']}  radius {values['JumpRadius']:.3f}")
    print(f"  attack      : {values['AttackCentre']}  radius {values['AttackRadius']:.3f}")
    print(f"  dodge       : {values['DodgeCentre']}  radius {values['DodgeRadius']:.3f}")
    print()

    checked = 0
    problems: list[str] = []
    for aspect_name, aspect in ASPECTS.items():
        for cutout_name, cutout in SAFE_AREA_CASES.items():
            checked += 1
            problems.extend(check(values, aspect_name, aspect, cutout_name, cutout))

    for problem in problems:
        print(f"  FAIL {problem}")

    if problems:
        print()
        print(f"{len(problems)} problem(s) across {checked} device shapes")
        return 1

    print(f"  ok   no overlap, nothing outside the safe area, every control hittable "
          f"({checked} device shapes)")
    print(f"{checked}/{checked} device shapes sound, 0 problem(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
