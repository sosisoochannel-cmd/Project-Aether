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
    "reveal_shader": "Assets/Aether/Resources/Brand/VarellonIntroReveal.shader",
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
reveal_shader = read("reveal_shader")

# The menu uses the Varellon mark as a still brand signature. Cinematic motion belongs to
# the dedicated studio intro, not the interactive menu where it can distract from navigation.
require("_glint" not in menu and "GlintShaderPath" not in menu and
        "BuildGlintMaterial" not in menu and "PresentRoutine" not in menu,
        "menu logo must stay static and must not load or animate a glint")
require("private void OnEnable()" in menu and "if (_mark != null && _mark.sprite != null) Apply();" in menu,
        "menu logo must reapply its fixed layout safely when re-enabled")
require('Resources.Load<Shader>("Brand/VarellonIntroSheen")' in intro,
        "studio intro must load Brand/VarellonIntroSheen")
require('Resources.Load<Shader>("Brand/VarellonIntroReveal")' in intro,
        "studio intro must load Brand/VarellonIntroReveal")
require('Shader "Sprites/VarellonIntroSheen"' in intro_shader,
        "intro shader name must be Sprites/VarellonIntroSheen")
require('Shader "Sprites/VarellonIntroReveal"' in reveal_shader,
        "intro reveal shader name must be Sprites/VarellonIntroReveal")

# The remaining intro sheen is clipped to the artwork's own alpha coverage.
for prop in ("_SweepProgress", "_SweepOpacity"):
    require(prop in intro_shader, f"intro shader is missing {prop}")
require("tex2D(_MainTex" in intro_shader and ".a" in intro_shader,
        "intro shader must use the logo mask alpha")
require("Blend SrcAlpha OneMinusSrcAlpha" in intro_shader,
        "intro shader must use transparent alpha blending")

# Catch accidental CPU texture uploads in per-frame animation methods while allowing one-time
# texture construction in setup methods.
intro_draw = method_body(intro, r"\bvoid\s+DrawSheen\s*\(")
require("SetPixels" not in intro_draw and ".Apply(" not in intro_draw,
        "intro DrawSheen must not rewrite/upload texture pixels per frame")
require("_SweepProgress" in intro_draw and "_SweepOpacity" in intro_draw,
        "intro DrawSheen must drive the shader's sweep parameters")

# The intro reveal is a soft diagonal matte over the source alpha, not a screen-space wipe.
for prop in ("_RevealProgress", "_RevealFeather", "_RevealTilt"):
    require(prop in reveal_shader, f"intro reveal shader is missing {prop}")
require("tex2D(_MainTex" in reveal_shader and ".a" in reveal_shader,
        "intro reveal shader must preserve the logo alpha coverage")
require("smoothstep" in reveal_shader and "Blend SrcAlpha OneMinusSrcAlpha" in reveal_shader,
        "intro reveal shader must use a soft edge and transparent blending")
require("_RevealProgress" in intro and "RevealProgress(elapsed, false)" in intro,
        "studio mark must animate the reveal matte")
require("RevealProgress(elapsed, true)" in intro,
        "studio wordmark must animate its reveal matte after the mark")
require("Mathf.Lerp(-20f, 0f, settle)" in intro and
        "Mathf.Lerp(3.6f, 0f, settle)" in intro and
        "Quaternion.Euler(0f, yaw, roll)" in intro,
        "studio emblem must have a restrained 3D yaw/roll settle rather than only a fade")
require("return wordmark ? EaseOutCubic(linear) : EaseOutSine(linear);" in intro,
        "intro matte must keep pace with the same ease-out as each part's alpha")
require("skipped ? markRevealFrom : 1f" in intro and "skipped ? wordmarkRevealFrom : 1f" in intro,
        "skipping the intro must preserve the current matte instead of snapping it open")

# Runtime-owned materials must have a cleanup path; the one-time glint owns one UI material.
require("_sweepMaterial" not in menu and "BuildSweepMaterial" not in menu and
        "_glintMaterial" not in menu and "BuildGlintMaterial" not in menu,
        "menu logo must not retain sweep or glint material machinery")
require("_generatedSprite != null) Destroy(_generatedSprite)" in menu and
        "_generatedTexture != null) Destroy(_generatedTexture)" in menu,
        "menu generated sprite and texture must be destroyed during cleanup")
require("_sheenMaterial" in intro and re.search(r"Destroy\s*\(\s*_sheenMaterial\s*\)", intro) is not None,
        "intro runtime sheen material must be destroyed during cleanup")
require("Destroy(_mark.RevealMaterial)" in intro and "Destroy(_wordmark.RevealMaterial)" in intro,
        "per-part intro reveal materials must be destroyed during cleanup")
require("IEnumerator" not in menu and "Coroutine" not in menu and "localScale" not in menu,
        "menu logo must not introduce entrance animation or scale effects")

# The intro's signature remains brief and its sheen is intentionally subtle.
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
    require(0.12 <= band <= 0.22,
            f"intro sheen band half-width {band:.3f} is outside the subtle range 0.12–0.22")
if peak is not None:
    require(0.35 <= peak <= 0.55,
            f"intro highlight peak {peak:.3f} is outside the subtle range 0.35–0.55")

if problems:
    print("Varellon brand-motion static gate: FAIL")
    for problem in problems:
        print(f" - {problem}")
    sys.exit(1)

print("Varellon brand-motion static gate: PASS")
print("Checked static menu-logo contract, intro shader/resource contracts, UI clipping,")
print("per-frame texture-upload absence, cleanup, and restrained studio-intro timing.")
print("Unity shader compilation, rendered appearance, and Android profiling remain separate checks.")
