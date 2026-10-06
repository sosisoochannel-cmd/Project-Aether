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
7. player      the launch-time decisions the player settings hold: landscape-only, sensor-driven
               rotation, no Unity splash ahead of the studio intro, and a black ground beneath it

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
    # uGUI: referenced by Aether.Gameplay for the menu. It is a package-provided assembly
    # (com.unity.ugui), not one of ours, so it belongs on this list rather than in an asmdef here.
    "UnityEngine.UI",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "nunit.framework.dll",
}

# What the engine's own types declare as virtual, for the one check that needs it. This is a
# hand-written model of the C# surface, not a guess: a member that is absent here is a member the
# compiler refuses to accept an `override` for, which is exactly the failure this exists to catch.
#
# It is deliberately short. Only the types this project actually derives from appear, and only
# their genuinely virtual members:
#
# * `UnityEngine.Object` declares ToString/Equals/GetHashCode/Finalize as virtual, and nothing
#   else a game class may override. Every UnityEngine type inherits those.
# * `Component`, `Behaviour`, `MonoBehaviour` and `ScriptableObject` add no virtual members of
#   their own. That is the reason `private void OnEnable()` is the correct shape for an engine
#   message and `protected override void OnEnable()` is not: the message is found by name, never
#   overridden.
# * `UnityEngine.EventSystems.UIBehaviour` and `UnityEngine.UI.Selectable` are the two uGUI bases
#   the menu uses. Their members below are from uGUI's own source (com.unity.ugui). Notably absent,
#   and this is the whole point of the table: `OnPointerClick` and `OnSubmit`, which `Selectable`
#   does *not* declare - only `Button`, through IPointerClickHandler and ISubmitHandler, does.
OBJECT_VIRTUALS = frozenset({"ToString", "Equals", "GetHashCode", "Finalize"})

# A base this tool cannot resolve and that is not one of the modelled engine types. "Is it an
# interface?" is answered from the name, because C# writes both in one list and the difference is
# only visible in the assembly that declares the type. The shape required is I followed by another
# capital, which is the .NET convention and, at that, the convention this project follows in its
# own code: it accepts IPointerClickHandler and IPanelBuilder, and it does not mistake Image,
# InputAction or InstantHolder for interfaces. Guessing "interface" is the safe direction here: an
# interface's members cannot be overridden, so nothing is lost by not judging them, whereas calling
# an unmodelled *class* an interface would silently stop checking real overrides.
INTERFACE_NAME_RE = re.compile(r"^I[A-Z]")

UI_BEHAVIOUR_VIRTUALS = frozenset({
    "Awake", "OnEnable", "Start", "OnDisable", "OnDestroy", "OnValidate", "Reset",
    "IsActive", "IsDestroyed",
    "OnRectTransformDimensionsChange", "OnBeforeTransformParentChanged",
    "OnTransformParentChanged", "OnDidApplyAnimationProperties", "OnCanvasGroupChanged",
    "OnCanvasHierarchyChanged",
})

SELECTABLE_VIRTUALS = UI_BEHAVIOUR_VIRTUALS | frozenset({
    "IsInteractable", "InstantClearState", "DoStateTransition",
    "OnMove", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
    "OnSelect", "OnDeselect",
})

