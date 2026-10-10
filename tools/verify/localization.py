#!/usr/bin/env python3
"""Checks the project's translations, and generates the tables they compile into.

There is exactly one English table and it is a C# dictionary, because it is the reference
every other table is measured against and it is read by people and by tools.  Every other
language is written as a sheet -- one `key=text` pair per line, in a file a translator can
work in without knowing C# -- and this tool turns those sheets into the C# tables the game
compiles, and refuses to let them drift.

What it enforces, all of it from evidence rather than from trust:

  * every language the catalogue offers has a sheet, and the sheet is complete: every English
    key, no key English does not have, no duplicate keys, no empty text;
  * every generated C# table matches its sheet exactly, so a language cannot be "in the build"
    while the sheet says something else;
  * the generated registry matches the catalogue's offered list, so a table cannot be added to
    the code and left out of the game -- or offered by the catalogue and missing from the code;
  * placeholders are preserved: a translation of a string with {0} has {0} in it too, so no
    language can drop a number out of a sentence that has one.

Usage:
    python3 tools/verify/localization.py            # check (this is what CI and the gate run)
    python3 tools/verify/localization.py --write    # regenerate the C# tables from the sheets
    python3 tools/verify/localization.py --coverage # print per-language coverage and stop
"""

from __future__ import annotations

import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

