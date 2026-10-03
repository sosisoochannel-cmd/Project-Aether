#!/usr/bin/env python3
"""Static verification for Project Aether.

IMPORTANT: this is NOT a C# compiler and NOT a Unity import. It is a project-specific
static analysis pass that catches the failure modes a compiler would catch *within our own
code*, plus the asset/reference integrity problems a Unity import would catch. It cannot
validate Unity API signatures, and it makes no claim to.

What it checks
--------------
1. meta        every asset under Assets/ has a .meta, and every .meta has an asset
2. guid        GUIDs are unique, and every GUID referenced in project assets resolves
3. asmdef      assemblies are valid, acyclic, and reference each other correctly;
               every namespace a file uses is reachable from that file's assembly
4. syntax      braces/parens/brackets balance, with comments and literals stripped
5. symbols     every member accessed on one of our own types actually exists on that
               type, with a compatible argument count (a mini type-checker, scoped to
               project-owned types only — Unity/BCL receivers are skipped, so this
               reports no false positives but also cannot check engine calls)
6. lints       Unity 6 API deprecations and known-risky patterns

Exit code is non-zero if any check fails.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import defaultdict

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(REPO_ROOT, "Assets")
PROJECT_SETTINGS = os.path.join(REPO_ROOT, "ProjectSettings")
CODE_ROOT = os.path.join(ASSETS, "Aether", "Code")

# Assemblies provided by packages rather than by an .asmdef in this repository.
EXTERNAL_ASSEMBLIES = {
    "Unity.InputSystem",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "nunit.framework.dll",
}

# Members our MonoBehaviours/ScriptableObjects inherit from UnityEngine. Needed so that
# `_motor.transform` is not reported as a missing member of PlayerMotor.
INHERITED_MEMBERS = {
    # UnityEngine.Object
    "name", "hideFlags", "GetInstanceID", "GetHashCode", "Equals", "ToString", "GetType",
    "Destroy", "DestroyImmediate", "DontDestroyOnLoad", "Instantiate", "FindObjectOfType",
    # UnityEngine.Component
    "transform", "gameObject", "tag", "GetComponent", "GetComponentInChildren",
    "GetComponentInParent", "GetComponents", "GetComponentsInChildren", "GetComponentsInParent",
    "TryGetComponent", "CompareTag", "SendMessage", "SendMessageUpwards", "BroadcastMessage",
    # UnityEngine.Behaviour
    "enabled", "isActiveAndEnabled",
    # MonoBehaviour
    "StartCoroutine", "StopCoroutine", "StopAllCoroutines", "Invoke", "InvokeRepeating",
    "CancelInvoke", "IsInvoking", "runInEditMode", "useGUILayout", "print",
    # ScriptableObject
    "SetDirty",
    # System.Object / interfaces we implement
    "IsAlive", "TakeDamage",
}

# ---- source text handling ---------------------------------------------------------------


def strip_noncode(text: str) -> str:
    """Replace comments and string/char literals with spaces, preserving length.

    Preserving offsets and line breaks keeps every later regex anchored to real code
    positions rather than shifted ones.
    """
    out = list(text)
    i, n = 0, len(text)
    state = "code"  # code | line | block | str | verbatim | char

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if state == "code":
            if c == "/" and nxt == "/":
                state = "line"
                out[i] = out[i + 1] = " "
                i += 2
                continue
            if c == "/" and nxt == "*":
                state = "block"
                out[i] = out[i + 1] = " "
                i += 2
                continue
            if c == '"':
                state = "verbatim" if (i > 0 and text[i - 1] == "@") else "str"
                out[i] = " "
                i += 1
                continue
            if c == "'":
                state = "char"
                out[i] = " "
                i += 1
                continue
            i += 1
            continue

        if state == "line":
            if c == "\n":
                state = "code"
            else:
                out[i] = " "
            i += 1
            continue

        if state == "block":
            if c == "*" and nxt == "/":
                out[i] = out[i + 1] = " "
                i += 2
                state = "code"
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue

        if state == "verbatim":
            if c == '"' and nxt == '"':
                out[i] = out[i + 1] = " "
                i += 2
                continue
            if c == '"':
                out[i] = " "
                i += 1
                state = "code"
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue

        if state == "str":
            if c == "\\":
                out[i] = " "
                if i + 1 < n and text[i + 1] != "\n":
                    out[i + 1] = " "
                i += 2
                continue
            if c == '"':
                out[i] = " "
                i += 1
                state = "code"
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue

        if state == "char":
            if c == "\\":
                out[i] = " "
                if i + 1 < n and text[i + 1] != "\n":
                    out[i + 1] = " "
                i += 2
                continue
            if c == "'":
                out[i] = " "
                i += 1
                state = "code"
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue

    return "".join(out)


def match_bracket(text: str, open_index: int) -> int:
    """Index of the bracket closing the one at open_index, or -1."""
    pairs = {"{": "}", "(": ")", "[": "]"}
    opening = text[open_index]
    closing = pairs[opening]
    depth = 0
    for i in range(open_index, len(text)):
        if text[i] == opening:
            depth += 1
        elif text[i] == closing:
            depth -= 1
            if depth == 0:
                return i
    return -1


def split_top_level(text: str, separator: str = ",") -> list[str]:
    """Split on a separator that is not nested inside brackets or angle brackets."""
    parts, depth, angle, current = [], 0, 0, []
    for ch in text:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "<":
            angle += 1
        elif ch == ">":
            angle = max(0, angle - 1)

        if ch == separator and depth == 0 and angle == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)

    tail = "".join(current)
    if tail.strip() or parts:
        parts.append(tail)
    return [p for p in parts]


def find_top_level_assignment(chunk: str) -> int:
    """Index of a top-level '=' that is not part of ==, !=, <=, >=, => or += style ops."""
    depth, angle = 0, 0
    for i, ch in enumerate(chunk):
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "<":
            angle += 1
        elif ch == ">":
            angle = max(0, angle - 1)
        elif ch == "=" and depth == 0 and angle == 0:
            prev = chunk[i - 1] if i > 0 else ""
            nxt = chunk[i + 1] if i + 1 < len(chunk) else ""
            if prev in "=!<>+-*/%&|^" or nxt == "=" or nxt == ">":
                continue
            return i
    return -1


def find_top_level_arrow(chunk: str) -> int:
    """Index of a top-level '=>' (an expression-bodied member), or -1.

    Only the declaration level is considered; '=>' inside a nested argument list is ignored.
    """
    depth, angle = 0, 0
    i = 0
    while i < len(chunk) - 1:
        ch = chunk[i]
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "<":
            angle += 1
        elif ch == ">":
            angle = max(0, angle - 1)
        elif ch == "=" and chunk[i + 1] == ">" and depth == 0 and angle == 0:
            return i
        i += 1
    return -1


# ---- C# model ---------------------------------------------------------------------------

MODIFIERS = {
    "public", "private", "protected", "internal", "static", "readonly", "const",
    "virtual", "override", "sealed", "abstract", "partial", "async", "unsafe",
    "extern", "new", "volatile", "ref", "in", "out", "params", "event", "this",
}

TYPE_DECL_RE = re.compile(
    r"\b(?P<kind>class|struct|interface|enum)\s+(?P<name>[A-Za-z_]\w*)"
    r"(?P<generics><[^>{;]*>)?"
    r"(?P<bases>\s*:\s*[^{]+)?"
    r"(?:\s+where\s+[^{]+)?"
    r"\s*\{"
)

NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)\s*(\{|;)")

# The declaring name is the last identifier before the parameter list / end of declaration.
# The optional generics group handles `Publish<T>(...)` and `List<T> _field`.
METHOD_NAME_RE = re.compile(r"([A-Za-z_]\w*)\s*(?:<[^<>]*>\s*)?$")
FIELD_NAME_RE = re.compile(r"([A-Za-z_]\w*)\s*$")


def strip_attributes(chunk: str) -> str:
    """Remove leading [Attribute] groups from a declaration chunk."""
    chunk = chunk.strip()
    while chunk.startswith("["):
        end = match_bracket(chunk, 0)
        if end < 0:
            break
        chunk = chunk[end + 1:].strip()
    return chunk


def parse_members(body: str) -> list[dict]:
    """Extract declarations that sit directly inside a type body.

    Walks the body tracking brace depth so that statements inside method bodies are never
    mistaken for members. Depth zero is member level, because the body passed in excludes
    the type's own braces.
    """
    members: list[dict] = []
    depth = 0
    chunk_start = 0
    i = 0
    n = len(body)

    def emit(chunk: str, terminated_by: str) -> None:
        parsed = parse_declaration(chunk, terminated_by)
        if parsed:
            members.append(parsed)

    while i < n:
        ch = body[i]

        if ch == "{":
            if depth == 0:
                emit(body[chunk_start:i], "block")
            depth += 1
            i += 1
            continue

        if ch == "}":
            depth -= 1
            i += 1
            chunk_start = i
            continue

        if ch == ";" and depth == 0:
            emit(body[chunk_start:i], "semicolon")
            i += 1
            chunk_start = i
            continue

        i += 1

    return members


def parse_declaration(chunk: str, terminated_by: str) -> dict | None:
    chunk = strip_attributes(chunk.replace("\n", " ")).strip()
    if not chunk:
        return None

    if chunk.startswith("~"):
        return None

    # Split off any expression body first: `Type Name => expr` is a property, while
    # `Type Name(args) => expr` is a method. Deciding this before looking for a parameter
    # list is what stops `Mathf.Max(0.05f, x)` in an initialiser being read as the name.
    arrow = find_top_level_arrow(chunk)
    core = chunk[:arrow] if arrow >= 0 else chunk

    assignment = find_top_level_assignment(core)
    paren = core.find("(")
    is_method = paren >= 0 and (assignment < 0 or assignment > paren)

    if is_method:
        close = match_bracket(core, paren)
        if close < 0:
            return None
        head = core[:paren].strip()
        params_text = core[paren + 1:close]
        name_match = METHOD_NAME_RE.search(head)
        if not name_match:
            return None

        params = [p.strip() for p in split_top_level(params_text) if p.strip()]
        required = 0
        for p in params:
            # Parameters carrying a default value are optional, so a call may omit them.
            if find_top_level_assignment(p) < 0:
                required += 1

        return {
            "name": name_match.group(1),
            "kind": "method",
            "type": None,
            "min_args": required,
            "max_args": len(params),
        }

    # Discard the initialiser before reading the declared type and name.
    head = (core[:assignment] if assignment >= 0 else core).strip()
    if not head:
        return None

    name_match = FIELD_NAME_RE.search(head)
    if not name_match:
        return None

    name = name_match.group(1)
    type_text = head[: name_match.start()].strip()
    is_event = bool(re.search(r"\bevent\b", type_text))
    is_property = terminated_by == "block" or arrow >= 0

    return {
        "name": name,
        "kind": "event" if is_event else ("property" if is_property else "field"),
        "type": type_text,
        "min_args": 0,
        "max_args": 0,
    }


def parse_enum_members(body: str) -> list[dict]:
    members = []
    for part in split_top_level(body):
        part = part.strip()
        if not part:
            continue
        name_match = re.match(r"([A-Za-z_]\w*)", part)
        if name_match:
            members.append({"name": name_match.group(1), "kind": "enum",
                            "type": None, "min_args": 0, "max_args": 0})
    return members


def base_type_names(bases: str | None) -> list[str]:
    if not bases:
        return []
    text = bases.lstrip(":").strip()
    names = []
    for part in split_top_level(text):
        part = part.strip()
        if not part:
            continue
        name = re.match(r"([A-Za-z_][\w.]*)", part)
        if name:
            names.append(name.group(1).split(".")[-1])
    return names


class Project:
    """Parsed model of every first-party C# file."""

    def __init__(self) -> None:
        self.files: dict[str, dict] = {}
        self.types: dict[str, dict] = {}          # simple name -> type record
        self.by_namespace: dict[str, set[str]] = defaultdict(set)
        self.duplicate_types: list[str] = []
        self.declared_namespaces: set[str] = set()

    def type_of(self, name: str) -> dict | None:
        return self.types.get(name)

    def members_of(self, type_name: str, seen: set[str] | None = None) -> set[str]:
        seen = seen or set()
        if type_name in seen:
            return set()
        seen.add(type_name)

        record = self.types.get(type_name)
        if not record:
            return set()

        names = {m["name"] for m in record["members"]} | INHERITED_MEMBERS
        for base in record["bases"]:
            if base in self.types:
                names |= self.members_of(base, seen)
        return names

    def arity_of(self, type_name: str, member: str) -> tuple[int, int] | None:
        record = self.types.get(type_name)
        if not record:
            return None
        for m in record["members"]:
            if m["name"] == member and m["kind"] == "method":
                return (m["min_args"], m["max_args"])
        return None

    def declared_type_of(self, type_name: str, member: str) -> str | None:
        """Simple type name a field/property holds, when that type is one of ours."""
        record = self.types.get(type_name)
        if not record:
            return None
        for m in record["members"]:
            if m["name"] == member and m["type"]:
                simple = simple_type_name(m["type"])
                if simple in self.types:
                    return simple
        return None