ENGINE_VIRTUALS = {
    "Object": OBJECT_VIRTUALS,
    "Component": OBJECT_VIRTUALS,
    "Behaviour": OBJECT_VIRTUALS,
    "MonoBehaviour": OBJECT_VIRTUALS,
    "ScriptableObject": OBJECT_VIRTUALS,
    "UIBehaviour": OBJECT_VIRTUALS | UI_BEHAVIOUR_VIRTUALS,
    "Selectable": OBJECT_VIRTUALS | SELECTABLE_VIRTUALS,
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


def strip_comments_only(text: str) -> str:
    """Replace comments with spaces, preserving length, and leave string literals alone.

    The call-argument reader needs the literals: an argument that happens to be a string is
    still an argument, and a pass that blanks it turns a three-argument call into a two-argument
    one. Comments still have to go, or a call quoted inside prose would be read as code.

    Offsets and line breaks match :func:`strip_noncode` exactly, so a position found in one view
    is a valid position in the other.
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
                i += 1
                continue
            if c == "'":
                state = "char"
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
                i += 2
                continue
            if c == '"':
                i += 1
                state = "code"
                continue
            i += 1
            continue

        if state == "str":
            if c == "\\":
                i += 2
                continue
            if c == '"':
                i += 1
                state = "code"
                continue
            i += 1
            continue

        if state == "char":
            if c == "\\":
                i += 2
                continue
            if c == "'":
                i += 1
                state = "code"
                continue
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
    quote = ""
    for index, ch in enumerate(text):
        if quote:
            # Inside a literal nothing counts: a comma in a sentence is not an argument.
            if ch == "\\" and quote != "'":
                current.append(ch)
                continue
            if ch == quote:
                quote = ""
            current.append(ch)
            continue

        if ch in "\"'":
            quote = ch
            current.append(ch)
            continue

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


def parameter_types(params: list[str]) -> list[str]:
    """The declared type of each parameter, for signature comparison.

    `params` is the split parameter list, so each entry is one parameter's whole text. The name is
    its last identifier and the type everything before that; a default value, when present, has
    already been cut by the caller only for arity, so it is removed here too.
    """
    types = []
    for param in params:
        text = param.split("=")[0].strip()
        text = re.sub(r"\b[A-Za-z_]\w*\s*$", "", text).strip()
        types.append(re.sub(r"\s+", " ", text))
    return types


def parse_declaration(chunk: str, terminated_by: str) -> dict | None:
    chunk = strip_attributes(chunk.replace("\n", " ")).strip()
    if not chunk:
        return None

    if chunk.startswith("~"):
        return None

    # What follows an auto-property's closing brace is its initialiser, not a member of its own:
    # `public List<X> Items { get; } = new List<X>();` is one property, and reading the tail
    # separately made the model invent a method named after the type in the initialiser.
    if chunk.startswith("="):
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
        variadic = False
        for p in params:
            # Parameters carrying a default value are optional, so a call may omit them.
            if find_top_level_assignment(p) < 0:
                required += 1

            # `params object[] args` takes a call with any number of arguments past the fixed
            # ones, so an arity range ending at the declaration's own count would be wrong.
            if re.match(r"params\b", p) or re.search(r"(?<![\w])params\s", p):
                variadic = True

        return {
            "name": name_match.group(1),
            "kind": "method",
            "type": None,
            "min_args": required - (1 if variadic else 0),
            "max_args": None if variadic else len(params),
            "params": tuple(parameter_types(params)),
            "override": bool(re.search(r"\boverride\b", head)),
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
        "params": (),
        "override": bool(re.search(r"\boverride\b", type_text)),
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
    # The capture is " : Base" when a declaration writes a space before its colon, which is the
    # usual style here: strip the space *before* the colon or the colon itself is not leading.
    text = bases.strip()
    text = text[1:] if text.startswith(":") else text
    text = text.strip()
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
        # Every declaration in the project, before nesting is resolved.
        self.declarations: list[dict] = []
        # Simple names declared as a top-level type in more than one namespace. Member access on
        # these cannot be resolved without full namespace context, so checks involving them are
        # skipped rather than guessed — a wrong guess would produce a false failure.
        self.ambiguous: set[str] = set()
        # Simple names shared by several *nested* types. C# resolves these by their containing
        # type, so they are not an ambiguity; they are merged here (see merge_sets) so that member
        # checks stay sound instead of being skipped.
        self.merged: list[str] = []
        self.merge_sets: dict[str, list[dict]] = {}
        self.declared_namespaces: set[str] = set()

    def records_for(self, name: str) -> list[dict]:
        """Every declaration sharing a simple name, for names that cannot be told apart."""
        if name in self.merge_sets:
            return self.merge_sets[name]
        record = self.types.get(name)
        return [record] if record else []

    def _merged_members(self, name: str) -> set[str]:
        """Members of every declaration sharing the name, plus everything they inherit.

        This is an over-approximation, which is the safe direction: a member that exists on any
        declaration is accepted, and a name that exists on none of them is still a failure.
        """
        return {m["name"] for record in self.records_for(name) for m in record["members"]}

    def visible_type(self, rel: str, simple: str) -> str | None:
        """The project type a bare simple name means in one file, or None when it cannot be one.

        Three things have to line up before a simple name is read as one of our types:

        * it names exactly one top-level declaration. A nested type is reachable only inside its
          own container or as ``Outer.Inner``, so `Bounds` — the private struct inside the intro
          sequence — is not the `Bounds` an enemy motor declares for a collider;
        * the name is not shared by several declarations, which the model records as ambiguous;
        * the namespace reaches this file: the file is in it or under it, or imports it.

        Returning None means the name refers to something this tool does not model, and the caller
        skips it rather than guessing. The third clause is the mistake a real compile punished:
        CameraFollow2D named CameraSettings without importing Aether.Core.Settings, and nothing
        here noticed until the editor did.
        """
        record = self.types.get(simple)
        if record is None or simple in self.ambiguous:
            return None

        records = self.records_for(simple)
        if len(records) == 1 and record["container"] is not None:
            # One nested type, declared somewhere else, is not claimed. The name may just as well
            # be the engine's: `Bounds` is a private struct inside the intro sequence and also a
            # UnityEngine struct, and reading the latter as the former accuses `bounds.center` of
            # not existing.
            return simple if record["file"] == rel else None

        entry = self.files.get(rel)
        if entry is None:
            return None

        own = entry["namespace"]
        for candidate in records:
            if candidate["file"] == rel:
                return simple          # declared in this very file
            for namespace in candidate["namespaces"]:
                if namespace in entry["usings"]:
                    return simple
                if own and (own == namespace or own.startswith(namespace + ".")):
                    return simple
        return None

    def type_of(self, name: str) -> dict | None:
        if name in self.ambiguous:
            return None
        return self.types.get(name)

    def members_of(self, type_name: str, seen: set[str] | None = None) -> set[str]:
        if type_name in self.ambiguous:
            return set()

        seen = seen or set()
        if type_name in seen:
            return set()
        seen.add(type_name)

        records = self.records_for(type_name)
        if not records:
            return set()

        names = self._merged_members(type_name) | INHERITED_MEMBERS
        for record in records:
            for base in record["bases"]:
                if base in self.types or base in self.merge_sets:
                    names |= self.members_of(base, seen)
        return names

    def declared_members_of(self, type_name: str, seen: set[str] | None = None) -> set[str]:
        """Member names declared by this type or any of its *project* bases.

        Unlike members_of this never consults the engine's inherited-member set. It answers a
        narrower question - "did anybody in this project declare this?" - which is the question an
        `override` has to be judged against, because the compiler needs a declaration to override.
        """
        if type_name in self.ambiguous:
            return set()

        seen = seen or set()
        if type_name in seen:
            return set()
        seen.add(type_name)

        records = self.records_for(type_name)
        if not records:
            return set()

        names = self._merged_members(type_name)
        for record in records:
            for base in record["bases"]:
                if base in self.types or base in self.merge_sets:
                    names |= self.declared_members_of(base, seen)
        return names

    def arity_of(self, type_name: str, member: str) -> tuple[int, int | None] | None:
        if type_name in self.ambiguous:
            return None
        widest: tuple[int, int | None] | None = None
        for record in self.records_for(type_name):
            for m in record["members"]:
                if m["name"] != member or m["kind"] != "method":
                    continue
                call = (m["min_args"], m["max_args"])
                if widest is None:
                    widest = call
                elif widest[1] is None or call[1] is None:
                    # A variadic overload accepts any number past the smallest fixed count, so the
                    # merged range is open at the top rather than the larger of the two maxima.
                    widest = (min(widest[0], call[0]), None)
                else:
                    widest = (min(widest[0], call[0]), max(widest[1], call[1]))
        return widest

    def declared_type_of(self, type_name: str, member: str) -> str | None:
        """Simple type name a field/property holds, when that type is one of ours."""
        if type_name in self.ambiguous:
            return None

        # With several declarations sharing a name the field or property type is only usable
        # when they all agree; otherwise a following check would be guessing.
        found = set()
        for record in self.records_for(type_name):
            for m in record["members"]:
                if m["name"] != member or not m["type"]:
                    continue
                kind = collection_kind(m["type"])
                found.add(COLLECTION_PREFIX + kind if kind else simple_type_name(m["type"]))
        for simple in found:
            if simple.startswith(COLLECTION_PREFIX) or (
                    simple in self.types and simple not in self.ambiguous):
                return None if len(found) > 1 else simple
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
            "container": None,
        }
        # Attribution to a containing type needs every declaration in the file, so it happens in
        # finalise_types() once the whole project has been parsed.
        project.declarations.append(
            {"path": path, "start": match.start(), "brace": brace, "close": close,
             "record": record})


def finalise_types(project: Project) -> None:
    """Attribute every declaration to its container and build the simple-name lookup.

    This is where C# nesting is modelled. A type declared inside another type is only visible by
    simple name inside that container (or as ``Outer.Inner``), so two private ``DeadState``
    classes nested in two different controllers are not an ambiguity — they cannot be confused.
    Treating them as one was a false positive in this tool, not a problem in the code.

    Names that genuinely cannot be told apart are handled in one of two ways:

    * top-level types sharing a name in different namespaces are a real resolution hazard and are
      recorded as ambiguous, so checks involving them are skipped;
    * declarations sharing a name that are all nested are *merged*: a member is accepted if any
      of them has it, and rejected only if none does. That over-approximates, which is the safe
      direction, and it checks more than skipping them entirely would.
    """
    by_file: dict[str, list[dict]] = defaultdict(list)
    for declaration in project.declarations:
        by_file[declaration["path"]].append(declaration)

    for declarations in by_file.values():
        for declaration in declarations:
            # The innermost enclosing declaration is the container. An outer declaration always
            # starts before a nested one, so comparing spans is enough.
            container = None
            for other in declarations:
                if other is declaration:
                    continue
                if other["brace"] < declaration["start"] < other["close"]:
                    if container is None or other["brace"] > container["brace"]:
                        container = other
            declaration["record"]["container"] = (
                container["record"]["name"] if container else None)

    by_name: dict[str, list[dict]] = defaultdict(list)
    for declaration in project.declarations:
        record = declaration["record"]
        by_name[record["name"]].append(record)
        for ns in record["namespaces"]:
            project.by_namespace[ns].add(record["name"])

    for name, records in by_name.items():
        top_level = [r for r in records if r["container"] is None]
        nested = [r for r in records if r["container"] is not None]

        if not top_level:
            # Every declaration is nested. Inside its own container each one resolves to itself,
            # so they only need merging, never ambiguity handling.
            project.types[name] = records[0]
            if len(records) > 1:
                project.merge_sets[name] = records
                project.merged.append(name)
            continue

        project.types[name] = top_level[0]
        if len(top_level) > 1:
            namespaces = {ns for r in top_level for ns in r["namespaces"]}
            if len(namespaces) > 1:
                project.ambiguous.add(name)
            else:
                project.duplicate_types.append(name)
        if nested:
            # A nested type shadowing a top-level name *is* context-dependent, so member checks
            # on that name stay skipped.
            project.merge_sets.pop(name, None)
            project.ambiguous.add(name)


# `using Aether.Core.Settings;` — a namespace import. A using *statement* (`using (var x = ...)`)
# and `using static X;` / `using X = Y;` are deliberately not matched: the name has to be the whole
# of what follows the keyword.
USING_NAMESPACE_RE = re.compile(r"^[ \t]*using\s+([A-Za-z_][\w.]*)\s*;", re.M)


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
            project.files[rel] = {"raw": raw, "code": code,
                                  "calls": strip_comments_only(raw),
                                  "usings": set(USING_NAMESPACE_RE.findall(code)),
                                  "namespace": next(
                                      (m.group(1) for m in NAMESPACE_RE.finditer(code)), "")}
            collect_namespaces_and_types(rel, code, project)
    finalise_types(project)
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
                # A dangling reference is not a style problem: Unity resolves it to nothing and
                # the asset silently loses a field. That is a failure, not a warning.
                report.fail(f"guid: '{rel}' references unknown guid {guid}")

    report.note(f"guid: {len(declared)} unique asset guid(s); {scanned} asset(s) scanned")


ASMDEF_REQUIRED_KEYS = {"name"}


ASSET_FIELD_RE = re.compile(r"^  (_[A-Za-z_][\w]*):", re.M)
MONO_SCRIPT_RE = re.compile(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})")
MONO_BLOCK_RE = re.compile(r"^--- !u!114 &\d+\n(.*?)(?=^--- |\Z)", re.M | re.S)


def check_asset_fields(report: Report, project: Project) -> None:
    """Check that a serialized asset's field names exist on the script it points at.

    This exists because of how forgiving Unity is: a key in a `.asset` file that does not match a
    serialized field is **silently ignored**, so a typo means an asset that looks right, loads
    without complaint, and quietly does nothing. For tuning that is worse than a crash — the level
    was proven against the numbers in the file, and the game would be running on different ones.

    The reverse direction is reported as a note: a serialized field with no key in the asset keeps
    its C# initialiser, which is legitimate but worth knowing about.
    """
    script_by_guid: dict[str, str] = {}
    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".cs.meta"):
                continue
            meta = os.path.join(root, f)
            found = GUID_DECL_RE.search(open(meta, encoding="utf-8").read())
            if found:
                script_by_guid[found.group(1)] = os.path.relpath(
                    meta[:-5], REPO_ROOT).replace(os.sep, "/")

    checked = 0
    for root, _dirs, files in os.walk(ASSETS):
        for f in sorted(files):
            # Scenes and prefabs are checked as well as assets, and for the same reason: a
            # hand-authored scene is exactly where a mistyped serialized field survives review,
            # because Unity silently keeps the C# default and the scene looks right in a diff.
            if not f.endswith((".asset", ".unity", ".prefab")):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
            kind = ("scene" if f.endswith(".unity")
                    else "prefab" if f.endswith(".prefab") else "asset")
            text = open(path, encoding="utf-8").read()

            # Each component is checked against the script it names, so a file with more than one
            # MonoBehaviour (any real scene) is checked component by component rather than by
            # comparing all of its fields to whichever script was mentioned first.
            blocks = MONO_BLOCK_RE.findall(text) if kind != "asset" else [text]
            for block in blocks:
                found = MONO_SCRIPT_RE.search(block)
                if not found:
                    continue
                script = script_by_guid.get(found.group(1))
                if script is None or script not in project.files:
                    continue

                # Unity's MonoScript for a file is the type whose name matches the file, which is
                # the rule when a file declares more than one type (an enum next to a class).
                wanted = os.path.basename(script)[:-3]
                type_name = None
                fallback = None
                for name, record in project.types.items():
                    if record["file"] != script:
                        continue
                    if fallback is None:
                        fallback = name
                    if name == wanted:
                        type_name = name
                        break
                if type_name is None:
                    type_name = fallback
                if type_name is None:
                    continue

                checked += 1
                members = project.members_of(type_name)
                keys = ASSET_FIELD_RE.findall(block)
                for key in keys:
                    if key not in members:
                        report.fail(
                            f"{kind}: '{rel}' sets '{key}', which does not exist on {type_name} "
                            f"({script}). Unity ignores unknown keys, so this value would silently "
                            "never apply")

                missing = sorted(m for m in members - set(keys)
                                 if m.startswith("_") and not m.startswith("__"))
                if kind == "asset" and keys and missing:
                    # Only for data assets. A component in a scene also has private runtime fields
                    # that Unity never serializes, and listing those as "left at their C# defaults"
                    # would be noise dressed as a finding.
                    report.note(
                        f"{kind}: '{rel}' leaves {len(missing)} field(s) of {type_name} at their "
                        f"C# defaults: {', '.join(missing[:6])}"
                        + (" ..." if len(missing) > 6 else ""))

    if checked:
        report.note(f"asset: {checked} serialized component(s) set only fields their script "
                    "declares")


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

        # The same rule for fully-qualified references. Without this, `using` would be the only
        # way to cross an assembly boundary legally detected here — writing the namespace inline
        # would slip past, which is exactly the kind of hole a verifier must not have.
        seen_inline: set[str] = set()
        for match in re.finditer(r"\bAether(?:\.\w+)+", entry["code"]):
            text = match.group(0)
            parts = text.split(".")
            prefix = None
            for cut in range(len(parts), 0, -1):
                candidate = ".".join(parts[:cut])
                if candidate in project.declared_namespaces and candidate in namespace_owner:
                    prefix = candidate
                    break
            if prefix is None or prefix in seen_inline:
                continue
            seen_inline.add(prefix)
            owner = namespace_owner.get(prefix)
            if owner and owner not in visible:
                line = entry["code"][: match.start()].count("\n") + 1
                report.fail(
                    f"asmdef: '{rel}' line {line} (assembly {assembly}) references namespace "
                    f"'{prefix}' owned by '{owner}', which it does not reference")

        # The third way to name a type across an assembly boundary: not a `using`, not a
        # qualified name, just the bare identifier. Both rules above look for evidence of a
        # namespace; a bare type name carries none, and that is the hole this closes.
        #
        # It is not hypothetical. `AttackRunner` sat in Aether.Core (which references nothing)
        # while reading `AttackDefinition` from Aether.Data, written without a using directive at
        # all: the `using Aether.Data.Config;` rule saw nothing, the `Aether.Data.*` rule saw
        # nothing either because the name never appears qualified, and the static gates passed
        # green on a file no C# compiler would accept. The first real Unity build stopped on it.
        #
        # Deliberately conservative in one direction only: an identifier that is declared
        # anywhere in the same file (a field, a local, a parameter, a method, a type) is treated
        # as shadowing and skipped, because a false accusation here costs more than a miss.
        shadowed = set()
        for pattern in (
            DECLARED_TYPE_RE,
            DECLARED_VARIABLE_RE,
            DECLARED_METHOD_RE,
            DECLARED_USING_ALIAS_RE,
        ):
            shadowed.update(pattern.findall(entry["code"]))

        flagged: set[str] = set()
        for match in re.finditer(r"(?<![\w.])([A-Z]\w*)(?![\w])", entry["code"]):
            name = match.group(1)
            if name in flagged or name in shadowed:
                continue
            record = project.types.get(name)
            if record is None or record.get("container"):
                continue
            owner = assembly_for(os.path.join(REPO_ROOT, record["file"]))
            if owner is None or owner in visible:
                continue
            flagged.add(name)
            line = entry["code"][: match.start()].count("\n") + 1
            report.fail(
                f"asmdef: '{rel}' line {line} (assembly {assembly}) uses the type '{name}' from "
                f"'{owner}', which it does not reference")

    report.note(f"asmdef: {len(asmdefs)} assembly definition(s), references and cycles OK")


# Names a file declares itself. Used to decide whether a bare identifier that matches a project
# type is really that type, or a local/field/method that merely shares its name.
DECLARED_TYPE_RE = re.compile(r"\b(?:class|struct|interface|enum|record)\s+([A-Z]\w*)")
# Modifiers in front of a declaration are part of the declaration, not part of its type. Without
# them the pattern missed `public bool Haptics = true;` entirely, so the bare name `Haptics` looked
# exactly like a reference to the unrelated `Haptics` type in another assembly and was reported as
# an illegal cross-assembly use. Found by a field named after a type that lives somewhere else.
# The indent belongs *after* the separator above, never inside it: a field sitting under a doc
# comment has a newline before it, not a `;`/`{`/`(`/`,`, and was invisible for that reason alone.
DECLARATION_MODIFIERS = (
    r"(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|override|virtual|sealed|abstract|partial|extern|unsafe|ref|in|out|event|async)\s+)*")
DECLARED_VARIABLE_RE = re.compile(
    r"(?:^|[;{(,])\s*(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|override|virtual|sealed|abstract|partial|extern|unsafe|ref|in|out|event|async)\s+)*(?:[A-Za-z_]\w*(?:<[^<>()]*>)?(?:\[\])?(?:[?])?)\s+([A-Za-z_]\w*)\s*(?=[=;,)])\s*",
    re.M)
DECLARED_METHOD_RE = re.compile(
    r"(?:^|[;{}\s])(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|override|virtual|sealed|abstract|partial|extern|unsafe|ref|in|out|event|async)\s+)*(?:[A-Za-z_]\w*(?:<[^<>()]*>)?(?:\[\])?)\s+([A-Za-z_]\w*)\s*\(")

DECLARED_USING_ALIAS_RE = re.compile(r"^using\s+([A-Za-z_]\w*)\s*=", re.M)

# Words that can stand where a declared type stands but are not one. `return panel;` matched the
# local-declaration pattern with `return` as the type, which added an unmodelled type to a name
# that was perfectly well understood and therefore dropped the name from scope entirely - the
# reason a planted `panel._cancel.rectTransform` was not reported.
# `var screen = (MainMenuScreen)root.System.Current;` — a cast is a type the file states, and
# recording it is what keeps an untyped local from being confused with a differently typed local of
# the same name elsewhere in the file. Without it, the `screen` in a helper (a MenuScreen) and the
# `screen` in a test (cast to MainMenuScreen) merged into one type, and the test's `screen.Play`
# was reported against the base class.
CAST_VARIABLE_RE = re.compile(
    r"\bvar\s+([A-Za-z_]\w*)\s*=\s*\(\s*([A-Za-z_]\w*)\s*\)\s*(?![=])")

NOT_A_TYPE = frozenset({
    "return", "throw", "new", "await", "yield", "case", "else", "do", "if", "while", "for",
    "foreach", "switch", "using", "lock", "goto", "break", "continue", "typeof", "default",
    "checked", "unchecked", "fixed", "unsafe", "delegate", "sizeof", "stackalloc", "is", "as",
    "in", "out", "ref", "true", "false", "null", "this", "base", "operator", "when", "var",
})

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


# What a collection offers, by kind, so that member access on it is not checked against its
# element type. A declared type is reduced to a bare name and `Thing[]` reduces to `Thing`, which
# is how `All.Length` — All being `SettingDefinition[]` — came to be reported as
# `SettingDefinition` having no member `Length`. Modelling the container fixes the false accusation
# and checks more than before: `All.Lenght` is now a failure rather than silence.
COLLECTION_MEMBERS = {
    "array": {"Length", "LongLength", "Rank", "GetLength", "GetLongLength", "GetLowerBound",
              "GetUpperBound", "GetValue", "SetValue", "Clone", "CopyTo", "GetEnumerator"},
    "List": {"Count", "Capacity", "Add", "AddRange", "Insert", "InsertRange", "Remove",
             "RemoveAt", "RemoveAll", "RemoveRange", "Clear", "Contains", "IndexOf",
             "LastIndexOf", "Sort", "Reverse", "ToArray", "Find", "FindAll", "FindIndex",
             "FindLast", "ForEach", "GetRange", "GetEnumerator", "CopyTo", "TrimExcess",
             "EnsureCapacity", "BinarySearch", "TrueForAll", "Exists"},
    "IReadOnlyList": {"Count", "Contains", "IndexOf", "CopyTo", "GetEnumerator"},
    "IEnumerable": {"GetEnumerator"},
    "Dictionary": {"Count", "Add", "Remove", "Clear", "Contains", "ContainsKey", "ContainsValue",
                   "TryAdd", "TryGetValue", "GetEnumerator", "Keys", "Values", "EnsureCapacity",
                   "TrimExcess"},
    "HashSet": {"Count", "Add", "Remove", "Clear", "Contains", "UnionWith", "IntersectWith",
                "ExceptWith", "SymmetricExceptWith", "IsSubsetOf", "IsSupersetOf", "Overlaps",
                "SetEquals", "CopyTo", "GetEnumerator", "TrimExcess", "EnsureCapacity"},
    "Queue": {"Count", "Enqueue", "Dequeue", "Peek", "Clear", "Contains", "ToArray",
              "GetEnumerator", "TrimExcess"},
    "Stack": {"Count", "Push", "Pop", "Peek", "Clear", "Contains", "ToArray", "GetEnumerator",
              "TrimExcess"},
}

# Longest first: "IReadOnlyList<" contains "List<", and matching the shorter name first would
# model the interface as the concrete list and allow members it does not have.
COLLECTION_NAMES = tuple(sorted(
    ("List", "IList", "IReadOnlyList", "IEnumerable", "Dictionary", "IDictionary", "HashSet",
     "Queue", "Stack"),
    key=len, reverse=True))

COLLECTION_PREFIX = "@"


def collection_kind(declared: str) -> str | None:
    """Which collection a declared type is, or None when it is not one this tool models."""
    if not declared:
        return None

    spelled = declared.strip()
    if "[]" in spelled:
        return "array"

    for name in COLLECTION_NAMES:
        if name + "<" in spelled:
            return name

    return None


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
        calls = entry["calls"]

        # Build the scope of resolvable names for this file: fields, properties, method
        # parameters and foreach variables whose type is one of our types.
        #
        # Two rules keep this from inventing false failures:
        #
        #   * only real parameter lists count. An earlier version treated every parenthesised
        #     text followed by a brace as one, so `foreach (PlayableNode other in reachable)`
        #     registered a parameter called `reachable` of type PlayableNode and then reported
        #     that PlayableNode has no member `Contains`. Control-flow keywords are excluded
        #     here, and foreach headers are read for what they actually declare.
        #
        #   * a name declared with different types in different places is dropped rather than
        #     guessed. The model is file-global while C# is not, so `other` may be a RouteCost in
        #     one method and a PlayableNode in another; when that happens the name is unknown and
        #     checks involving it are skipped, which under-reports instead of lying.
        #
        # UNKNOWN is recorded for declarations whose type this tool does not model. It matters:
        # `candidate` is a PlayableNode in one loop and a KeyValuePair<...> in another, and
        # without the marker the PlayableNode declaration would look like the only one.
        unknown = "?"
        candidates: dict[str, set[str]] = defaultdict(set)

        # Names declared as collections, and every kind each name was seen declared as. A name
        # declared as two different things is dropped rather than guessed, exactly like a name
        # declared with two different element types.
        collection_kinds: dict[str, set[str]] = defaultdict(set)

        for type_name, record in project.types.items():
            if record["file"] != rel:
                continue
            for m in record["members"]:
                if m["kind"] in ("field", "property") and m["type"]:
                    simple = simple_type_name(m["type"])
                    candidates[m["name"]].add(project.visible_type(rel, simple) or unknown)
                    kind = collection_kind(m["type"])
                    if kind:
                        collection_kinds[m["name"]].add(kind)
                        candidates[m["name"]].add(COLLECTION_PREFIX + kind)

        not_parameters = {"if", "while", "for", "foreach", "switch", "catch", "using", "lock",
                          "fixed", "checked", "unchecked", "when", "return", "do", "else", "in"}
        for match in re.finditer(r"(?<![\w.])([A-Za-z_]\w*)\s*\(([^()]*)\)\s*(?:\{|=>)", code):
            if match.group(1) in not_parameters:
                continue
            for param in split_top_level(match.group(2)):
                param = param.strip()
                if not param:
                    continue
                bits = param.rsplit(" ", 1)
                if len(bits) != 2:
                    continue
                simple = simple_type_name(bits[0])
                pname = bits[1].strip()
                if not re.fullmatch(r"[A-Za-z_]\w*", pname):
                    continue
                candidates[pname].add(project.visible_type(rel, simple) or unknown)

                # A parameter declared as an array or a list: `SettingDefinition[] rows`.
                kind = collection_kind(bits[0])
                if kind:
                    collection_kinds[pname].add(kind)
                    candidates[pname].add(COLLECTION_PREFIX + kind)

        # A local whose declared type is one of ours is a receiver like any other, and leaving
        # locals out is how `MenuConfirmPanel panel = ...; panel._cancel.rectTransform` reached a
        # real compile: a row is a Selectable, and a Selectable has no rectTransform. The declared
        # type is read from the text between the separator and the variable's own name, so
        # `List<EnemySpawn> spawns` is a List and not an EnemySpawn.
        modifier_strip = re.compile(r"^" + DECLARATION_MODIFIERS)
        for match in DECLARED_VARIABLE_RE.finditer(code):
            declared = modifier_strip.sub(
                "", code[match.start():match.start(1)].strip(";{(, \t\n"))
            simple = simple_type_name(declared)
            if simple in NOT_A_TYPE or not simple:
                continue

            candidates[match.group(1)].add(project.visible_type(rel, simple) or unknown)

            kind = collection_kind(declared)
            if kind:
                collection_kinds[match.group(1)].add(kind)
                candidates[match.group(1)].add(COLLECTION_PREFIX + kind)

        # A cast on the right of a `var` local, for the same reason.
        for match in CAST_VARIABLE_RE.finditer(code):
            candidates[match.group(1)].add(
                project.visible_type(rel, match.group(2)) or unknown)

        # foreach (PlayableNode node in nodes) — the loop variable is a real declaration, and
        # reading it makes member checks *stronger*: what it points at is now known.
        for match in re.finditer(
                r"\bforeach\s*\(\s*([A-Za-z_][\w.]*(?:<[^<>]*>)?)\s+([A-Za-z_]\w*)\s+in\b", code):
            simple = simple_type_name(match.group(1))
            candidates[match.group(2)].add(project.visible_type(rel, simple) or unknown)

            kind = collection_kind(match.group(1))
            if kind:
                collection_kinds[match.group(2)].add(kind)
                candidates[match.group(2)].add(COLLECTION_PREFIX + kind)

        scope: dict[str, str] = {}
        for name, types in candidates.items():
            if name in collection_kinds and len(collection_kinds[name]) > 1:
                continue
            if len(types) == 1:
                scope[name] = next(iter(types))

        for match in IDENT_CHAIN_RE.finditer(code):
            chain = match.group(1).split(".")
            current = scope.get(chain[0]) or (chain[0] if chain[0] in project.types else None)
            if current is None:
                continue

            for index in range(1, len(chain)):
                member = chain[index]

                # A collection's members are its own; what its elements offer is not modelled here,
                # so the walk stops rather than guessing.
                if current.startswith(COLLECTION_PREFIX):
                    allowed = COLLECTION_MEMBERS.get(current[len(COLLECTION_PREFIX):])
                    if allowed is not None and member not in allowed:
                        line = code[: match.start()].count("\n") + 1
                        report.fail(
                            f"symbol: '{rel}' line {line}: '{chain[0]}.{member}' — "
                            f"a {current[len(COLLECTION_PREFIX):]} has no member '{member}'")
                    break

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
                    # The argument list is read from the view that keeps string literals, at an
                    # offset found in the view that blanks them: the two are the same length.
                    call = calls[match.end():]
                    if CALL_SUFFIX_RE.match(call):
                        open_paren = call.find("(")
                        close = match_bracket(call, open_paren)
                        if close > 0:
                            args = [a for a in split_top_level(call[open_paren + 1:close])
                                    if a.strip()]
                            arity = project.arity_of(current, member)
                            within = arity is not None and arity[0] <= len(args) and (
                                arity[1] is None or len(args) <= arity[1])
                            if arity and not within:
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


# Engine types that live outside `UnityEngine` itself, with the namespace that has to be imported
# to name them. The list is curated, and deliberately short: it holds the uGUI, EventSystems and
# Input System types this project actually uses. The engine's own surface cannot be enumerated from
# here, but a *using directive* can be checked, and this is the mistake that costs a compile: the
# play-mode tests named CanvasScaler with only `using UnityEngine;` in scope.
ENGINE_UI_NAMES = {
    "UnityEngine.UI": (
        "Image", "RawImage", "Text", "Selectable", "Button", "Slider", "Toggle", "ScrollRect",
        "Mask", "Graphic", "CanvasScaler", "GraphicRaycaster", "LayoutElement", "LayoutGroup",
        "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup", "ContentSizeFitter",
        "AspectRatioFitter", "Scrollbar", "InputField", "CanvasUpdateRegistry",
    ),
    "UnityEngine.EventSystems": (
        "EventSystem", "BaseEventData", "PointerEventData", "AxisEventData", "UIBehaviour",
        "StandaloneInputModule", "IPointerClickHandler", "IPointerEnterHandler",
        "IPointerExitHandler", "IPointerDownHandler", "IPointerUpHandler", "ISelectHandler",
        "IDeselectHandler", "IMoveHandler", "ISubmitHandler", "IDragHandler", "IBeginDragHandler",
        "IEndDragHandler", "ICancelHandler", "IScrollHandler", "IPointerMoveHandler",
    ),
    # The assembly is Unity.InputSystem; the namespace inside it is UnityEngine.InputSystem, which
    # is what a file has to import for the UI module.
    "UnityEngine.InputSystem.UI": ("InputSystemUIInputModule",),
}

ENGINE_TYPE_NAMES = (
    # Names that exist in UnityEngine and not in the BCL namespaces these files import by default.
    # Deliberately excludes MonoBehaviour, Object, GameObject, Transform, Debug, Camera,
    # Application, Random and Input: those are either declared by our own types or shared with
    # System, and a verifier that cries wolf gets ignored.
    "Mathf", "Vector2", "Vector3", "Vector2Int", "Vector3Int", "Quaternion", "Rect", "Color",
    "Color32", "Collider2D", "BoxCollider2D", "CircleCollider2D", "Rigidbody2D",
    "RigidbodyType2D", "ContactFilter2D", "RaycastHit2D", "LayerMask", "Screen", "Time",
    "KeyCode", "AudioSource", "AudioClip", "SpriteRenderer", "Tilemap", "TilemapRenderer",
    "TilemapCollider2D", "CompositeCollider2D", "GridLayout", "Animator", "Light2D", "Sprite",
)
ENGINE_USING_RE = re.compile(r"^\s*using\s+UnityEngine(?:\.[\w.]+)?\s*;", re.M)


# A name is being *declared* rather than *used* when what follows it is a value separator:
# `public bool Haptics = true;` is a field named after a type, not a reference to it, and reporting
# it would be the false accusation this tool's own history warns about.
VALUE_FOLLOWS_RE = re.compile(r"\s*(?:[=;,:\)\]])")
BARE_NAME_RE = re.compile(r"(?<![\w.])([A-Za-z_]\w*)")


def check_engine_usings(report: Report, project: Project) -> None:
    """A uGUI, EventSystems or Input System type needs the directive that brings it in.

    `using UnityEngine;` does not reach `UnityEngine.UI`, and the play-mode tests proved it: a file
    that named CanvasScaler with only the engine's own namespace in scope does not compile. Static
    analysis cannot enumerate what the engine declares, but for the handful of namespaces this
    project actually uses it can check the directive, which is the half of the mistake that is
    visible from here.

    A name that is qualified (`UnityEngine.UI.CanvasScaler`) or introduced by the directive is
    fine, and a name that is being *declared* rather than referenced is an identifier choice, not a
    type - the same guard the project-name rule uses.
    """
    findings = 0

    for rel, entry in project.files.items():
        code = entry["code"]
        for namespace, names in ENGINE_UI_NAMES.items():
            if namespace in entry["usings"]:
                continue
            for name in names:
                reported = False
                for match in re.finditer(r"(?<![\w.])" + name + r"(?![\w])", code):
                    if VALUE_FOLLOWS_RE.match(code[match.end():]):
                        continue
                    line = code[:match.start()].count("\n") + 1
                    report.fail(
                        f"engine: '{rel}' line {line} names '{name}' but never imports "
                        f"'{namespace}' (add 'using {namespace};' or qualify the name)")
                    findings += 1
                    reported = True
                    break
                if reported:
                    continue

    if not findings:
        names = sum(len(v) for v in ENGINE_UI_NAMES.values())
        report.note(
            f"engine: every uGUI, EventSystems and Input System type named is imported "
            f"({names} name(s) across {len(ENGINE_UI_NAMES)} namespace(s))")


def check_project_names(report: Report, project: Project) -> None:
    """A file that names one of our types by simple name has to be able to see that type.

    C# resolves a bare name through the file's own namespace and its using directives, and nothing
    else. A file that names a type it cannot see does not compile (CS0246), and the editor said so
    about the shipped code: CameraFollow2D declared `CameraSettings wanted` without importing
    Aether.Core.Settings. The member-existence check had looked right at that line and passed,
    because it has no opinion about whether a type is in scope.

    Three deliberate silences, all of them the safe direction:

    * a nested type is skipped unless the file declares it, because a bare name that happens to
      match a private nested `Bounds` somewhere else may just as well be UnityEngine's own type;
    * a name that is being declared rather than used (`public bool Haptics = true;`) is a choice of
      identifier, not a reference;
    * a name this tool cannot tell apart - two declarations, two namespaces - is skipped.

    One file and one name are reported once, at the first line that names it.
    """
    findings = 0

    for rel, entry in project.files.items():
        code = entry["code"]
        reported: set[str] = set()

        for match in BARE_NAME_RE.finditer(code):
            name = match.group(1)
            if name in reported:
                continue

            record = project.types.get(name)
            if record is None or name in project.ambiguous or name in project.merge_sets:
                continue
            if record["container"] is not None:
                continue
            if project.visible_type(rel, name) is not None:
                continue
            if VALUE_FOLLOWS_RE.match(code[match.end():]):
                continue

            reported.add(name)
            namespace = record["namespaces"][0] if record["namespaces"] else "the global namespace"
            line = code[:match.start()].count("\n") + 1
            report.fail(
                f"using: '{rel}' line {line} names the type '{name}' ({namespace}) without "
                f"importing it; the bare name resolves to nothing the compiler can see (CS0246)")
            findings += 1

    if not findings:
        report.note(
            f"using: every project type a file names by simple name is one it imports "
            f"({len(project.types)} name(s) known)")


def check_duplicate_members(report: Report, project: Project) -> None:
    """Two declarations of the same member inside one type do not compile.

    C# answers a repeated name with CS0102 (a field, property or event declared twice) or CS0111
    (a method declared twice with the same signature). Neither is visible to any other check here:
    every rule in this file asks whether a name exists, and a name that exists twice passes all of
    them. The editor's compiler found exactly that - a camera with two OnEnable methods - after the
    whole gate had gone green, which is why this rule exists.

    Overloads are legal and must stay legal, so methods are compared by name and parameter types.
    Constructors are skipped: a static constructor and an instance constructor share the type's
    name and are not duplicates of each other.
    """
    findings = 0

    for declaration in project.declarations:
        record = declaration["record"]
        seen: dict[tuple, dict] = {}

        for member in record["members"]:
            if member["name"] == record["name"]:
                continue          # a constructor, not a duplicate

            if member["kind"] == "method":
                key = ("method", member["name"], member["params"])
            elif member["kind"] in ("field", "property", "event"):
                key = ("value", member["name"])
            else:
                key = (member["kind"], member["name"])

            if key in seen:
                rel = os.path.relpath(record["file"], REPO_ROOT).replace(os.sep, "/")
                signature = member["name"]
                if member["kind"] == "method":
                    signature += "(" + ", ".join(member["params"]) + ")"
                report.fail(
                    f"duplicate: '{rel}' declares '{record['name']}.{signature}' more than once, "
                    f"which is a compile error (CS0102/CS0111)")
                findings += 1
                continue

            seen[key] = member

    if not findings:
        report.note(
            f"duplicate: no type declares a member twice "
            f"({len(project.declarations)} type declaration(s) checked)")


def check_overrides(report: Report, project: Project) -> None:
    """An `override` has to name something its base actually declares.

    The compiler rejects the rest with CS0115, and nothing else in this file can see the mistake:
    the member's name is never called, so the member-existence rule has nothing to look at. The
    fault this catches is not hypothetical - `public override void OnPointerClick(...)` on a
    `Selectable` subclass compiles nowhere, because Selectable implements neither
    IPointerClickHandler nor ISubmitHandler, and it reached a real build before this rule existed.

    A base the model does not know is skipped with a note rather than failed: this tool cannot see
    an engine assembly's surface, and guessing at it in either direction would be worse than
    saying so.

    The judgement is by name, not by signature. Arity is the difference between an override and an
    overload, and modelling the engine's parameter lists by hand would put a wrong number in the
    gate's mouth; a name that exists on the base is accepted here and left to the compiler.
    """
    findings = 0
    unmodelled: set[str] = set()

    for declaration in project.declarations:
        record = declaration["record"]
        class_bases = []
        for base in record["bases"]:
            known = project.types.get(base) or (project.merge_sets.get(base) or [None])[0]
            if known is not None and known["kind"] == "interface":
                continue
            class_bases.append(base)

        # A class with no class base inherits object, and an interface on its own does not change
        # that - which is what makes `public override string ToString()` legal there.
        if not class_bases:
            class_bases = ["Object"]

        targets = set()
        engine = set()
        unknown = []
        judged = []
        for base in class_bases:
            if base in project.types or base in project.merge_sets:
                targets.add(base)
                judged.append(base)
            elif base in ENGINE_VIRTUALS:
                engine |= ENGINE_VIRTUALS[base]
                judged.append(base)
            elif INTERFACE_NAME_RE.match(base):
                continue          # implemented, never overridden
            else:
                unknown.append(base)

        if unknown:
            unmodelled.update(unknown)
            continue

        for member in record["members"]:
            if not member.get("override"):
                continue

            if member["name"] in engine:
                continue

            allowed = set()
            for target in targets:
                allowed |= project.declared_members_of(target)
            if member["name"] in allowed:
                continue

            rel = os.path.relpath(record["file"], REPO_ROOT).replace(os.sep, "/")
            bases = " or ".join(sorted(judged))
            report.fail(
                f"override: '{rel}' declares '{record['name']}.{member['name']}' as an override, "
                f"but {bases} does not declare it (CS0115)")
            findings += 1

    if unmodelled:
        report.note("override: base(s) this tool cannot model, so overrides of theirs went "
                    "unjudged: " + ", ".join(sorted(unmodelled)))

    if not findings:
        report.note(
            f"override: every override names a member its base declares "
            f"({len(ENGINE_VIRTUALS)} engine base(s) modelled by hand)")


def check_nested_member_scope(report: Report, project: Project) -> None:
    """A member of a nested type is not in scope in the enclosing type.

    `Layout.JumpCentre` is a member of the nested `Layout` class; `JumpCentre` on its own is not
    a name the enclosing `TouchControlsView` can see, which is what the third licensed build
    stopped on - six times in three lines. The symbol check could not see it either: it resolves
    members accessed *on a receiver*, and these had no receiver at all.

    An occurrence inside the nested type's own body is legal (members are in scope there), a name
    the enclosing type also declares is legal by C# name lookup, and the target of an assignment
    is legal inside an object initialiser (`new Extra { Kind = ... }`), so all three are skipped.
    """
    enclosing_member_names: dict[str, set[str]] = {}
    for decl in project.declarations:
        record = decl["record"]
        if record["container"] is None:
            enclosing_member_names.setdefault(decl["path"], set()).update(
                m["name"] for m in record["members"])

    findings = 0
    for rel, entry in project.files.items():
        code = entry["code"]

        nested: dict[str, str] = {}
        own_ranges: list[tuple[int, int]] = []
        for decl in project.declarations:
            if decl["path"] != rel:
                continue
            record = decl["record"]
            if record["container"] is not None:
                own_ranges.append((decl["brace"], decl["close"]))
                for member in record["members"]:
                    nested.setdefault(member["name"], record["name"])

        if not nested:
            continue

        legal = enclosing_member_names.get(rel, set())

        # A declaration shadows the nested name only when it is *outside* the nested type. The
        # nested type's own fields are declarations too — `public static readonly Vector2
        # JumpCentre` — and counting those as shadowing would turn this rule off for every name
        # it exists to check, which is what happened when the declaration patterns learned to see
        # indented fields.
        shadowing = set()
        for pattern in (DECLARED_TYPE_RE, DECLARED_VARIABLE_RE, DECLARED_METHOD_RE,
                        DECLARED_USING_ALIAS_RE):
            for found in pattern.finditer(code):
                if any(start <= found.start() <= end for start, end in own_ranges):
                    continue
                shadowing.add(found.group(1) if found.groups() else found.group(0))

        for name in sorted(nested):
            if name in legal or name in shadowing:
                continue
            for match in re.finditer(r"(?<![\w.])" + re.escape(name) + r"(?![\w])", code):
                if any(start <= match.start() <= end for start, end in own_ranges):
                    continue

                # `Kind = ExtraKind.RunState` inside `new Extra { ... }` is an object initialiser,
                # where the nested member is the assignment's target and lookup runs in the nested
                # type's own context. Only reads are this rule's business.
                if re.match(r"\s*=(?!=)", code[match.end():]):
                    continue
                line = code[: match.start()].count("\n") + 1
                report.fail(
                    f"scope: '{rel}' line {line} names '{name}', which is a member of the nested "
                    f"type '{nested[name]}' and not in scope here (write '{nested[name]}.{name}')")
                findings += 1
                break

    if not findings:
        report.note("scope: no nested-type member is used without its container")


def check_engine_names(report: Report, project: Project) -> None:
    """A file may not name a UnityEngine type it never imports.

    The compiler catches this, but only after a container has been pulled and the editor has
    started, which is a ten-minute round trip per name. `PlayerTraversalSolver` reached the
    first licensed build with `Mathf` and no `using UnityEngine;` and cost exactly that.

    Shadowing is respected in the same conservative way as the asmdef rule: a name the file
    declares itself is not the engine type.
    """
    findings = 0

    for rel, entry in project.files.items():
        code = entry["code"]
        if ENGINE_USING_RE.search(code):
            continue

        declared = set()
        for pattern in (
            DECLARED_TYPE_RE,
            DECLARED_VARIABLE_RE,
            DECLARED_METHOD_RE,
            DECLARED_USING_ALIAS_RE,
        ):
            declared.update(pattern.findall(code))

        for name in ENGINE_TYPE_NAMES:
            if name in declared:
                continue
            match = re.search(r"(?<![\w.])" + name + r"(?![\w])", code)
            if not match:
                continue
            line = code[: match.start()].count("\n") + 1
            report.fail(
                f"engine: '{rel}' line {line} names '{name}' but the file never imports "
                f"UnityEngine (add 'using UnityEngine;' or qualify the name)")
            findings += 1
            break

    if not findings:
        report.note(
            f"engine: no file names a UnityEngine type it does not import "
            f"({len(ENGINE_TYPE_NAMES)} names checked)")


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
            report.fail(f"symbol: type '{name}' is declared more than once in the same namespace")

    if project.merged:
        report.note(
            f"symbol: {len(project.merged)} nested type name(s) declared in more than one "
            "container are merged for member checking (C# resolves them by their outer type, so "
            "this is not an error)")

    if project.ambiguous:
        report.note(
            f"symbol: {len(project.ambiguous)} simple type name(s) declared in more than one "
            "namespace; member checks involving them are skipped (see docs/ARCHITECTURE.md)")
        for name in sorted(project.ambiguous):
            report.warn(
                f"symbol: '{name}' is declared in more than one namespace; references to it are "
                "unverifiable by this tool and rely on the real compiler")


# ---- scene flow --------------------------------------------------------------------------

BUILD_SETTINGS = "ProjectSettings/EditorBuildSettings.asset"
INTRO_SCENE = "Assets/Aether/Scenes/StudioIntro.unity"
INTRO_SCRIPT = "Assets/Aether/Code/Aether.Gameplay/Runtime/Flow/StudioIntroSequence.cs"
BRAND_DIR = "Assets/Aether/Resources/Brand"

SCENE_ENTRY_RE = re.compile(
    r"- enabled: (\d+)\n    path: ([^\n]+)\n    guid: ([0-9a-f]{32})")

IMAGE_EXTENSIONS = (".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr")

# What a brand mark's importer has to say. The studio intro draws the mark from its alpha channel
# and recolours it, so a texture imported without these is a mark that is invisible (no alpha or no
# CPU access), fringed (block compression), or wrapped at the edges.
REQUIRED_TEXTURE_SETTINGS = {
    "textureType": "8",            # Sprite
    "spriteMode": "1",             # single
    "spriteMeshType": "0",         # full rect: a tight mesh can crop the artwork's own edges
    "alphaIsTransparency": "1",
    "isReadable": "1",             # the intro reads the pixels to recolour them
    "enableMipMap": "0",
    "filterMode": "1",             # bilinear
    "wrapU": "1",
    "wrapV": "1",
    "textureCompression": "0",     # uncompressed: a logo is small and its edges are the point
}


PLAYER_SETTINGS = "ProjectSettings/ProjectSettings.asset"
BOOT_SCENE = "Assets/Aether/Scenes/Boot.unity"

# The serialized values behind the Player Settings that decide what the phone shows in the first
# second of the app. They are written here as the numbers Unity writes, with what the editor's UI
# calls them, because that pairing is the part that is easy to get wrong and invisible afterwards.
AUTO_ROTATION = 4                # Default Orientation: Auto Rotation
AUTO_ROTATION_SENSOR = 1         # Auto Rotation Behavior: Sensor (ignores the device's lock)
SPLASH_BACKGROUND_MAX = 0.05     # luminance ceiling for the ground behind the first frame


def _setting(text: str, name: str) -> float | None:
    """The numeric value of one serialized player setting, or None when it is absent."""
    match = re.search(rf"^  {name}: (-?[0-9.]+)$", text, re.M)
    if match:
        return float(match.group(1))
    match = re.search(rf"^  {name}: \{{r: ([0-9.]+), g: ([0-9.]+), b: ([0-9.]+), a: [0-9.]+\}}$",
                      text, re.M)
    return None if match is None else max(float(match.group(i)) for i in (1, 2, 3))


def check_player_settings(report: Report) -> None:
    """Check the settings that decide how the app launches, before any of our code runs.

    Orientation and the splash screen are not gameplay: they are what the player sees while the
    first scene loads, and they are decided entirely by `ProjectSettings.asset`. A device is the
    only place the result is visible, so the values are checked here, the built APK's manifest is
    checked in CI, and the screenshot on a phone is the final word.

    Landscape only, both ways up, driven by the rotation sensor rather than by the device's own
    rotation lock: a player holding the phone sideways gets the game the way it was drawn even if
    their phone is locked to portrait, and a player holding it the other way gets it flipped
    instead of the app refusing to turn.
    """
    path = os.path.join(REPO_ROOT, PLAYER_SETTINGS)
    if not os.path.exists(path):
        report.fail(f"player: {PLAYER_SETTINGS} is missing; nothing decides the orientation")
        return

    text = open(path, encoding="utf-8").read()

    orientation = _setting(text, "defaultScreenOrientation")
    if orientation is None:
        report.fail("player: defaultScreenOrientation is not set, so the launch orientation is "
                    "whatever the last editor wrote")
    elif orientation != AUTO_ROTATION:
        report.fail(f"player: defaultScreenOrientation is {orientation:g}, not Auto Rotation "
                    f"({AUTO_ROTATION}); landscape-only needs Auto Rotation with the landscape "
                    "sides allowed")

    for name, wanted, why in (
        ("allowedAutorotateToPortrait", 0, "portrait becomes selectable, so a phone held upright "
                                           "shows the game sideways"),
        ("allowedAutorotateToPortraitUpsideDown", 0, "upside-down portrait becomes selectable"),
        ("allowedAutorotateToLandscapeLeft", 1, "one of the two landscape directions is refused"),
        ("allowedAutorotateToLandscapeRight", 1, "one of the two landscape directions is refused"),
    ):
        value = _setting(text, name)
        if value is None:
            report.fail(f"player: {name} is not set, so the launch orientations are undefined")
        elif value != wanted:
            report.fail(f"player: {name} is {value:g}, expected {wanted:g} - {why}")

    behavior = _setting(text, "androidAutoRotationBehavior")
    if behavior is None:
        report.fail("player: androidAutoRotationBehavior is not set; with Auto Rotation that "
                    "leaves the default (User), which stops rotating when the phone is locked")
    elif behavior != AUTO_ROTATION_SENSOR:
        report.fail(f"player: androidAutoRotationBehavior is {behavior:g}, expected "
                    f"{AUTO_ROTATION_SENSOR} (Sensor): with User, a phone whose rotation lock is on "
                    "is shown the game in whatever orientation the lock allows")

    for name, wanted in (("m_ShowUnitySplashScreen", 0), ("m_ShowUnitySplashLogo", 0)):
        value = _setting(text, name)
        if value is None:
            report.fail(f"player: {name} is not set, so the splash is whatever the default is")
        elif value != wanted:
            report.fail(f"player: {name} is {value:g}, expected {wanted:g} - the Unity splash would "
                        "screen ahead of the studio intro, which is the first thing the app is "
                        "supposed to show")

    background = _setting(text, "m_SplashScreenBackgroundColor")
    if background is None:
        report.fail("player: m_SplashScreenBackgroundColor is not set, so the frames before the "
                    "first scene are a colour nobody chose")
    elif background > SPLASH_BACKGROUND_MAX:
        report.fail(f"player: m_SplashScreenBackgroundColor is {background:.3f} at its brightest, "
                    f"above the {SPLASH_BACKGROUND_MAX} this project keeps its first frame under")

    boot_path = os.path.join(REPO_ROOT, BOOT_SCENE)
    if not os.path.exists(boot_path):
        report.fail(f"player: {BOOT_SCENE} is missing, so the intro has nothing to hand over to")
        return

    boot = open(boot_path, encoding="utf-8").read()
    match = re.search("^  m_BackGroundColor: \\{r: ([0-9.]+), g: ([0-9.]+), b: ([0-9.]+), a: [0-9.]+\\}$",
                      boot, re.M)
    if match is None:
        report.fail(f"player: {BOOT_SCENE} has no camera background colour, so the frames after "
                    "the intro are whatever the camera was left pointing at")
    else:
        brightest = max(float(match.group(i)) for i in (1, 2, 3))
        if brightest > SPLASH_BACKGROUND_MAX:
            report.fail(f"player: {BOOT_SCENE}'s camera clears to {brightest:.3f} at its brightest, "
                        f"above {SPLASH_BACKGROUND_MAX}: the intro hands over to a bright page "
                        "instead of to black")
        else:
            report.note("player: landscape only, both ways up, driven by the rotation sensor; no "
                        "Unity splash ahead of the intro, and the ground under the first frame is "
                        "black. The built APK's manifest is checked in CI, and the phone is the "
                        "final word")


def check_scene_flow(report: Report) -> None:
    """Check which scene the app starts in, and that the intro can reach the next one.

    This is the part of the game that a device shows first, and it is decided entirely by files that
    no compiler reads: the build order in `EditorBuildSettings.asset`, the GUID that has to match a
    scene's own `.meta`, the component a scene has to contain for the intro to run at all, and the
    scene name the intro hands over to. Every one of those is a silent failure at runtime — a scene
    that is not in the build simply never loads, and a scene name that does not exist throws when
    the intro ends — so they are checked here.
    """
    settings_path = os.path.join(REPO_ROOT, BUILD_SETTINGS)
    if not os.path.exists(settings_path):
        report.fail(f"scene: {BUILD_SETTINGS} is missing; the build would contain no scenes")
        return

    entries = SCENE_ENTRY_RE.findall(open(settings_path, encoding="utf-8").read())
    if not entries:
        report.fail(f"scene: {BUILD_SETTINGS} lists no scenes")
        return

    enabled = [(path, guid) for flag, path, guid in entries if flag == "1"]
    disabled = [path for flag, path, guid in entries if flag != "1"]
    for path, guid in enabled:
        asset = os.path.join(REPO_ROOT, path)
        if not os.path.exists(asset):
            report.fail(f"scene: '{path}' is in the build order but does not exist")
            continue
        meta = asset + ".meta"
        if not os.path.exists(meta):
            report.fail(f"scene: '{path}' has no .meta, so its guid cannot be resolved")
            continue
        declared = GUID_DECL_RE.search(open(meta, encoding="utf-8").read())
        if declared is None or declared.group(1) != guid:
            report.fail(
                f"scene: '{path}' is listed with guid {guid}, but its .meta declares "
                f"{declared.group(1) if declared else 'nothing'}. Unity finds a scene by guid, so "
                "it would load the wrong scene or none at all")

    if disabled:
        report.note(f"scene: {len(disabled)} scene(s) in the build order are disabled: "
                    f"{', '.join(disabled)}")

    # The studio intro is the app's first screen. If the project has one, it has to be first, or the
    # game boots into the region and the intro is never seen.
    intro_present = os.path.exists(os.path.join(REPO_ROOT, INTRO_SCENE))
    if intro_present:
        if not enabled or enabled[0][0] != INTRO_SCENE:
            first = enabled[0][0] if enabled else "nothing"
            report.fail(
                f"scene: the studio intro ('{INTRO_SCENE}') is not the first enabled scene in the "
                f"build order; '{first}' is. The first scene is what the app shows.")

        # ... and the scene has to contain the component, or it is a black screen with a camera.
        script_rel = INTRO_SCRIPT
        script_guid = None
        if os.path.exists(os.path.join(REPO_ROOT, script_rel + ".meta")):
            found = GUID_DECL_RE.search(
                open(os.path.join(REPO_ROOT, script_rel + ".meta"), encoding="utf-8").read())
            script_guid = found.group(1) if found else None
        if script_guid is None:
            report.fail(f"scene: {script_rel} has no .meta guid, so nothing can reference it")
        else:
            scene_text = open(os.path.join(REPO_ROOT, INTRO_SCENE), encoding="utf-8").read()
            if f"guid: {script_guid}" not in scene_text:
                report.fail(
                    f"scene: '{INTRO_SCENE}' does not contain a component of {script_rel}; the "
                    "scene would load, show black, and never hand over")

            # The app's first frame is black, and that is a property of the scene's camera. A
            # camera left on Skybox clear flags draws the project's skybox (or the pipeline default)
            # behind the mark, so "the logo on black" would be a claim about settings rather than
            # about the data. The mark is white on transparent, so a non-black background would also
            # show its transparent parts as a grey rectangle.
            cameras = []
            for document in re.split(r"^--- ", scene_text, flags=re.M):
                if "!u!20 &" not in document or "\nCamera:\n" not in document:
                    continue
                owner = re.search(r"m_GameObject: \{fileID: (\d+)\}", document)
                on = re.search(r"^  m_Enabled: (\d+)$", document, re.M)
                clear = re.search(r"^  m_ClearFlags: (\d+)$", document, re.M)
                colour = re.search(
                    r"^  m_BackGroundColor: \{r: ([-\d.]+), g: ([-\d.]+), b: ([-\d.]+)",
                    document, re.M)
                cameras.append((owner.group(1) if owner else "",
                                on.group(1) if on else "",
                                clear.group(1) if clear else "",
                                [float(v) for v in colour.groups()] if colour else None))

            tagged = {found.group(1) for document in re.split(r"^--- ", scene_text, flags=re.M)
                      if "!u!1 &" in document and "m_TagString: MainCamera" in document
                      for found in [re.search(r"!u!1 &(\d+)", document)] if found}

            main_cameras = [camera for camera in cameras if camera[0] in tagged]
            if not main_cameras:
                report.fail(
                    f"scene: '{INTRO_SCENE}' has no camera tagged MainCamera, so the intro cannot "
                    "draw anything and would hand over without being seen")
            for _owner, on, clear, colour in main_cameras:
                if on != "1":
                    report.fail(
                        f"scene: the studio intro's MainCamera is disabled, so the screen stays at "
                        "whatever the last scene left behind")
                elif clear != "2":
                    report.fail(
                        f"scene: the studio intro's camera clears to clear-flags '{clear or 'unset'}' "
                        "instead of Solid Color, so the first frame is the skybox rather than the "
                        "black screen the intro is drawn on")
                elif colour is None or any(channel > 0.002 for channel in colour):
                    report.fail(
                        "scene: the studio intro's camera background is not black, so the "
                        "transparent parts of the mark would show as a grey rectangle")

            # The hand-over target has to be a scene the build contains, or the intro ends in a
            # thrown SceneManager exception on the device.
            found = re.search(r"^  _nextScene: (.+)$", scene_text, re.M)
            if found is None:
                found = re.search(r'_nextScene\s*=\s*"([^"]*)"',
                                  open(os.path.join(REPO_ROOT, script_rel), encoding="utf-8").read())
            target = found.group(1).strip() if found else ""
            names = {os.path.basename(path)[:-len(".unity")] for path, _guid in enabled}
            if not target:
                report.fail("scene: the studio intro names no scene to hand over to")
            elif target not in names:
                report.fail(
                    f"scene: the studio intro hands over to '{target}', which is not an enabled "
                    f"scene in the build order (enabled: {', '.join(sorted(names))}). "
                    "SceneManager would throw when the intro ends")
            else:
                report.note(f"scene: the studio intro hands over to '{target}', which the build "
                            "contains")

    # Brand textures: the mark the intro draws, and how it must be imported.
    brand_path = os.path.join(REPO_ROOT, BRAND_DIR)
    marks = []
    if os.path.isdir(brand_path):
        marks = sorted(f for f in os.listdir(brand_path) if f.lower().endswith(IMAGE_EXTENSIONS))

    if not marks:
        report.warn(
            f"brand: '{BRAND_DIR}' holds no image yet, so the studio intro has nothing to draw and "
            "hands straight over. Drop the mark in and this check starts enforcing its import "
            "settings.")
    else:
        script_text = (open(os.path.join(REPO_ROOT, script_rel), encoding="utf-8").read()
                       if os.path.exists(os.path.join(REPO_ROOT, script_rel)) else "")
        for name in marks:
            meta_path = os.path.join(brand_path, name + ".meta")
            if not os.path.exists(meta_path):
                report.fail(f"brand: '{BRAND_DIR}/{name}' has no .meta file")
                continue
            meta = open(meta_path, encoding="utf-8").read()
            if "TextureImporter:" not in meta:
                report.fail(f"brand: '{BRAND_DIR}/{name}' is not imported by a TextureImporter; "
                            "regenerate its .meta with tools/verify/gen_meta.py")
                continue
            for field, expected in REQUIRED_TEXTURE_SETTINGS.items():
                if not re.search(rf"^\s*{field}: {expected}\s*$", meta, re.M):
                    report.fail(
                        f"brand: '{BRAND_DIR}/{name}' is imported with {field} set to something "
                        f"other than {expected}; the studio intro draws the mark from its alpha "
                        "channel and needs it imported as a readable, uncompressed sprite")
            resource_path = "Brand/" + os.path.splitext(name)[0]
            # The scene serializes the path on the component, so either place may be the one that
            # names the file; what must not happen is a brand image nothing draws.
            scene_text = (open(os.path.join(REPO_ROOT, INTRO_SCENE), encoding="utf-8").read()
                          if os.path.exists(os.path.join(REPO_ROOT, INTRO_SCENE)) else "")
            if script_text and resource_path not in script_text and resource_path not in scene_text:
                report.fail(
                    f"brand: '{resource_path}' is in Resources but the studio intro does not name "
                    "it, so the mark in the repository is not the mark on screen")
        report.note(f"brand: {len(marks)} mark(s) imported as readable, uncompressed sprites")

    # Any other image in the project still has to be imported as an image.
    for root, _dirs, files in os.walk(ASSETS):
        for f in sorted(files):
            if not f.lower().endswith(IMAGE_EXTENSIONS):
                continue
            meta_path = os.path.join(root, f + ".meta")
            rel = os.path.relpath(os.path.join(root, f), REPO_ROOT).replace(os.sep, "/")
            if not os.path.exists(meta_path):
                continue          # check_meta already reports the missing .meta
            if "TextureImporter:" not in open(meta_path, encoding="utf-8").read():
                report.fail(f"brand: '{rel}' is not imported by a TextureImporter; "
                            "regenerate its .meta with tools/verify/gen_meta.py")


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
    check_asset_fields(report, project)
    check_symbols(report, project)
    check_project_names(report, project)
    check_engine_usings(report, project)
    check_duplicate_members(report, project)
    check_overrides(report, project)
    check_nested_member_scope(report, project)
    check_engine_names(report, project)
    check_lints(report, project)
    check_namespace_hygiene(report, project)
    check_scene_flow(report)
    check_player_settings(report)

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
