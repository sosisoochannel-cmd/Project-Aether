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

# The menu logo gets one finite, alpha-clipped UI glint after its entrance; it must not loop or glow.
require('Resources.Load<Shader>(GlintShaderPath)' in menu and
        'private const string GlintShaderPath = "Brand/VarellonLogoSheen";' in menu,
        "menu logo must load the dedicated VarellonLogoSheen shader for its one-time glint")
require('logo._glint.gameObject.SetActive(false);' in menu and
        '_glint.gameObject.SetActive(false);' in menu,
        "menu glint must be hidden at rest and after it finishes")
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
menu_update = method_body(menu, r"\bIEnumerator\s+PresentRoutine\s*\(")
intro_draw = method_body(intro, r"\bvoid\s+DrawSheen\s*\(")
for label, body in (("menu PresentRoutine", menu_update), ("intro DrawSheen", intro_draw)):
    require("SetPixels" not in body and ".Apply(" not in body,
            f"{label} must not rewrite/upload texture pixels per frame")
require("_SweepProgress" in menu_update and "_SweepOpacity" in menu_update,
        "menu logo must animate one glint pass and its opacity envelope")
require("const float sweepDuration = 0.92f;" in menu_update and
        "const float lingerDuration = 0.16f;" in menu_update and
        "const float fadeDuration = 0.34f;" in menu_update,
        "menu glint must use the longer sweep, brief linger, and soft fade")
require("!MenuPreferences.ReducedMotion" in menu_update,
        "menu glint must respect Reduced Motion")
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
require("return wordmark ? EaseOutCubic(linear) : EaseOutSine(linear);" in intro,
        "intro matte must keep pace with the same ease-out as each part's alpha")
require("skipped ? markRevealFrom : 1f" in intro and "skipped ? wordmarkRevealFrom : 1f" in intro,
        "skipping the intro must preserve the current matte instead of snapping it open")

# Runtime-owned materials must have a cleanup path; the one-time glint owns one UI material.
require("_sweepMaterial" not in menu and "BuildSweepMaterial" not in menu,
        "menu logo must not retain the old perpetual sweep-material machinery")
require("BuildGlintMaterial()" in menu and "Destroy(_glintMaterial)" in menu,
        "menu one-time glint material must be created and destroyed safely")
require("_generatedSprite != null) Destroy(_generatedSprite)" in menu and
        "_generatedTexture != null) Destroy(_generatedTexture)" in menu,
        "menu generated sprite and texture must be destroyed during cleanup")
require("_sheenMaterial" in intro and re.search(r"Destroy\s*\(\s*_sheenMaterial\s*\)", intro) is not None,
        "intro runtime sheen material must be destroyed during cleanup")
require("Destroy(_mark.RevealMaterial)" in intro and "Destroy(_wordmark.RevealMaterial)" in intro,
        "per-part intro reveal materials must be destroyed during cleanup")
require("ReducedMotion" in menu or "reducedMotion" in menu,
        "menu logo must retain Reduced Motion handling")
require("logo.BeginPresentation();" in menu and "private void BeginPresentation()" in menu,
        "menu logo entrance motion must start after the generated mark is ready")
require("if (!isActiveAndEnabled || _mark == null || _mark.sprite == null) return;" in menu,
        "menu logo presentation start must respect active/enabled lifecycle state")
require("const float duration = 0.42f;" in menu and "const float rise = 6f;" in menu,
        "menu logo must keep its brief restrained entrance")

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
print("Checked shader/resource contracts, one-time menu glint timing, UI clipping,")
print("per-frame texture-upload absence, cleanup, Reduced Motion, and restrained intro timing.")
print("Unity shader compilation, rendered appearance, and Android profiling remain separate checks.")