GENERIC_RE = re.compile(r"<[^<>]*>")


def simple_type_name(text: str) -> str:
    """Reduce a declared type expression to a bare type name.

    Handles generics (including nested ones), arrays, nullability and namespaces.
    """
    if not text:
        return ""
    cleaned = text.strip()
    while True:
        reduced = GENERIC_RE.sub("", cleaned)
        if reduced == cleaned:
            break
        cleaned = reduced
    cleaned = cleaned.replace("[]", "").replace("?", "").strip()
    cleaned = re.sub(r"\b(public|private|protected|internal|static|readonly|const|event|"
                     r"virtual|override|sealed|abstract|partial|new|volatile|ref|in|out|"
                     r"params|async|unsafe|extern)\b", "", cleaned).strip()
    if not cleaned:
        return ""
    token = re.match(r"([A-Za-z_][\w.]*)", cleaned)
    return token.group(1).split(".")[-1] if token else ""


def collect_namespaces_and_types(path: str, code: str, project: Project) -> None:
    """Record every namespace and type declaration in one file."""
    namespaces = [m.group(1) for m in NAMESPACE_RE.finditer(code)]
    for ns in namespaces:
        project.declared_namespaces.add(ns)

    for match in TYPE_DECL_RE.finditer(code):
        name = match.group("name")
        kind = match.group("kind")
        brace = match.end() - 1
        close = match_bracket(code, brace)
        if close < 0:
            continue
        body = code[brace + 1:close]

        members = parse_enum_members(body) if kind == "enum" else parse_members(body)

        record = {
            "name": name,
            "kind": kind,
            "file": path,
            "namespaces": namespaces,
            "bases": base_type_names(match.group("bases")),
            "members": members,
        }

        if name in project.types:
            project.duplicate_types.append(name)
        else:
            project.types[name] = record

        for ns in namespaces:
            project.by_namespace[ns].add(name)


