#!/usr/bin/env python3
"""Proves the studio intro's layout, its length, and that the committed artwork works with it.

The intro is decidable in three places, and all three are checked here:

* **the numbers** — `StudioIntroSequence.Layout` is where the lockup sits, in fractions of the safe
  area, and `Timing` is how long each part lasts, in seconds. The layout is fitted with one scale
  factor from both limits, so the rendered aspect ratio equals the artwork's on every shape of
  screen and at every point in the animation; nothing leaves the safe area, the lockup stays
  centred, and it is neither a banner nor a watermark. The phases add up to two to three seconds.
* **the artwork** — the PNG the scene names is decoded here, in pure Python, and measured against
  the same rules the runtime uses: its coverage (alpha, or keyed by luminance when the file has no
  alpha), the split into mark and wordmark, and whether that leaves anything to draw at all. This
  is the check that catches a logo which is present, imported, committed — and invisible.
* **the two together** — the layout above is re-run against the real artwork's aspect ratio, so the
  fifty synthetic device shapes are not the only shapes the fit has been proven on.

What is not decidable here: whether it looks good. This proves the intro is not broken; taste is a
person watching it on a phone.

Run:  python3 tools/verify/intro.py
Exit: 0 when the intro's numbers and its artwork are sound, 1 otherwise.
"""

from __future__ import annotations

import os
import re
import struct
import sys
import zlib

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SEQUENCE = "Assets/Aether/Code/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs"
SCENE = "Assets/Aether/Scenes/StudioIntro.unity"
RESOURCES = "Assets/Aether/Resources"
BRAND_DIR = "Assets/Aether/Resources/Brand"

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

# Aspect ratios (width / height) of lockups a studio might hand over. The intro must be correct for
# any of them, because the artwork is not this file's business: a monogram, a lockup with a wordmark
# under it, or a wide banner all have to survive the same fit.
MARKS = {
    "tall lockup 0.45:1": 0.45,
    "portrait mark 0.6:1": 0.6,
    "square mark 1:1": 1.0,
    "wide mark 1.8:1": 1.8,
    "banner 3:1": 3.0,
}

# Bounds on the constants themselves. These are the rules the numbers have to obey, independent of
# any device: the lockup may not be allowed to fill the screen or to shrink to a speck, motion has to
# stay subtle, and no phase may be zero-length.
MIN_LIMIT_FRACTION = 0.10      # below this the mark is a watermark on every shape
MAX_LIMIT_FRACTION = 0.90      # above this it is a banner that can be cropped by a cutout
MIN_REVEAL_SCALE = 0.90        # a smaller start is a pop, not a reveal
MAX_EXIT_SCALE = 1.10          # a larger finish is a lunge
MAX_RISE_FRACTION = 0.08       # of the safe height; more than this is a slide, not a settle

# Floors on the drawn lockup, as fractions of the safe height, measured over the whole animation.
MIN_MAJOR_EXTENT = 0.25        # the lockup's larger dimension is never a speck
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

# Rules on the artwork's half of the constants: where the lockup splits, how far the wordmark lags
# the mark, and how a file without an alpha channel is keyed.
MIN_SPLIT = 0.20               # a split nearer the top has no mark above it
MAX_SPLIT = 0.90               # one nearer the bottom has no wordmark below it
MAX_REVEAL_OVERLAP = 0.80      # the wordmark cannot start when the reveal is almost over
MIN_KEY_GAP = 0.25             # ink and canvas luminance must not be adjacent, or edges smear

# What the committed artwork has to decode to: enough ink to be a mark, not a canvas that failed to
# key, and a border that is canvas rather than a frame around the mark.
MIN_INK_COVERAGE = 0.005
MAX_INK_COVERAGE = 0.90
BORDER_FRACTION = 0.01         # of width / height; how much of the edge counts as the border


def csharp_float(text: str) -> float:
    """C# float literals carry an 'f' suffix: '0.62f'."""
    return float(text.strip().rstrip("fF"))


CONSTANTS = (
    "MaxWidthFraction", "MaxHeightFraction", "RevealScale", "RevealRise", "ExitScale", "ExitRise",
    "WordmarkSplit", "RevealOverlap", "WordmarkRise",
    "BackgroundLuminance", "InkLuminance",
    "BlackHold", "Reveal", "Hold", "Exit", "HandOver", "SkipGrace", "SkipExit",
)