MENU_STRINGS = os.path.join(
    ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime", "Menus", "MenuStrings.cs")
CATALOG = os.path.join(
    ROOT, "Assets", "Aether", "Code", "Aether.Core", "Runtime", "Localization", "LanguageCatalog.cs")
SHEET_DIR = os.path.join(ROOT, "tools", "localization")
TABLE_DIR = os.path.join(
    ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime", "Localization", "Tables")

PAIR_RE = re.compile(r'\{\s*"([^"]+)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\}')
PLACEHOLDER_RE = re.compile(r"\{(\d+)\}")

# The codes the generated tables use, and the C# class each one produces.  Kept here rather
# than derived so that a code with a name the generator would mangle is a visible failure.
CLASS_NAMES = {
    "en": "StringsEn",
    "es": "StringsEs",
    "fr": "StringsFr",
    "de": "StringsDe",
    "it": "StringsIt",
    "pt": "StringsPt",
    "ru": "StringsRu",
    "tr": "StringsTr",
    "ar": "StringsAr",
    "fa": "StringsFa",
    "zh": "StringsZh",
    "ja": "StringsJa",
    "ko": "StringsKo",
}


def read(path: str) -> str:
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def english() -> "list[tuple[str, str]]":
    """The English table, in file order: the reference every other language answers to."""
    source = read(MENU_STRINGS)
    pairs = PAIR_RE.findall(source)
    if not pairs:
        raise SystemExit("localization: no English strings found in MenuStrings.cs")

    # The file holds two tables in one dictionary; the second never repeats a key, and a repeat
    # here would mean a string that cannot be reached, so it is treated as a failure.
    seen = set()
    ordered = []
    for key, text in pairs:
        if key in seen:
            raise SystemExit("localization: English table repeats the key '%s'" % key)
        seen.add(key)
        ordered.append((key, unescape(text)))

    return ordered


def unescape(text: str) -> str:
    return text.replace('\\"', '"').replace("\\\\", "\\")


def escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace('"', '\\"')


def catalogue() -> "dict[str, dict]":
    """Reads the language catalogue: which languages exist, and which ones are offered."""
    source = read(CATALOG)

    offered = []
    match = re.search(r"Offered\s*=\s*\{([^}]*)\}", source)
    if match:
        offered = re.findall(r'"([a-z]{2})"', match.group(1))

    entries = {}
    for code, name, latin, direction, covers in re.findall(
            r'new LanguageDefinition\(\s*"([a-z]{2})"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,'
            r'\s*TextDirection\.(\w+)\s*,\s*(true|false)\s*\)',
            source):
        entries[code] = {
            "code": code,
            "native": name,
            "latin": latin,
            "direction": direction,
            "font": covers == "true",
        }

    if not entries:
        raise SystemExit("localization: the language catalogue could not be read")

    for code in offered:
        if code not in entries:
            raise SystemExit("localization: the catalogue offers '%s' but does not define it" % code)

    return {"entries": entries, "offered": offered}


def sheet(code: str) -> "list[tuple[str, str]]":
    """One language sheet, in file order."""
    path = os.path.join(SHEET_DIR, "strings_%s.txt" % code)
    if not os.path.exists(path):
        raise SystemExit("localization: no sheet for '%s' (%s)" % (code, path))

    pairs = []
    seen = set()
    for number, line in enumerate(read(path).splitlines(), start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        if "=" not in stripped:
            raise Refused("localization: %s:%d has no '=' in it" % (path, number))

        key, _, text = stripped.partition("=")
        key = key.strip()
        text = text.strip()

        if not text:
            raise Refused("localization: %s:%d is empty; delete the line instead" % (path, number))
        if key in seen:
            raise Refused("localization: %s:%d repeats the key '%s'" % (path, number, key))
        seen.add(key)
        pairs.append((key, text))

    return pairs


def check_persian_quality() -> "list[str]":
    """Guard the known Persian copy and RTL-joining regressions against returning."""
    problems = []
    fa = dict(sheet("fa"))
    expected = {
        "menu.credits": "دست‌اندرکاران",
        "credits.title": "دست‌اندرکاران",
        "about.credits": "دست‌اندرکاران",
    }
    for key, wanted in expected.items():
        if fa.get(key) != wanted:
            problems.append("fa: %s should use the approved Persian wording %r" % (key, wanted))

    rate_help = fa.get("setting.frameRate.help", "")
    if "۱۲۰ تا ۱۲۰" in rate_help:
        problems.append("fa: setting.frameRate.help contains the incorrect phrase '۱۲۰ تا ۱۲۰'")
    if "\u200c" not in fa.get("credits.title", ""):
        problems.append("fa: credits.title must preserve the Persian zero-width non-joiner")

    rtl_path = os.path.join(
        ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime", "Menus", "RtlText.cs")
    rtl_source = read(rtl_path) if os.path.exists(rtl_path) else ""
    if rtl_source.count("if (IsRtlMark(text[i])) continue;") < 2:
        problems.append("RtlText.cs must skip combining marks without joining across spaces or ZWNJ")
    if "Add(m, 'ة', 0xFE93, 0xFE94, 0, 0, false);" not in rtl_source:
        problems.append("RtlText.cs is missing the Arabic teh-marbuta presentation forms")
    if "Add(m, 'ی', 0xFBFC, 0xFBFD, 0xFBFE, 0xFBFF, true);" not in rtl_source:
        problems.append("RtlText.cs must use U+FBFF for the Persian yeh medial form")
    if "previousCanJoinForward" not in rtl_source or "nextCanAcceptJoin" not in rtl_source:
        problems.append("RtlText.cs must allow right-joining letters to accept a connection from the previous letter")
    if "for (int g = groups.Count - 1; g >= 0; g--)" not in rtl_source:
        problems.append("RtlText.cs must preserve LTR phrases while ordering mixed RTL runs")
    if "ReverseKeepingMarks" not in rtl_source:
        problems.append("RtlText.cs must keep combining marks attached while reversing visual runs")
    if "\x00" in rtl_source:
        problems.append("RtlText.cs contains a literal NUL byte; use the C# \\0 escape")
    test_path = os.path.join(
        ROOT, "Assets", "Aether", "Code", "Aether.Tests.EditMode", "Tests", "RtlTextTests.cs")
    test_source = read(test_path) if os.path.exists(test_path) else ""
    for regression in ("ArabicRightJoiningLettersConnectToTheirPreviousLetter",
                       "PersianYehUsesItsActualMedialPresentationForm",
                       "ZeroWidthNonJoinerBreaksJoining",
                       "PlaceholderStaysReadableBesideArabicText",
                       "MixedRtlTextKeepsLatinPhraseInReadingOrder",
                       "ArabicCombiningMarksStayWithTheirBaseAfterReversal",
                       "PureLeftToRightTextIsPreservedExactly"):
        if regression not in test_source:
            problems.append("RtlTextTests.cs is missing regression test %s" % regression)
    return problems


def check_runtime_script_fonts() -> "list[str]":
    """Guard the script-aware runtime font resolver and selection gate against regressions."""
    problems = []
    menu_art_path = os.path.join(
        ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime", "Menus", "MenuArt.cs")
    service_path = os.path.join(
        ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime",
        "Localization", "LanguageService.cs")
    menu_art = read(menu_art_path) if os.path.exists(menu_art_path) else ""
    service = read(service_path) if os.path.exists(service_path) else ""

    required_families = {
        "Arabic": "Noto Sans Arabic",
        "Chinese": "Noto Sans CJK SC",
        "Japanese": "Noto Sans CJK JP",
        "Korean": "Noto Sans CJK KR",
    }
    for script, family in required_families.items():
        if family not in menu_art:
            problems.append("MenuArt.cs has no configured runtime font candidate for %s" % script)

    if "public static bool CanRenderLanguage(string code)" not in menu_art:
        problems.append("MenuArt.cs must probe script glyph coverage on the current device")
    if "font.HasCharacter(probe)" not in menu_art:
        problems.append("MenuArt.cs must verify a representative glyph, not just a font name")
    if "MenuArt.CanRenderLanguage(wanted.Code)" not in service:
        problems.append("LanguageService.cs must gate selection on runtime glyph coverage")

    return problems


def check_placeholders(code: str, reference: "list[tuple[str, str]]",
                       translated: "dict[str, str]") -> "list[str]":
    """Finds translations that lost, gained or renumbered a placeholder."""
    problems = []
    english_map = dict(reference)

    for key, text in translated.items():
        expected = PLACEHOLDER_RE.findall(english_map.get(key, ""))
        found = PLACEHOLDER_RE.findall(text)
        if expected != found:
            problems.append("  %s: '%s' has {%s}, English has {%s}"
                            % (key, code, ",".join(found), ",".join(expected)))

    return problems


def render_table(code: str, pairs: "list[tuple[str, str]]") -> str:
    """The C# table for one language: generated, and never edited by hand."""
    class_name = CLASS_NAMES[code]
    lines = []
    lines.append("// <auto-generated>")
    lines.append("//     Generated by tools/verify/localization.py from tools/localization/strings_%s.txt." % code)
    lines.append("//     Edit the sheet, not this file: the tool refuses to let the two disagree.")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("namespace Aether.Gameplay.Localization.Tables")
    lines.append("{")
    lines.append("    /// <summary>The %s table, generated from its sheet in tools/localization.</summary>" % code)
    lines.append("    internal static class %s" % class_name)
    lines.append("    {")
    lines.append("        internal static readonly Strings Table = Strings.Of(")
    # A comma after every pair but the last: these are the arguments of one call, not the entries
    # of an initialiser, and a ';' on the last one is a compile error in a file nobody edits by hand.
    for index, (key, text) in enumerate(pairs):
        comma = "," if index + 1 < len(pairs) else ""
        lines.append('            "%s", "%s"%s' % (escape(key), escape(text), comma))
    lines.append("        );")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


def render_registry(codes: "list[str]") -> str:
    """The C# dictionary that hands the tables to the localization service."""
    lines = []
    lines.append("// <auto-generated>")
    lines.append("//     Generated by tools/verify/localization.py.")
    lines.append("//     Lists every language whose sheet is complete; the tool keeps it exact.")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("using System.Collections.Generic;")
    lines.append("")
    lines.append("namespace Aether.Gameplay.Localization.Tables")
    lines.append("{")
    lines.append("    /// <summary>Every table this build carries, by language code.</summary>")
    lines.append("    internal static class StringsTables")
    lines.append("    {")
    lines.append("        internal static Dictionary<string, Strings> Build()")
    lines.append("        {")
    lines.append("            return new Dictionary<string, Strings>")
    lines.append("            {")
    for code in codes:
        if code == "en":
            lines.append('                // English is the reference table, written in MenuStrings.cs and')
            lines.append('                // wrapped by StringsEn rather than generated like the others.')
            lines.append('                { "en", StringsEn.Table },')
            continue

        lines.append('                { "%s", %s.Table },' % (code, CLASS_NAMES[code]))
    lines.append("            };")
    lines.append("        }")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


class Refused(Exception):
    """Raised when the tool will not write something it can see is wrong."""


def validate_table(code: str, text: str) -> None:
    """Refuses to write a table that is not the shape of a C# call it claims to be.

    A compiler is the real judge and this is not one. What it does catch is the mistake that
    actually happened: an argument list whose last entry was terminated like an initialiser's,
    which is a syntax error in every version of C# and which the bracket counter cannot see,
    because the brackets still balance. The build found it; this finds it here.
    """
    lines = [line for line in text.splitlines() if line.strip()]

    if not lines or lines[-1] != "}":
        raise Refused("localization: %s does not end with a closing brace" % code)
    if lines[-2] != "    }":
        raise Refused("localization: %s does not close its class" % code)
    if lines[-3] != "        );":
        raise Refused("localization: %s does not close its Strings.Of( call" % code)

    # Everything between the opening call and its close is the argument list. Semicolons inside the
    # strings are punctuation; only the code between the arguments is judged, so the literals come
    # out first, and a Spanish sentence with a ';' in it cannot stop a table being written.
    body = text.split("Strings.Of(", 1)
    if len(body) != 2:
        raise Refused("localization: %s does not open a Strings.Of( call" % code)

    arguments = body[1].rsplit("        );", 1)[0]
    code_only = re.sub(r'"(?:[^"\\]|\\.)*"', '""', arguments)
    if ";" in code_only:
        raise Refused("localization: %s has a ';' between its arguments" % code)
    if '"' not in arguments:
        raise Refused("localization: %s has no strings in it at all" % code)


def table_path(code: str) -> str:
    return os.path.join(TABLE_DIR, "%s.cs" % CLASS_NAMES[code])


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true", help="regenerate the C# tables")
    parser.add_argument("--coverage", action="store_true", help="print coverage and stop")
    args = parser.parse_args()

    reference = english()
    keys = [key for key, _ in reference]
    book = catalogue()

    failures = []
    coverage = []
    failures.extend(check_persian_quality())
    failures.extend(check_runtime_script_fonts())

    generated = []
    for code in book["offered"]:
        if code == "en":
            # English is the reference, and it lives in MenuStrings.cs where people edit it. The
            # check below holds the wrapper to it rather than the other way round.
            wrapper = os.path.join(ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime",
                                   "Localization", "StringsEn.cs")
            if not os.path.exists(wrapper):
                failures.append("no English wrapper at %s" % os.path.relpath(wrapper, ROOT))
            elif "Strings.From(MenuStrings.Raw)" not in read(wrapper):
                failures.append("%s does not wrap MenuStrings.Raw"
                                % os.path.relpath(wrapper, ROOT))

            coverage.append((code, len(keys), len(keys)))
            continue

        pairs = sheet(code)
        translated = dict(pairs)

        missing = [key for key in keys if key not in translated]
        extra = [key for key in translated if key not in dict(reference)]
        empty = [key for key, text in pairs if not text.strip()]

        if missing:
            failures.append("%s: %d string(s) not translated, first: %s"
                            % (code, len(missing), ", ".join(missing[:5])))
        if extra:
            failures.append("%s: %d key(s) that English does not have: %s"
                            % (code, len(extra), ", ".join(sorted(extra)[:5])))
        if empty:
            failures.append("%s: %d empty string(s): %s" % (code, len(empty), ", ".join(empty[:5])))

        problems = check_placeholders(code, reference, translated)
        if problems:
            failures.append("%s: placeholders do not match English:\n%s" % (code, "\n".join(problems)))

        done = len(keys) - len(missing)
        coverage.append((code, done, len(keys)))
        generated.append((code, [(key, translated.get(key, "")) for key in keys]))

    if args.coverage:
        for code, done, total in coverage:
            print("%s: %d/%d (%.0f%%)" % (code, done, total, 100.0 * done / total))
        return 0

    if args.write:
        if failures:
            for failure in failures:
                print("FAIL " + failure)
            print("localization: refusing to write tables from incomplete sheets")
            return 1

        if not os.path.isdir(TABLE_DIR):
            os.makedirs(TABLE_DIR)

        for code, pairs in generated:
            rendered = render_table(code, pairs)
            try:
                validate_table(code, rendered)
            except Refused as refused:
                # Printed to stdout rather than raised: the message is what CI and the control
                # harness read, and a traceback on stderr is not a readable failure.
                print("FAIL " + str(refused))
                print("localization: refused to write a table that would not compile")
                return 1

            with open(table_path(code), "w", encoding="utf-8") as handle:
                handle.write(rendered)
            print("wrote %s" % os.path.relpath(table_path(code), ROOT))

        registry = os.path.join(TABLE_DIR, "StringsTables.cs")
        with open(registry, "w", encoding="utf-8") as handle:
            handle.write(render_registry([code for code, _, _ in coverage]))
        print("wrote %s" % os.path.relpath(registry, ROOT))
        return 0

    # Check mode: the committed tables must be exactly what the sheets produce.
    for code, pairs in generated:
        path = table_path(code)
        if not os.path.exists(path):
            failures.append("%s: no generated table at %s" % (code, os.path.relpath(path, ROOT)))
            continue

        wanted = render_table(code, pairs)
        if read(path) != wanted:
            failures.append("%s: %s is out of date; run localization.py --write"
                            % (code, os.path.relpath(path, ROOT)))

    registry = os.path.join(TABLE_DIR, "StringsTables.cs")
    wanted = render_registry([code for code, _, _ in coverage])
    if not os.path.exists(registry):
        failures.append("no generated registry at %s" % os.path.relpath(registry, ROOT))
    elif read(registry) != wanted:
        failures.append("%s is out of date; run localization.py --write"
                        % os.path.relpath(registry, ROOT))

    # The service's registration list is the other half of the same fact.
    service = os.path.join(ROOT, "Assets", "Aether", "Code", "Aether.Gameplay", "Runtime",
                           "Localization", "LanguageService.cs")
    source = read(service) if os.path.exists(service) else ""
    if "StringsTables.Build()" not in source:
        failures.append("LanguageService.cs does not take its tables from StringsTables.Build()")

    if failures:
        for failure in failures:
            print("FAIL " + failure)
        print("localization: %d failure(s)" % len(failures))
        return 1

    printed = ", ".join("%s %d/%d" % (code, done, total) for code, done, total in coverage)
    print("localization: PASSED — %d offered language(s): %s" % (len(coverage), printed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