def load_project() -> Project:
    project = Project()
    for root, _dirs, files in os.walk(CODE_ROOT):
        for fname in sorted(files):
            if not fname.endswith(".cs"):
                continue
            full = os.path.join(root, fname)
            with open(full, encoding="utf-8") as handle:
                raw = handle.read()
            rel = os.path.relpath(full, REPO_ROOT).replace(os.sep, "/")
            code = strip_noncode(raw)
            project.files[rel] = {"raw": raw, "code": code}
            collect_namespaces_and_types(rel, code, project)
    return project


# ---- checks -----------------------------------------------------------------------------


class Report:
    def __init__(self) -> None:
        self.failures: list[str] = []
        self.warnings: list[str] = []
        self.notes: list[str] = []

    def fail(self, message: str) -> None:
        self.failures.append(message)

    def warn(self, message: str) -> None:
        self.warnings.append(message)

    def note(self, message: str) -> None:
        self.notes.append(message)


def check_meta(report: Report) -> None:
    ignored = {".DS_Store", "Thumbs.db"}
    assets, metas = set(), set()

    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for d in dirs:
            assets.add(os.path.join(root, d))
        for f in files:
            if f.startswith(".") or f in ignored:
                continue
            path = os.path.join(root, f)
            (metas if f.endswith(".meta") else assets).add(path)

    for asset in sorted(assets):
        if asset + ".meta" not in metas:
            report.fail(f"meta: missing .meta for {os.path.relpath(asset, REPO_ROOT)}")

    for meta in sorted(metas):
        if meta[:-5] not in assets:
            report.fail(f"meta: orphan .meta {os.path.relpath(meta, REPO_ROOT)}")

    # Git cannot track an empty directory, so an empty folder plus its .meta disappears on a
    # fresh clone and leaves the .meta dangling. Either the folder holds content, or it holds
    # a .gitkeep so the folder itself survives.
    for folder in sorted(assets):
        if not os.path.isdir(folder):
            continue
        # A folder kept alive by a .gitkeep is intentionally empty and is fine.
        if os.listdir(folder):
            continue
        report.fail(
            f"meta: '{os.path.relpath(folder, REPO_ROOT)}' is empty; add content or a "
            ".gitkeep, otherwise it vanishes on clone and orphans its .meta")

    if not report.failures:
        report.note(f"meta: {len(assets)} asset(s) each have a .meta, no empty folders")