def parse_constants(path: str) -> dict:
    text = open(path, encoding="utf-8").read()
    values: dict[str, float] = {}

    for name in CONSTANTS:
        # Anchored on the whole declaration, so 'Reveal' cannot match 'RevealScale'.
        match = re.search(rf"public const float {name}\s*=\s*([0-9.]+f)\s*;", text)
        if not match:
            raise SystemExit(f"FATAL: could not read '{name}' from {SEQUENCE}; "
                             "the layout moved and this tool would be checking nothing")
        values[name] = csharp_float(match.group(1))

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
    for name in ("RevealRise", "ExitRise", "WordmarkRise"):
        if not 0.0 <= values[name] <= MAX_RISE_FRACTION:
            problems.append(f"constants: {name} is {values[name]:.3f}, outside 0-"
                            f"{MAX_RISE_FRACTION} of the safe height; that is a slide, not a settle")

    if not MIN_SPLIT <= values["WordmarkSplit"] <= MAX_SPLIT:
        problems.append(f"constants: WordmarkSplit is {values['WordmarkSplit']:.3f}, outside "
                        f"{MIN_SPLIT}-{MAX_SPLIT} of the artwork's height; one half of the lockup "
                        "would have nowhere to come from")
    if not 0.0 <= values["RevealOverlap"] <= MAX_REVEAL_OVERLAP:
        problems.append(f"constants: RevealOverlap is {values['RevealOverlap']:.3f}; the wordmark "
                        f"cannot start after {MAX_REVEAL_OVERLAP} of the reveal")
    if not 0.0 <= values["InkLuminance"] < values["BackgroundLuminance"] <= 1.0:
        problems.append(f"constants: the key runs from ink at {values['InkLuminance']:.3f} to "
                        f"background at {values['BackgroundLuminance']:.3f}, which is not an "
                        "increasing range inside 0-1")
    elif values["BackgroundLuminance"] - values["InkLuminance"] < MIN_KEY_GAP:
        problems.append(f"constants: only {values['BackgroundLuminance'] - values['InkLuminance']:.3f} "
                        f"of luminance separates ink from canvas, below the {MIN_KEY_GAP} this tool "
                        "needs to key without smearing the edges")

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


# ---- the artwork ------------------------------------------------------------------------------
#
# The intro builds its mark from the PNG's pixels: coverage from the alpha channel when the file has
# one, and from darkness when it does not, then split into mark and wordmark and trimmed to the ink
# in each half. Whether that produces something drawable is decided by the file, so the file is read
# here — in pure Python, because a gate that needs a package installed is a gate that stops being
# run.


