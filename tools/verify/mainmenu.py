#!/usr/bin/env python3
"""Proves the main menu is wired, complete and laid out where nobody can compile it.

The menu is built in code: there is no prefab to look at and no scene hierarchy to inspect, which
normally means the things that go wrong are only visible on a device. They are decidable here
instead, because every one of them is a number or a name in a file:

* the flow the app takes — the intro, then Boot, then the menu — is a build order and two GUIDs;
* a screen that no row can reach, or a row that no screen answers, is a name that appears on one
  side of a switch and not the other;
* a label that does not exist in the string table is a key that is missing from a dictionary;
* a menu that does not fit the screen it is designed for is arithmetic on the theme's own numbers;
* a row too small to press is a pitch shorter than the 48dp floor;
* a per-frame search or a second scene loader is a call in the wrong file.

What this tool cannot do is decide whether any of it *looks* good, or whether a tap feels
immediate. That needs the editor and a phone, and the report says so rather than implying
otherwise.

Run:  python3 tools/verify/mainmenu.py
Exit: 0 when the menu is sound, 1 otherwise.
"""

from __future__ import annotations

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

GAMEPLAY = "Assets/Aether/Code/Aether.Gameplay/Runtime"
MENUS = f"{GAMEPLAY}/Menus"
SCREENS = f"{MENUS}/Screens"

# What the player reads while they are playing, held to the menu's own rules: real string keys,
# no per-frame searches, and no second way to load a scene or quit the game.
INTERFACE = f"{GAMEPLAY}/Interface"
PRESENTATION = f"{GAMEPLAY}/Presentation"

THEME = f"{MENUS}/MenuTheme.cs"
STRINGS = f"{MENUS}/MenuStrings.cs"
SCREEN_REGISTRY = f"{MENUS}/MenuScreens.cs"
MAIN_SCREEN = f"{SCREENS}/MainMenuScreen.cs"
TITLE_PANEL = f"{MENUS}/Panels/MenuTitlePanel.cs"
ROOT = f"{MENUS}/MenuRoot.cs"
SETTINGS_PANEL = f"{MENUS}/Panels/MenuSettingsPanel.cs"
CATEGORY_PANEL = f"{MENUS}/Panels/MenuCategoryPanel.cs"
CREDITS_PANEL = f"{MENUS}/Panels/MenuCreditsPanel.cs"
CATALOG = "Assets/Aether/Code/Aether.Core/Runtime/Settings/SettingsCatalog.cs"
BUILD_SETTINGS = "ProjectSettings/EditorBuildSettings.asset"
MAIN_SCENE = "Assets/Aether/Scenes/MainMenu.unity"
BOOT_SCENE = "Assets/Aether/Scenes/Boot.unity"
INTRO_SCENE = "Assets/Aether/Scenes/StudioIntro.unity"

# The menu's canvas matches height at 1080 reference units, and a phone that is 1080 units tall is
# about 360dp tall in landscape (a 1080p panel at xxhdpi, or a 720p panel at xhdpi — the two cases
# that cover almost every Android phone). One unit is therefore a third of a dp, and this is the
# conversion the touch-target rule below uses.
UNITS_PER_DP = 3.0

# Landscape shapes the brief names, plus the narrowest one the interface has to survive.
ASPECTS = [
    ("16:9", 16 / 9),
    ("18:9", 18 / 9),
    ("19.5:9", 19.5 / 9),
    ("20:9", 20 / 9),
]

REFERENCE_HEIGHT = 1080.0


def read(rel: str) -> str:
    path = os.path.join(REPO_ROOT, rel)
    if not os.path.exists(path):
        raise SystemExit(f"FATAL: {str(rel)[:120]!r} is not a file in this repository; "
                         "this tool would be checking nothing")
    return open(path, encoding="utf-8").read()


def csharp_float(text: str) -> float:
    return float(text.strip().rstrip("fF"))


# ---- reading the theme -------------------------------------------------------------------