GUID_DECL_RE = re.compile(r"^guid: ([0-9a-f]{32})\s*$", re.M)
GUID_REF_RE = re.compile(r"guid: ([0-9a-f]{32})")

BUILTIN_GUIDS = {
    "0000000000000000f000000000000000",  # unity default resources
    "0000000000000000e000000000000000",  # unity builtin extra
}


def check_guids(report: Report) -> None:
    declared: dict[str, str] = {}

    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".meta"):
                continue
            path = os.path.join(root, f)
            text = open(path, encoding="utf-8").read()
            found = GUID_DECL_RE.search(text)
            if not found:
                report.fail(f"guid: {os.path.relpath(path, REPO_ROOT)} has no guid line")
                continue
            guid = found.group(1)
            asset = os.path.relpath(path[:-5], REPO_ROOT).replace(os.sep, "/")
            if guid in declared:
                report.fail(f"guid: duplicate {guid} in '{asset}' and '{declared[guid]}'")
            declared[guid] = asset

    # Assets that live inside URP / URP-editor packages rather than in this repository.
    # These are legitimate cross-package references from the pipeline assets.
    package_guids = {
        # URP runtime scripts and default materials
        "11145981673336645838492a2d98e247",  # Renderer2DData
        "bf2edee5c58d82540a51f03df9d42094",  # UniversalRenderPipelineAsset
        "d7fd9488000d3734a9e00ee676215985",  # VolumeProfile
        "2ec995e51a6e251468d2a3fd8a686257",  # UniversalRenderPipelineGlobalSettings
        "247994e1f5a72c2419c26a37e9334c01",  # UniversalProjectSettings
        "a97c105638bdf8b4a8650670310a4cd3",  # 2D default lit material
        "9dfc825aed78fcd4ba02077103263b40",  # sprite-unlit default material
        # URP debug shaders referenced by Renderer2DData
        "cf852408f2e174538bcd9b7fda1c5ae7",
        "573620ae32aec764abd4d728906d2587",
        "53626a513ea68ce47b59dc1299fe3959",
        # URP runtime textures referenced by UniversalRenderPipelineAsset
        "e3d24661c1e055f45a7560c033dbb837",  # blue noise LUT
        "f9ee4ed84c1d10c49aabb9b210b0fc44",  # bayer matrix
    }

    scanned = 0
    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if f.endswith(".meta"):
                continue
            if not f.endswith((".asset", ".unity", ".prefab", ".mat")):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
            text = open(path, encoding="utf-8").read()
            scanned += 1
            for guid in set(GUID_REF_RE.findall(text)):
                if guid in declared or guid in BUILTIN_GUIDS or guid in package_guids:
                    continue
                # URP ships several hundred asset GUIDs inside its global settings file;
                # those legitimately point into the package, not into this repository.
                if "UniversalRenderPipelineGlobalSettings" in rel:
                    continue
                report.warn(f"guid: '{rel}' references unknown guid {guid}")

    report.note(f"guid: {len(declared)} unique asset guid(s); {scanned} asset(s) scanned")


