#!/usr/bin/env python3
"""Static regression gate for the Varellon logo motion.

Run from any directory:
    python3 tools/verify/brand_motion.py

This checks source/asset contracts only. It does not compile Unity shaders, render a frame, or
replace the on-device visual and performance review.
"""
from __future__ import annotations

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FILES = {
    "menu": "Assets/Aether/Code/Aether.Gameplay/Runtime/Presentation/MenuLogo.cs",
    "intro": "Assets/Aether/Code/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs",
    "menu_shader": "Assets/Aether/Resources/Brand/VarellonLogoSheen.shader",
    "intro_shader": "Assets/Aether/Resources/Brand/VarellonIntroSheen.shader",
}
problems: list[str] = []


def read(key: str) -> str:
    path = os.path.join(ROOT, FILES[key])
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return handle.read()
    except OSError as error:
        problems.append(f"{FILES[key]}: cannot read asset ({error})")
        return ""


def require(condition: bool, message: str) -> None:
    if not condition:
        problems.append(message)


def method_body(source: str, signature: str) -> str:
    """Return a C# method body using balanced braces, not a greedy regex."""
    match = re.search(signature, source)
    if not match:
        problems.append(f"source contract: method / signature not found: {signature}")
        return ""
    opening = source.find("{", match.end())
    if opening < 0:
        problems.append(f"source contract: opening brace missing after {signature}")
        return ""
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    problems.append(f"source contract: unclosed method body: {signature}")
    return ""


menu = read("menu")
intro = read("intro")
menu_shader = read("menu_shader")
intro_shader = read("intro_shader")

# Resource paths must match both shader assets exactly; Resources.Load keeps them in player builds.
require('Resources.Load<Shader>("Brand/VarellonLogoSheen")' in menu,
        "menu logo must load Brand/VarellonLogoSheen")
require('Resources.Load<Shader>("Brand/VarellonIntroSheen")' in intro,
        "studio intro must load Brand/VarellonIntroSheen")
require('Shader "UI/VarellonLogoSheen"' in menu_shader,
        "menu shader name must be UI/VarellonLogoSheen")
require('Shader "Sprites/VarellonIntroSheen"' in intro_shader,
        "intro shader name must be Sprites/VarellonIntroSheen")

# Both sweeps must be driven by shader parameters and clipped to the logo's own alpha coverage.
for name, shader in (("menu", menu_shader), ("intro", intro_shader)):
    for prop in ("_SweepProgress", "_SweepOpacity"):
        require(prop in shader, f"{name} shader is missing {prop}")
    require("tex2D(_MainTex" in shader and ".a" in shader,
            f"{name} shader must use the logo mask alpha")
    require("Blend SrcAlpha OneMinusSrcAlpha" in shader,
            f"{name} shader must use transparent alpha blending")

for prop in ("_Stencil", "_StencilComp", "_StencilOp", "_ColorMask"):
    require(prop in menu_shader, f"menu UI shader is missing Canvas property {prop}")
require("UnityGet2DClipping" in menu_shader and "UNITY_UI_CLIP_RECT" in menu_shader,
        "menu UI shader must preserve RectMask2D / clip-rect behavior")
require("UNITY_UI_ALPHACLIP" in menu_shader,
        "menu UI shader must preserve optional UI alpha clipping")

# Catch accidental CPU texture uploads in per-frame animation methods while allowing one-time
# texture construction in setup methods.
menu_update = method_body(menu, r"\bIEnumerator\s+PresentRoutine\s*\(")
intro_draw = method_body(intro, r"\bvoid\s+DrawSheen\s*\(")
for label, body in (("menu PresentRoutine", menu_update), ("intro DrawSheen", intro_draw)):
    require("SetPixels" not in body and ".Apply(" not in body,
            f"{label} must not rewrite/upload texture pixels per frame")
    require("_SweepProgress" in body and "_SweepOpacity" in body,
            f"{label} must drive the shader's sweep parameters")

# Runtime-owned materials must have a cleanup path. The menu must also retain Reduced Motion.
require("_sweepMaterial" in menu and re.search(r"Destroy\s*\(\s*_sweepMaterial\s*\)", menu) is not None,
        "menu runtime sweep material must be destroyed during cleanup")
require("_sheenMaterial" in intro and re.search(r"Destroy\s*\(\s*_sheenMaterial\s*\)", intro) is not None,
        "intro runtime sheen material must be destroyed during cleanup")
require("ReducedMotion" in menu or "reducedMotion" in menu,
        "menu logo must retain Reduced Motion handling")

# The intro's signature remains brief and the sheen remains restrained.
def number(name: str) -> float | None:
    match = re.search(rf"public const float {re.escape(name)}\s*=\s*([0-9.]+)f?\s*;", intro)
    if not match:
        problems.append(f"intro timing/visual contract: constant {name} not found")
        return None
    return float(match.group(1))


starts = number("SheenStartsAt")
duration = number("SheenDuration")
hold = number("Hold")
exit_time = number("Exit")
handover = number("HandOver")
band = number("BandHalfWidth")
peak = number("HighlightPeak")
if None not in (starts, duration, hold, exit_time, handover):
    total = starts + duration + hold + exit_time + handover
    require(2.80 <= total <= 3.00,
            f"intro total is {total:.2f}s; required range is 2.80–3.00s")
if band is not None:
    require(0.12 <= band <= 0.28,
            f"intro sheen band half-width {band:.3f} is outside the restrained range 0.12–0.28")
if peak is not None:
    require(0.45 <= peak <= 0.78,
            f"intro highlight peak {peak:.3f} is outside the restrained range 0.45–0.78")

if problems:
    print("Varellon brand-motion static gate: FAIL")
    for problem in problems:
        print(f" - {problem}")
    sys.exit(1)

print("Varellon brand-motion static gate: PASS")
print("Checked shader/resource contracts, UI clipping, per-frame texture-upload absence, cleanup,")
print("Reduced Motion retention, and restrained intro timing/shine constants.")
print("Unity shader compilation, rendered appearance, and Android profiling remain separate checks.")