def metrics() -> dict:
    """Every `public const float X = Nf;` in MenuTheme.Metrics, by name."""
    text = read(THEME)
    block = text.split("public static class Metrics", 1)
    if len(block) != 2:
        raise SystemExit("FATAL: MenuTheme.Metrics is gone; every number below would be unread")
    found = dict(re.findall(r"public const float ([A-Za-z]\w*) = ([0-9.]+)f;", block[1]))
    if len(found) < 40:
        raise SystemExit(f"FATAL: only {len(found)} metrics read from MenuTheme; the format moved")
    return {name: float(value) for name, value in found.items()}


def palette() -> dict:
    text = read(THEME)
    block = text.split("public static class Palette", 1)[1].split("public static class Metrics", 1)[0]
    colours = {}
    for name, body in re.findall(r"public static readonly Color (\w+) = new Color\(([^)]*)\);", block):
        channels = [csharp_float(v) for v in body.split(",")]
        colours[name] = channels
    return colours


def number(text: str, pattern: str, what: str) -> float:
    match = re.search(pattern, text)
    if not match:
        raise SystemExit(f"FATAL: could not read {what}; the code moved and this check is blind")
    return csharp_float(match.group(1))


# ---- reading calls -----------------------------------------------------------------------


def call_arguments(text: str, name: str) -> list:
    """Every argument list of `name(...)` in a source file, split at top level.

    The name may be called through a receiver — `panel.AddEntry(...)` — so any number of
    `something.` segments are allowed in front of it. A name with dots in it is matched whole,
    which is what the static calls look like.
    """
    results = []
    pattern = r"(?:[A-Za-z_]\w*\s*\.\s*)*" + re.escape(name).replace(r"\.", r"\s*\.\s*")
    for match in re.finditer(pattern + r"\s*\(", text):
        start = match.end() - 1
        depth, index = 0, start
        while index < len(text):
            if text[index] == "(":
                depth += 1
            elif text[index] == ")":
                depth -= 1
                if depth == 0:
                    break
            index += 1
        if index >= len(text):
            continue
        body = text[start + 1:index]
        results.append(split_arguments(body))
    return results


def split_arguments(body: str) -> list:
    parts, depth, angle, quote, current = [], 0, 0, "", []
    for char in body:
        if quote:
            current.append(char)
            if char == quote:
                quote = ""
            continue
        if char in "\"'":
            quote = char
            current.append(char)
            continue
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == "<":
            angle += 1
        elif char == ">":
            angle = max(0, angle - 1)
        if char == "," and depth == 0 and angle == 0:
            parts.append("".join(current).strip())
            current = []
            continue
        current.append(char)
    if current or parts:
        parts.append("".join(current).strip())
    return [p for p in parts if p]


def body_of(text: str, signature: str) -> str:
    """The brace-matched body of the first method whose header contains `signature`."""
    index = text.find(signature)
    if index < 0:
        return ""
    brace = text.find("{", index)
    if brace < 0:
        return ""
    depth = 0
    for cursor in range(brace, len(text)):
        if text[cursor] == "{":
            depth += 1
        elif text[cursor] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:cursor + 1]
    return ""


# ---- the checks --------------------------------------------------------------------------


class Gate:
    def __init__(self) -> None:
        self.checked = 0
        self.problems: list[str] = []
        self.notes: list[str] = []

    def check(self, condition: bool, message: str) -> bool:
        self.checked += 1
        if not condition:
            self.problems.append(message)
        return bool(condition)