ASMDEF_REQUIRED_KEYS = {"name"}


def check_asmdefs(report: Report, project: Project) -> None:
    asmdefs: dict[str, dict] = {}

    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".asmdef"):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
            try:
                data = json.loads(open(path, encoding="utf-8").read())
            except json.JSONDecodeError as exc:
                report.fail(f"asmdef: '{rel}' is not valid JSON: {exc}")
                continue

            for key in ASMDEF_REQUIRED_KEYS:
                if key not in data:
                    report.fail(f"asmdef: '{rel}' is missing required key '{key}'")

            name = data.get("name")
            if name in asmdefs:
                report.fail(f"asmdef: duplicate assembly name '{name}'")
            data["_dir"] = os.path.dirname(path)
            data["_path"] = rel
            asmdefs[name] = data

    known = set(asmdefs) | EXTERNAL_ASSEMBLIES

    for name, data in asmdefs.items():
        for ref in data.get("references", []):
            if ref not in known:
                report.fail(f"asmdef: '{name}' references unknown assembly '{ref}'")

    # Cycle detection over the project-owned assemblies only.
    graph = {n: [r for r in d.get("references", []) if r in asmdefs] for n, d in asmdefs.items()}
    visiting, visited = set(), set()

    def visit(node: str, stack: list[str]) -> None:
        if node in visited:
            return
        if node in visiting:
            report.fail(f"asmdef: reference cycle {' -> '.join(stack + [node])}")
            return
        visiting.add(node)
        for nxt in graph.get(node, []):
            visit(nxt, stack + [node])
        visiting.discard(node)
        visited.add(node)

    for node in graph:
        visit(node, [])

    # Every namespace a file uses must live in an assembly that file can see.
    def assembly_for(path_abs: str) -> str | None:
        best, best_len = None, -1
        for name, data in asmdefs.items():
            d = data["_dir"]
            if path_abs.startswith(d + os.sep) and len(d) > best_len:
                best, best_len = name, len(d)
        return best

    # A namespace is owned by the assembly of the first file that declares a type in it.
    namespace_owner: dict[str, str] = {}
    for ns, types in project.by_namespace.items():
        for type_name in sorted(types):
            owner = assembly_for(os.path.join(REPO_ROOT, project.types[type_name]["file"]))
            if owner:
                namespace_owner[ns] = owner
                break

    for rel, entry in project.files.items():
        abs_path = os.path.join(REPO_ROOT, rel)
        assembly = assembly_for(abs_path)
        if assembly is None:
            continue
        visible = {assembly, "UnityEngine", "UnityEditor", "System"} | set(
            graph.get(assembly, [])) | EXTERNAL_ASSEMBLIES

        for match in re.finditer(r"^using\s+(?:static\s+)?([A-Za-z_][\w.]*)\s*;", entry["code"], re.M):
            ns = match.group(1)
            if not ns.startswith("Aether"):
                continue
            if ns not in project.declared_namespaces:
                report.fail(f"asmdef: '{rel}' has 'using {ns};' but no such namespace exists")
                continue
            owner = namespace_owner.get(ns)
            if owner and owner not in visible:
                report.fail(
                    f"asmdef: '{rel}' (assembly {assembly}) uses namespace '{ns}' owned by "
                    f"'{owner}', which it does not reference")

    report.note(f"asmdef: {len(asmdefs)} assembly definition(s), references and cycles OK")