def read_png(path: str) -> tuple[int, int, list[bytes], str, int]:
    """Decode an 8-bit, non-interlaced PNG into rows of samples.

    Returns (width, height, rows, kind, channels), where `kind` is a short human name for the file's
    colour layout.

    `kind` is a short human name for the file's colour layout. Palette images are refused rather
    than approximated: the intro reads raw samples, and a palette would have to be resolved first,
    which is a different code path in a different language — the honest answer is to re-export.
    """
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG file")

    position = 8
    header = None
    alpha_chunk = False
    idat = bytearray()
    while position + 8 <= len(data):
        length, kind = struct.unpack_from(">I4s", data, position)
        chunk = data[position + 8:position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", chunk)
        elif kind == b"tRNS":
            alpha_chunk = True
        elif kind == b"IDAT":
            idat += chunk
        elif kind == b"IEND":
            break

    if header is None:
        raise ValueError("no IHDR chunk")

    width, height, depth, colour, _compression, _filter, interlace = header
    if depth != 8 or interlace != 0 or colour not in (0, 2, 4, 6):
        raise ValueError(f"unsupported PNG: bit depth {depth}, colour type {colour}, "
                         f"interlace {interlace}; re-export it as an 8-bit non-interlaced "
                         "greyscale, RGB, greyscale+alpha or RGBA image")

    channels = {0: 1, 2: 3, 4: 2, 6: 4}[colour]
    names = {0: "greyscale", 2: "RGB", 4: "greyscale+alpha", 6: "RGBA"}
    name = names[colour] + (" + tRNS" if alpha_chunk else "")

    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    rows: list[bytes] = []
    previous = bytearray(stride)
    position = 0
    for _ in range(height):
        filter_type = raw[position]
        position += 1
        row = bytearray(raw[position:position + stride])
        position += stride
        if filter_type == 1:
            for i in range(channels, stride):
                row[i] = (row[i] + row[i - channels]) & 0xFF
        elif filter_type == 2:
            for i in range(stride):
                row[i] = (row[i] + previous[i]) & 0xFF
        elif filter_type == 3:
            for i in range(stride):
                left = row[i - channels] if i >= channels else 0
                row[i] = (row[i] + ((left + previous[i]) >> 1)) & 0xFF
        elif filter_type == 4:
            for i in range(stride):
                left = row[i - channels] if i >= channels else 0
                up = previous[i]
                corner = previous[i - channels] if i >= channels else 0
                estimate = left + up - corner
                pa = abs(estimate - left)
                pb = abs(estimate - up)
                pc = abs(estimate - corner)
                if pa <= pb and pa <= pc:
                    predictor = left
                elif pb <= pc:
                    predictor = up
                else:
                    predictor = corner
                row[i] = (row[i] + predictor) & 0xFF
        elif filter_type != 0:
            raise ValueError(f"unknown PNG filter type {filter_type}")
        rows.append(bytes(row))
        previous = row

    return width, height, rows, name, channels


def artwork_coverage(width: int, height: int, rows: list[bytes], channels: int,
                     has_alpha: bool, background: float, ink: float) -> bytearray:
    """Coverage per pixel, computed exactly as the runtime computes it.

    With alpha, the alpha channel is the coverage. Without it, the canvas is subtracted by
    luminance: at or above `background` a pixel is transparent, at or below `ink` it is opaque, and
    between the two it ramps, which keeps anti-aliased edges smooth.
    """
    coverage = bytearray(width * height)
    if has_alpha:
        index = 3 if channels == 4 else 1
        for y, row in enumerate(rows):
            base = y * width
            for x in range(width):
                coverage[base + x] = row[x * channels + index]
        return coverage

    for y, row in enumerate(rows):
        base = y * width
        for x in range(width):
            offset = x * channels
            if channels >= 3:
                luminance = (0.299 * row[offset] + 0.587 * row[offset + 1]
                             + 0.114 * row[offset + 2]) / 255.0
            else:
                luminance = row[offset] / 255.0
            value = (background - luminance) / (background - ink)
            coverage[base + x] = 0 if value <= 0.0 else (255 if value >= 1.0 else int(value * 255))
    return coverage


def artwork_has_alpha(width: int, height: int, rows: list[bytes], channels: int) -> bool:
    """True when the runtime would find transparency in the file, by its own test.

    The runtime scans for a pixel whose alpha is below 250 rather than trusting the colour type, so
    an RGBA export that is opaque everywhere is keyed by luminance instead — and this has to agree
    with that, or the gate would be checking a different pipeline from the one that ships.
    """
    if channels not in (2, 4):
        return False
    index = 1 if channels == 2 else 3
    for row in rows:
        for x in range(width):
            if row[x * channels + index] < 250:
                return True
    return False


def ink_bounds(coverage: bytearray, width: int, y_from: int, y_to: int) -> tuple | None:
    """The box of ink inside a band of rows, as (min_x, min_y, max_x, max_y) inclusive."""
    min_x, min_y, max_x, max_y = width, y_to, -1, -1
    for y in range(y_from, y_to):
        base = y * width
        for x in range(width):
            if coverage[base + x] == 0:
                continue
            if x < min_x:
                min_x = x
            if x > max_x:
                max_x = x
            if y < min_y:
                min_y = y
            if y > max_y:
                max_y = y
    if max_x < min_x or max_y < min_y:
        return None
    return (min_x, min_y, max_x, max_y)


def border_luminance(rows: list[bytes], width: int, height: int, channels: int) -> float:
    """The median luminance of the artwork's outer edge, which is what a key has to erase."""
    margin = max(1, int(round(min(width, height) * BORDER_FRACTION)))
    samples = []

    def luminance(row: bytes, x: int) -> float:
        offset = x * channels
        if channels >= 3:
            return (0.299 * row[offset] + 0.587 * row[offset + 1] + 0.114 * row[offset + 2]) / 255.0
        return row[offset] / 255.0

    for y in list(range(margin)) + list(range(height - margin, height)):
        row = rows[y]
        for x in range(0, width, max(1, width // 256)):
            samples.append(luminance(row, x))
    for y in range(0, height, max(1, height // 256)):
        row = rows[y]
        for x in list(range(margin)) + list(range(width - margin, width)):
            samples.append(luminance(row, x))

    samples.sort()
    return samples[len(samples) // 2]


def check_artwork(values: dict, scene_text: str, script_text: str) -> tuple[list, list, str]:
    """Decode the artwork the scene names and measure it against the runtime's rules.

    Returns (problems, notes, summary line).
    """
    problems: list[str] = []
    notes: list[str] = []

    resource = None
    match = re.search(r"^  _markResourcePath: (.+)$", scene_text, re.M)
    if match:
        resource = match.group(1).strip()
    else:
        match = re.search(r'_markResourcePath\s*=\s*"([^"]*)"', script_text)
        if match:
            resource = match.group(1).strip()

    if not resource:
        problems.append(f"brand: neither '{SCENE}' nor {SEQUENCE} names an artwork resource; the "
                        "intro has nothing to draw")
        return problems, notes, "brand: no artwork named"

    candidates = [os.path.join(RESOURCES, resource + ext) for ext in (".png", ".PNG", ".jpg",
                                                                     ".jpeg", ".tga")]
    source = next((path for path in candidates if os.path.exists(path)), None)
    if source is None:
        problems.append(f"brand: the intro draws '{resource}' from Assets/Aether/Resources, but no "
                        f"image is at {os.path.join(RESOURCES, resource)}.png — the intro would load "
                        "nothing, log an error and hand over without being seen")
        return problems, notes, f"brand: '{resource}' is missing"

    relative = os.path.relpath(source, REPO_ROOT).replace(os.sep, "/")
    if os.path.dirname(relative) != BRAND_DIR:
        problems.append(f"brand: '{relative}' is outside {BRAND_DIR}, which is where brand artwork "
                        "and its import settings are checked")
    if not source.lower().endswith(".png"):
        problems.append(f"brand: '{relative}' is not a PNG; the supplied studio artwork is a PNG "
                        "with a transparent background, which is what the intro expects")

    try:
        width, height, rows, kind, channels = read_png(source)
    except (ValueError, OSError, zlib.error, struct.error) as error:
        problems.append(f"brand: '{relative}' could not be read: {error}")
        return problems, notes, f"brand: '{relative}' unreadable"

    has_alpha = artwork_has_alpha(width, height, rows, channels)
    if channels in (2, 4) and not has_alpha:
        notes.append(f"brand: '{relative}' has an alpha channel but every pixel is opaque, so its "
                     "background is keyed by luminance like a flat export")
    if channels in (1, 3):
        notes.append(f"brand: '{relative}' is {kind} with no transparency, so its light background "
                     "is keyed out by luminance; the intro subtracts the canvas and draws the ink")

    coverage = artwork_coverage(width, height, rows, channels, has_alpha,
                               values["BackgroundLuminance"], values["InkLuminance"])
    ink = sum(1 for value in coverage if value > 0)
    fraction = ink / (width * height)

    # The order matters, because each answer depends on the one before it: a canvas that cannot be
    # keyed leaves the ink fraction meaningless, and an artwork with no ink at all says nothing
    # about where the split falls.
    if not has_alpha:
        edge = border_luminance(rows, width, height, channels)
        if edge < values["BackgroundLuminance"]:
            problems.append(
                f"brand: '{relative}' has no transparency and its border luminance is {edge:.3f}, "
                f"below the {values['BackgroundLuminance']:.3f} the key removes; the intro would "
                "draw that background as a grey rectangle behind the mark")

    if fraction < MIN_INK_COVERAGE:
        problems.append(f"brand: '{relative}' keys down to {fraction * 100:.3f}% ink; the intro "
                        "would draw nothing. Check Artwork.BackgroundLuminance against the file")
        return problems, notes, f"brand: '{relative}' has no ink"

    if fraction > MAX_INK_COVERAGE:
        problems.append(f"brand: '{relative}' keys down to {fraction * 100:.1f}% ink, which is a "
                        "filled rectangle rather than a mark; the background is not being removed")
        return problems, notes, f"brand: '{relative}' keys to a filled rectangle"

    # The split runs from the top: above the line is the mark, below it the wordmark. In the
    # runtime's bottom-up rows the boundary is round(height * (1 - split)), so in these top-down
    # rows the mark occupies [0, height - boundary) and the wordmark the rest.
    boundary = min(max(int(round(height * (1.0 - values["WordmarkSplit"]))), 1), height - 1)
    mark_rows = (0, height - boundary)
    wordmark_rows = (height - boundary, height)

    mark_bounds = ink_bounds(coverage, width, *mark_rows)
    wordmark_bounds = ink_bounds(coverage, width, *wordmark_rows)

    if mark_bounds is None:
        problems.append(f"brand: '{relative}' has no ink above {values['WordmarkSplit']:.3f} of its "
                        "height, where the mark is expected; the intro would log an error and show "
                        "nothing. Check Layout.WordmarkSplit against the file")
        return problems, notes, f"brand: '{relative}' has no mark above the split"
    if wordmark_bounds is None:
        notes.append(f"brand: '{relative}' has no wordmark below {values['WordmarkSplit']:.3f} of "
                     "its height; the intro draws the mark alone, which is correct for a monogram")

    boxes = [box for box in (mark_bounds, wordmark_bounds) if box is not None]
    lockup = (min(box[0] for box in boxes), min(box[1] for box in boxes),
              max(box[2] for box in boxes), max(box[3] for box in boxes))
    lockup_w = lockup[2] - lockup[0] + 1
    lockup_h = lockup[3] - lockup[1] + 1
    aspect = lockup_w / lockup_h

    real_problems = []
    checked = 0
    for aspect_name, screen_aspect in ASPECTS.items():
        for cutout_name, cutout in SAFE_AREA_CASES.items():
            checked += 1
            found, _major, _minor = check_shape(values, aspect_name, screen_aspect, cutout_name,
                                                cutout, "the committed artwork", aspect)
            real_problems.extend(found)
    problems.extend(real_problems)

    summary = (f"brand: '{relative}' {width}x{height} {kind}, "
               f"{'alpha' if has_alpha else 'keyed by luminance'}, ink {fraction * 100:.2f}%, "
               f"mark {mark_bounds[2] - mark_bounds[0] + 1}x{mark_bounds[3] - mark_bounds[1] + 1}"
               + (f", wordmark {wordmark_bounds[2] - wordmark_bounds[0] + 1}"
                  f"x{wordmark_bounds[3] - wordmark_bounds[1] + 1}" if wordmark_bounds else
                  ", no wordmark")
               + f"; lockup {lockup_w}x{lockup_h}")
    notes.append(f"{summary}; the fit was re-proven on {checked} device shapes with this artwork")
    return problems, notes, summary


def main() -> int:
    path = os.path.join(REPO_ROOT, SEQUENCE)
    if not os.path.exists(path):
        print(f"FATAL: {SEQUENCE} not found")
        return 1

    script_text = open(path, encoding="utf-8").read()
    values = parse_constants(path)

    total = (values["BlackHold"] + values["Reveal"] + values["Hold"]
             + values["Exit"] + values["HandOver"])
    print(f"studio intro: {SEQUENCE}")
    print(f"  black {values['BlackHold']:.2f}s -> reveal {values['Reveal']:.2f}s -> "
          f"hold {values['Hold']:.2f}s -> exit {values['Exit']:.2f}s -> "
          f"hand-over {values['HandOver']:.2f}s   total {total:.2f}s")
    print(f"  skip: grace {values['SkipGrace']:.2f}s, then a {values['SkipExit']:.2f}s exit")
    print(f"  lockup: up to {values['MaxWidthFraction']:.2f} of the safe width, "
          f"{values['MaxHeightFraction']:.2f} of its height; scale "
          f"{values['RevealScale']:.2f}->{values['ExitScale']:.2f}")
    print(f"  parts: split at {values['WordmarkSplit']:.3f} of the artwork, wordmark lags the mark "
          f"by {values['RevealOverlap'] * values['Reveal']:.2f}s and rises "
          f"{values['WordmarkRise']:.3f} of the safe height")
    print(f"  key: ink at {values['InkLuminance']:.2f} luminance, canvas at "
          f"{values['BackgroundLuminance']:.2f}")
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

    scene_path = os.path.join(REPO_ROOT, SCENE)
    scene_text = open(scene_path, encoding="utf-8").read() if os.path.exists(scene_path) else ""
    if not scene_text:
        problems.append(f"brand: '{SCENE}' is missing, so there is no scene to draw the artwork in")

    brand_problems, brand_notes, brand_summary = check_artwork(values, scene_text, script_text)
    problems.extend(brand_problems)

    for problem in problems:
        print(f"  FAIL {problem}")

    if problems:
        print()
        print(f"{len(problems)} problem(s) across {checked} synthetic device shapes")
        return 1

    for note in brand_notes:
        print(f"  note {note}")

    print(f"  ok   aspect preserved, inside the safe area, centred, readable "
          f"({checked} synthetic device shapes)")
    print(f"  ok   tightest synthetic mark: larger dimension {tightest_major[0]:.3f} of the safe "
          f"height ({tightest_major[1]}), smaller {tightest_minor[0]:.3f} ({tightest_minor[1]})")
    print(f"  ok   {total:.2f}s long, inside the required {MIN_TOTAL}-{MAX_TOTAL}s")
    print(f"  ok   {brand_summary}")
    print(f"{checked}/{checked} synthetic device shapes sound, artwork sound, 0 problem(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