def check_flow(gate: Gate) -> None:
    """The app's route into the menu, and that the menu scene can actually run."""
    settings = read(BUILD_SETTINGS)
    entries = re.findall(r"- enabled: (\d)\n\s+path: (\S+)\n\s+guid: ([0-9a-f]{32})", settings)
    enabled = [(path, guid) for flag, path, guid in entries if flag == "1"]

    # No early return: a build order that lost a scene has to be reported for every rule it
    # affects, not only for the first one that notices.
    gate.check(len(enabled) >= 3, f"the build order lists {len(enabled)} enabled scene(s), "
                                  "not the three the flow needs")

    order = [path for path, _guid in enabled]
    if not order:
        return

    gate.check(order[0] == INTRO_SCENE, f"the studio intro is not the first scene ('{order[0]}' is)")
    gate.check(len(order) > 1 and order[1] == BOOT_SCENE,
               f"boot is not the second scene ('{order[1] if len(order) > 1 else 'nothing'}' is)")
    gate.check(MAIN_SCENE in order,
               f"'{MAIN_SCENE}' is not in the build order, so the menu can never be loaded")

    if MAIN_SCENE not in order:
        # The rest of this check reads the menu scene. With the scene gone there is nothing to
        # read, and continuing would produce failures that are not about the menu.
        return

    # Every listed scene has to resolve: a guid that does not match its .meta loads the wrong scene
    # or none at all, and Unity says nothing about it.
    for path, guid in enabled:
        asset = os.path.join(REPO_ROOT, path)
        if not os.path.exists(asset):
            gate.check(False, f"'{path}' is in the build order but does not exist")
            continue
        meta = asset + ".meta"
        declared = re.search(r"guid: ([0-9a-f]{32})", read(meta)) if os.path.exists(meta) else None
        gate.check(declared is not None and declared.group(1) == guid,
                   f"'{path}' is listed with guid {guid}, but its .meta declares "
                   f"{declared.group(1) if declared else 'nothing'}")

    scene = read(MAIN_SCENE)
    root_guid = re.search(r"guid: ([0-9a-f]{32})",
                          read(ROOT.replace(".cs", ".cs.meta"))).group(1)
    gate.check(f"guid: {root_guid}" in scene,
               f"'{MAIN_SCENE}' contains no {ROOT}; the scene would load and show nothing")

    # Exactly one MonoBehaviour, and it is the menu root: the canvas, the event system, the
    # screens and the audio are all built by it, so a second component in the scene would be a
    # second menu.
    scripts = re.findall(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", scene)
    gate.check(scripts == [root_guid],
               f"'{MAIN_SCENE}' has {len(scripts)} authored script component(s); the menu scene "
               "should contain the menu root and nothing else")

    # The first frame the player sees after the intro is this scene's camera colour, so it is the
    # menu's own ground colour rather than skybox or black.
    ground = palette()["Ground"]
    camera = re.search(r"--- !u!20 &(\d+)\n(.*?)(?=^--- )", scene, re.M | re.S)
    if gate.check(camera is not None, f"'{MAIN_SCENE}' has no camera"):
        document = camera.group(2)
        gate.check("m_ClearFlags: 2" in document,
                   f"'{MAIN_SCENE}'s camera does not clear to a solid colour, so the menu would be "
                   "composited over the skybox")
        found = re.search(r"m_BackGroundColor: \{r: ([-0-9.]+), g: ([-0-9.]+), b: ([-0-9.]+)", document)
        if gate.check(found is not None, f"'{MAIN_SCENE}'s camera has no background colour"):
            channels = [float(v) for v in found.groups()]
            close = all(abs(a - b) <= 0.01 for a, b in zip(channels, ground[:3]))
            gate.check(close, f"'{MAIN_SCENE}'s camera background {channels} is not the menu's "
                              f"ground colour {ground[:3]}, so the load in from the intro would "
                              "flash a different shade")
    gate.check("!u!81 &" in scene, f"'{MAIN_SCENE}' has no AudioListener, so the menu is silent")


def builds(text: str, screen: str) -> bool:
    """Whether a factory body builds one screen type, however it spells the call.

    `MenuScreen.Create<T>` is generic on the screen, and C# infers nothing from a return type, so
    every call has to name its screen explicitly - `MenuScreen.Create<ChaptersScreen>(...)`. The
    older spelling this gate was written against was `ChaptersScreen.Create(...)`, the inherited
    static, and both are accepted here rather than one being treated as a rule.
    """
    return f"{screen}.Create(" in text or f"Create<{screen}>(" in text


def check_screens(gate: Gate) -> None:
    """Every screen exists, is reachable, and every row leads somewhere."""
    registry = read(SCREEN_REGISTRY)
    members = re.findall(r"^\s{8}(\w+) = \d+,", registry, re.M)
    gate.check(len(members) >= 7, f"only {len(members)} screen id(s) declared")

    create = body_of(registry, "public static MenuScreen Create(")
    gate.check("default:" in create,
               "MenuScreens.Create has no default branch, so an id added later would return null")

    title = body_of(registry, "public static string TitleKey(")
    body = body_of(registry, "public static string BodyKey(")
    table = read(STRINGS)

    for member in members:
        if member == "MainMenu":
            # The main menu is the default branch: it is what an unknown id falls back to.
            gate.check(builds(create, "MainMenuScreen"),
                       "MenuScreens.Create never builds the main menu screen")
            continue

        gate.check(f"MenuScreenId.{member}" in create,
                   f"no case in MenuScreens.Create builds the {member} screen, so an entry point "
                   "for it would open nothing")
        gate.check(f"MenuScreenId.{member}" in title,
                   f"MenuScreens.TitleKey has no title for {member}, so its header would say "
                   "'menu.title'")
        # A screen with a system behind it has nothing to explain. The ones that do are the
        # chapter list (the regions that are not in the build) and the three placeholders.
        if member not in ("Settings", "Credits", "Chapters"):
            gate.check(f"MenuScreenId.{member}" in body,
                       f"MenuScreens.BodyKey says nothing about {member}, so that screen would "
                       "have no sentence to show")

    # The three ids with no system behind them have to be the honest screen, not a blank one.
    gate.check(builds(create, "PlaceholderScreen"),
               "the screens with no system behind them are not mapped to the placeholder screen")

    # Keys the screens ask for have to exist, or the interface shows the key itself.
    for key in ("menu.title", "menu.subtitle", "menu.settings", "menu.credits", "chapters.title",
                "locked.title", "locked.characters.body", "locked.collection.body",
                "locked.achievements.body", "chapters.locked", "menu.quit.confirm",
                "menu.newGame.confirm", "data.reset.title", "data.reset.body"):
        gate.check(f'"{key}"' in table, f"the string table has no '{key}'")

    # The brief's entry points, each of which has to exist as a row on the main menu.
    main = read(MAIN_SCREEN)
    for destination in ("Chapters", "Characters", "Collection", "Achievements", "Settings",
                        "Credits"):
        gate.check(f"MenuScreenId.{destination}" in main,
                   f"the main menu has no row for {destination}")
    for key in ("menu.continue", "menu.newGame"):
        gate.check(f'"{key}"' in main, f"the main menu has no {key} entry")

    # BACK has to exist everywhere it can be reached from, and the main menu has to answer the
    # system back gesture itself.
    screens = sorted(f for f in os.listdir(os.path.join(REPO_ROOT, SCREENS)) if f.endswith(".cs"))
    gate.check(len(screens) >= 6, f"only {len(screens)} screen file(s) in {SCREENS}")
    for name in screens:
        text = read(f"{SCREENS}/{name}")
        if "abstract class" in text:
            continue    # the base every screen derives from, not a screen
        gate.check("public override bool OnBack()" in text,
                   f"{name} does not answer a back request")
        if name == "MainMenuScreen.cs":
            gate.check("MenuHeader.Create" not in text,
                       "the main menu has a screen header; its brand block is its header")
        else:
            gate.check("MenuHeader.Create" in text, f"{name} has no header, so it has no way back")
            gate.check("MenuHeader.Create(..., true)" in text or ", true)" in text,
                       f"{name}'s header is built without the back row")


def check_settings(gate: Gate) -> None:
    """The categories the brief lists, and that every row has somewhere to go."""
    catalog = read(CATALOG)
    block = catalog.split("public enum SettingCategory", 1)
    if len(block) != 2:
        raise SystemExit("FATAL: SettingCategory is gone; the ten categories cannot be read")
    categories = re.findall(r"^\s{8}(\w+) = \d+,", block[1].split("}", 1)[0], re.M)
    gate.check(len(categories) == 10, f"the catalogue declares {len(categories)} categories, "
                                      "not the ten the brief lists")

    table = read(STRINGS)
    for category in categories:
        key = category.lower()
        gate.check(f'"category.{key}"' in table, f"no label in the string table for category.{key}")
        gate.check(f'"note.{key}"' in table,
                   f"category '{category}' has no note, so its screen would show whatever "
                   "'note.*' resolved to")

    # Every definition's label, help and options are keys in the same table, and every definition
    # names a destination for its value — the two things that stop a settings screen from being a
    # convincing collection of sliders that nothing reads.
    # Every definition is one positional constructor call: id, category, kind, label key, help
    # key, range, step, option keys, option values, destination.
    definitions = call_arguments(catalog, "new SettingDefinition")
    gate.check(len(definitions) >= 20, f"only {len(definitions)} setting(s) in the catalogue")

    keys = []
    for arguments in definitions:
        if len(arguments) < 11:
            gate.check(False, f"a setting definition has {len(arguments)} arguments, not 11")
            continue

        keys.append(_literal(arguments[3]))
        helper = _literal(arguments[4])
        if helper:
            keys.append(helper)
        keys.extend(re.findall(r'"([^"]+)"', arguments[8]))

        # The destination is what a reader checks to know the row is not decorative.
        gate.check(bool(_literal(arguments[10])),
                   f"setting {_literal(arguments[0]) or '(unnamed)'} names no destination, so its "
                   "value is written nowhere")

    for key in keys:
        gate.check(f'"{key}"' in table,
                   f"the string table has no '{key}', but a catalogue row shows it")


def without_comments(text: str) -> str:
    """The source with comments blanked, so a mentioned name is not a call."""
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    return re.sub(r"//[^\n]*", " ", text)


def _literal(argument: str) -> str:
    """A C# string literal argument, without its quotes. Empty when it is not a literal."""
    text = argument.strip()
    if len(text) >= 2 and text.startswith('"') and text.endswith('"'):
        return text[1:-1]
    return ""


def check_localisation(gate: Gate) -> None:
    """No screen holds a sentence: every visible string is a key."""
    table = read(STRINGS)
    files = []
    for folder in (MENUS, f"{MENUS}/Panels", f"{MENUS}/Components", f"{MENUS}/Widgets", SCREENS,
                   INTERFACE):
        for name in sorted(os.listdir(os.path.join(REPO_ROOT, folder))):
            if name.endswith(".cs"):
                files.append(f"{folder}/{name}")

    for rel in files:
        text = read(rel)
        for call in ("MenuStrings.Get", "MenuStrings.Format", "MenuStrings.Has"):
            for arguments in call_arguments(text, call):
                key = arguments[0]
                if not key.startswith('"'):
                    continue
                if f"{key}" not in table.split("Dictionary<string, string>")[-1]:
                    gate.check(False, f"{rel} asks for {key}, which the string table does not hold")

    # A literal passed where the interface shows text is a sentence that ships untranslated. A
    # gameObject name is allowed to be a literal — it is for the hierarchy, not for the player —
    # so each call is checked at the argument that ends up on screen.
    # Keyed by the name as it is written. The static calls are written with their type; the ones
    # that are called on a receiver are written with a bare method name, which is why the names
    # here are mixed.
    shown = {
        "MenuUi.CreateText": 2,
        "MenuUi.CreateTrackedText": 2,
        "MenuUi.CreateParagraph": 2,
        "MenuButton.Create": 2,
        "MenuHeader.Create": 1,
        "AddEntry": 1,
        "SetLabel": 0,
        "SetMeta": 0,
        "SetHelp": 0,
    }
    for rel in files:
        text = read(rel)
        for name, index in shown.items():
            for arguments in call_arguments(text, name):
                if len(arguments) <= index:
                    continue
                content = arguments[index]
                if content.lstrip().startswith('"'):
                    gate.check(False, f"{rel} shows the literal {content.strip()} in "
                                      f"{name}(...) instead of a string key")


def check_composition(gate: Gate) -> None:
    """The main menu fits the screens it is designed for, and every row is pressable."""
    m = metrics()
    main = read(MAIN_SCREEN)
    title_source = read(TITLE_PANEL)

    footer = number(main, r"FooterHeight\s*=\s*([0-9.]+)f", "MainMenuScreen.FooterHeight")
    minimum_cell = number(main, r"MinimumSecondaryCell\s*=\s*([0-9.]+)f",
                          "MainMenuScreen.MinimumSecondaryCell")
    title_factor = number(title_source, r"TitleSize \* ([0-9.]+)f", "the title's line height")
    subtitle_factor = number(title_source, r"SubtitleSize \* ([0-9.]+)f",
                             "the subtitle's line height")
    columns = int(re.findall(r"SecondaryColumns = (\d+)", read(THEME))[0]) if re.search(
        r"SecondaryColumns = (\d+)", read(THEME)) else int(m.get("SecondaryColumns", 2))
    primary_rows, secondary_rows, system_rows = 2, 4, 2

    for name, aspect in ASPECTS:
        canvas_h = REFERENCE_HEIGHT
        canvas_w = canvas_h * aspect
        box_w = canvas_w - m["ScreenMarginLeft"] - m["ScreenMarginRight"]
        box_h = canvas_h - m["ScreenMarginTop"] - m["ScreenMarginBottom"]
        viewport = box_h - footer

        title_block = (m["LogoHeight"] + m["LogoGap"] + (m["TitleSize"] * title_factor)
                       + (m["SubtitleSize"] * subtitle_factor) + m["TitleGapRule"]
                       + m["TitleRuleHeight"])
        top = title_block + m["TitleGapBody"]

        right = box_w * m["RightBlockFraction"]
        left = box_w * m["LeftColumnFraction"]
        cell = right / columns
        gate.check(cell >= minimum_cell,
                   f"{name}: a two-column cell is {cell:.0f} units wide, under the {minimum_cell:.0f} "
                   "the grid needs before it drops to one column")
        gate.check(left < right,
                   f"{name}: the primary column ({left:.0f}) is not narrower than the secondary "
                   f"block ({right:.0f}), so the composition has no hierarchy")

        section = m["SectionLabelHeight"] + m["SectionLabelGap"]
        play = section + (primary_rows * m["PrimaryPitch"])
        explore = section + (_bands(secondary_rows, columns) * m["SecondaryPitch"])
        system_top = top + explore + m["ClusterGap"]
        system = section + (_bands(system_rows, columns) * m["SecondaryPitch"])
        content = max(top + play, system_top + system) + m["ParagraphBlockPadding"]

        gate.check(content <= viewport,
                   f"{name}: the main menu is {content:.0f} units tall in a {viewport:.0f}-unit "
                   f"viewport, so it would scroll at the default interface size")
        gate.check(title_block + m["TitleGapBody"] < viewport * 0.6,
                   f"{name}: the brand block takes {title_block:.0f} of {viewport:.0f} units, "
                   "leaving the entries too little of the screen")
        gate.check(box_w > 0 and box_h > 0, f"{name}: the content box is empty")

    # Every interactive row is at least 48dp tall. The menu's canvas is 1080 units over a screen
    # that is about 360dp in landscape, so a unit is a third of a dp.
    # Only the rows a player can press: an information row is a fact, is not selectable, and is
    # shorter on purpose.
    pitches = {
        "a primary entry": m["PrimaryPitch"],
        "a secondary entry": m["SecondaryPitch"],
        "a category": m["SecondaryPitch"],
        "a setting row": m["SettingRowHeight"],
    }
    for what, pitch in pitches.items():
        dp = pitch / UNITS_PER_DP
        gate.check(dp >= m["MinimumTouchDp"],
                   f"{what} is {pitch:.0f} units ({dp:.0f}dp), under the {m['MinimumTouchDp']:.0f}dp "
                   "floor for a touch target")


def _bands(rows: int, columns: int) -> int:
    return (rows + columns - 1) // columns


def check_discipline(gate: Gate) -> None:
    """No per-frame searches, no second transition system, no scattered persistence."""
    menu_files = []
    for folder in (MENUS, f"{MENUS}/Panels", f"{MENUS}/Components", f"{MENUS}/Widgets", SCREENS,
                   PRESENTATION, INTERFACE):
        for name in sorted(os.listdir(os.path.join(REPO_ROOT, folder))):
            if name.endswith(".cs"):
                menu_files.append(f"{folder}/{name}")

    # Per frame: five components are allowed a per-frame callback, and each one is named here with
    # the work it does. The menu root compares one size and copies one bool; the backdrop drifts;
    # the region's shell reads one key and adds one float to a clock; the region's strip compares one
    # rect and retires a toast; the pause overlay does nothing at all until a "SAVED" note needs
    # retiring. Everything else in the interface is event-driven, and a sixth entry here is a
    # decision rather than an oversight.
    allowed_frame_callbacks = {
        ROOT: ("LateUpdate",),
        f"{PRESENTATION}/MenuBackdrop.cs": ("Update",),
        f"{INTERFACE}/GameplayShell.cs": ("Update",),
        f"{INTERFACE}/GameplayHud.cs": ("Update",),
        f"{INTERFACE}/GameplayPause.cs": ("Update",),
    }
    forbidden = ("GetComponent", "FindAnyObjectByType", "FindFirstObjectByType", "FindObjectOfType",
                 "Instantiate", "Destroy(", "new GameObject", "Camera.main", "Resources.Load",
                 "PlayerPrefs", "SceneManager", "AudioMixer")
    for rel in menu_files:
        text = read(rel)
        for callback in ("Update", "LateUpdate", "FixedUpdate"):
            signature = f"private void {callback}()"
            if signature not in text and f"void {callback}()" not in text:
                continue
            gate.check(callback in allowed_frame_callbacks.get(rel, ()),
                       f"{rel} has a {callback}; a menu that runs something every frame is a menu "
                       "that costs battery while it is being read")
            body = body_of(text, f"void {callback}()")
            for smell in forbidden:
                gate.check(smell not in body,
                           f"{rel}: {callback} calls {smell} — a per-frame search or allocation")

        # Searching and loading happen at build time, never from a screen.
        for smell in ("FindAnyObjectByType", "FindFirstObjectByType", "FindObjectOfType",
                      "Camera.main", "PlayerPrefs"):
            gate.check(smell not in text, f"{rel} uses {smell}")

    # One transition system, one place that loads a scene, one place that closes the game.
    loaders = [rel for rel in menu_files
               if "LoadScene(" in read(rel) or "LoadSceneAsync(" in read(rel)
               or "SceneManager" in read(rel)]
    gate.check(loaders == [f"{PRESENTATION}/MenuTransition.cs"],
               f"scene loading happens in {loaders}; it belongs to the transition system alone")
    quitters = [rel for rel in menu_files if "Application.Quit" in read(rel)]
    gate.check(quitters == [f"{MENUS}/MenuFlow.cs"],
               f"closing the game happens in {quitters}; it belongs to one place that writes first")

    # Persistence is the settings service and the save store. `PlayerPrefs` is not a store.
    # Settings and progress are files, through the settings service and the save store. The rule
    # is enforced where it matters — the layers that own persistence and the interface that talks
    # to them — and elsewhere a use is reported as a note rather than a failure, because a
    # play-once flag written by the studio intro is not a preference and changing it now would be
    # a change to a stage that is already delivered and verified.
    persistence_layers = ("Runtime/Menus", "Runtime/Presentation", "Runtime/Settings",
                          "Runtime/Storage", "Runtime/Progression")
    for root, _dirs, names in os.walk(os.path.join(REPO_ROOT, "Assets/Aether/Code")):
        for name in names:
            if not name.endswith(".cs"):
                continue
            rel = os.path.relpath(os.path.join(root, name), REPO_ROOT).replace(os.sep, "/")
            # Only real code counts: several files here explain in prose why they do *not* use it,
            # and a check that cannot tell a comment from a call would punish the explanation.
            uses = "PlayerPrefs" in without_comments(read(rel))
            if any(layer in rel for layer in persistence_layers):
                gate.check(not uses, f"{rel} persists through PlayerPrefs instead of the "
                                     "settings service")
            elif uses:
                gate.notes.append(f"{rel} uses PlayerPrefs outside the settings layers "
                                  "(pre-existing)")

    # The lists that can outgrow their box are scrollable, so a long translation or a large
    # interface scale moves the content rather than hiding it.
    for rel in (MAIN_SCREEN, SETTINGS_PANEL, CATEGORY_PANEL, CREDITS_PANEL):
        gate.check("CreateScroll" in read(rel), f"{rel} has no scroll view")

    # Every row is a target the size of its pitch, and every row that is drawn as a fact is not
    # selectable — the two halves of "no dead rows".
    button = read(f"{MENUS}/Components/MenuButton.cs")
    # The states the brief names. Normal needs no case of its own — it is the drawing every other
    # state modifies — so what is checked is that the other four are drawn, and that "locked" is
    # drawn as a state apart from "disabled": a row that is not in the build is not the same as a
    # row that is switched off.
    for state in ("SelectionState.Highlighted", "SelectionState.Pressed", "SelectionState.Disabled",
                  "SelectionState.Selected"):
        gate.check(state in button, f"MenuButton does not draw the {state} state")
    gate.check("_locked" in button and "MenuTheme.Palette.Locked" in button,
               "MenuButton has no locked drawing, so a row that is not in the build would look "
               "like one that is merely switched off")
    # A row drawn as a read-only fact is not selectable, so the navigation walks past it.
    gate.check(re.search(r"SetInformational\(bool informational\)[\s\S]{0,400}?interactable = false",
                         button) is not None,
               "SetInformational does not make the row non-interactable, so the navigation could "
               "land on a row that does nothing")
    gate.check("Time.frameCount" in button,
               "MenuButton has no same-frame guard, so one tap could activate a row twice")


def main() -> int:
    gate = Gate()

    print("main menu:")
    check_flow(gate)
    check_screens(gate)
    check_settings(gate)
    check_localisation(gate)
    check_composition(gate)
    check_discipline(gate)

    m = metrics()
    for name, aspect in ASPECTS:
        box_h = REFERENCE_HEIGHT - m["ScreenMarginTop"] - m["ScreenMarginBottom"]
        print(f"  {name:>7}: canvas {REFERENCE_HEIGHT * aspect:.0f}x{REFERENCE_HEIGHT:.0f}, "
              f"content box {REFERENCE_HEIGHT * aspect - m['ScreenMarginLeft'] - m['ScreenMarginRight']:.0f}"
              f"x{box_h:.0f}")
    print()

    for note in gate.notes:
        print(f"  note {note}")
    for problem in gate.problems:
        print(f"  FAIL {problem}")

    if gate.problems:
        print()
        print(f"{len(gate.problems)} problem(s) across {gate.checked} check(s)")
        return 1

    print(f"  ok   flow, screens, settings, strings, composition and per-frame discipline "
          f"({gate.checked} checks)")
    print(f"{gate.checked}/{gate.checked} checks passed, 0 problem(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