BALANCE_PAIRS = {"{": "}", "(": ")", "[": "]"}


def check_syntax(report: Report, project: Project) -> None:
    for rel, entry in project.files.items():
        code = entry["code"]
        for opener, closer in BALANCE_PAIRS.items():
            depth = 0
            for ch in code:
                if ch == opener:
                    depth += 1
                elif ch == closer:
                    depth -= 1
                    if depth < 0:
                        report.fail(f"syntax: '{rel}' has an unmatched '{closer}'")
                        break
            if depth > 0:
                report.fail(f"syntax: '{rel}' has {depth} unmatched '{opener}'")

    if not report.failures:
        report.note(f"syntax: brackets balance in {len(project.files)} file(s)")


IDENT_CHAIN_RE = re.compile(r"(?<![\w.])([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)")
CALL_SUFFIX_RE = re.compile(r"^\s*\(")


def check_symbols(report: Report, project: Project) -> None:
    """Verify member access on project-owned types.

    Only receivers whose type is known and owned by this project are checked. Anything
    whose type cannot be resolved (locals, Unity APIs, BCL types) is skipped, which is why
    this produces no false positives. It therefore under-reports rather than over-reports.
    """
    checked = 0

    for rel, entry in project.files.items():
        code = entry["code"]

        # Build the scope of resolvable names for this file: its own fields/properties plus
        # method parameters whose type is one of our types.
        scope: dict[str, str] = {}

        for type_name, record in project.types.items():
            if record["file"] != rel:
                continue
            for m in record["members"]:
                if m["kind"] in ("field", "property") and m["type"]:
                    simple = simple_type_name(m["type"])
                    if simple in project.types:
                        scope[m["name"]] = simple

        for match in re.finditer(r"\(([^()]*)\)\s*(?:\{|=>)", code):
            for param in split_top_level(match.group(1)):
                param = param.strip()
                if not param:
                    continue
                bits = param.rsplit(" ", 1)
                if len(bits) != 2:
                    continue
                simple = simple_type_name(bits[0])
                pname = bits[1].strip()
                if simple in project.types and re.fullmatch(r"[A-Za-z_]\w*", pname):
                    scope[pname] = simple

        for match in IDENT_CHAIN_RE.finditer(code):
            chain = match.group(1).split(".")
            current = scope.get(chain[0]) or (chain[0] if chain[0] in project.types else None)
            if current is None:
                continue

            for index in range(1, len(chain)):
                member = chain[index]
                if current not in project.types:
                    break

                available = project.members_of(current)
                if member not in available:
                    line = code[: match.start()].count("\n") + 1
                    report.fail(
                        f"symbol: '{rel}' line {line}: '{current}.{member}' — "
                        f"'{current}' has no member '{member}'")
                    break

                # Arity, when the final chain element is a call.
                if index == len(chain) - 1:
                    call = code[match.end():]
                    if CALL_SUFFIX_RE.match(call):
                        open_paren = call.find("(")
                        close = match_bracket(call, open_paren)
                        if close > 0:
                            args = [a for a in split_top_level(call[open_paren + 1:close])
                                    if a.strip()]
                            arity = project.arity_of(current, member)
                            if arity and not (arity[0] <= len(args) <= arity[1]):
                                line = code[: match.start()].count("\n") + 1
                                report.fail(
                                    f"symbol: '{rel}' line {line}: '{current}.{member}' called "
                                    f"with {len(args)} argument(s); declared arity is "
                                    f"{arity[0]}..{arity[1]}")

                checked += 1

                declared = project.declared_type_of(current, member)
                if declared is None:
                    break
                current = declared

    report.note(f"symbol: resolved {checked} member reference(s) on project types")


LINT_RULES = [
    (re.compile(r"\.velocity\b(?!\s*[=:])"),
     "Rigidbody2D.velocity is deprecated in Unity 6 — use linearVelocity"),
    (re.compile(r"\.drag\b"),
     "Rigidbody2D.drag is deprecated in Unity 6 — use linearDamping"),
    (re.compile(r"\.angularDrag\b"),
     "Rigidbody2D.angularDrag is deprecated in Unity 6 — use angularDamping"),
    (re.compile(r"\bFindObjectOfType\s*<"),
     "Object.FindObjectOfType is deprecated in Unity 6 — use FindFirstObjectByType"),
    (re.compile(r"\bFindObjectsOfType\s*<"),
     "Object.FindObjectsOfType is deprecated in Unity 6 — use FindObjectsByType"),
    (re.compile(r"\bGetComponents?\s*<[^>]+>\s*\(\s*\)\s*\[0\]"),
     "GetComponent(...)[0] allocates an array — use TryGetComponent"),
]


def check_lints(report: Report, project: Project) -> None:
    found = 0
    for rel, entry in project.files.items():
        code = entry["code"]
        for pattern, message in LINT_RULES:
            for match in pattern.finditer(code):
                line = code[: match.start()].count("\n") + 1
                report.warn(f"lint: '{rel}' line {line}: {message}")
                found += 1

    if found == 0:
        report.note("lint: no deprecated Unity 6 API usage found")


def check_namespace_hygiene(report: Report, project: Project) -> None:
    """Flag namespaces whose last segment collides with a UnityEngine type name."""
    unity_types = {
        "Camera", "Light", "Input", "Audio", "Physics", "Physics2D", "Animator",
        "Application", "Debug", "Screen", "Time", "Random", "Render", "Resources",
        "Material", "Texture", "Sprite", "Transform", "Object", "Component",
    }
    for ns in sorted(project.declared_namespaces):
        last = ns.split(".")[-1]
        if last in unity_types:
            report.warn(
                f"namespace: '{ns}' ends with '{last}', which shadows a UnityEngine type name "
                "and forces fully-qualified references at every use site")

    if project.duplicate_types:
        for name in sorted(set(project.duplicate_types)):
            report.fail(f"symbol: type '{name}' is declared more than once")


# ---- entry point ------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    report = Report()
    project = load_project()

    check_meta(report)
    check_guids(report)
    check_syntax(report, project)
    check_asmdefs(report, project)
    check_symbols(report, project)
    check_lints(report, project)
    check_namespace_hygiene(report, project)

    if not args.quiet:
        for line in report.notes:
            print(f"  ok    {line}")
        for line in report.warnings:
            print(f"  warn  {line}")
        for line in report.failures:
            print(f"  FAIL  {line}")

    print()
    print(f"files analysed : {len(project.files)}")
    print(f"types modelled : {len(project.types)}")
    print(f"failures       : {len(report.failures)}")
    print(f"warnings       : {len(report.warnings)}")

    if report.failures:
        print("\nVERIFICATION FAILED")
        return 1

    print("\nVERIFICATION PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
