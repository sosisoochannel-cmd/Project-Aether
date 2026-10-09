"""Traversal solver for Aether level data.

This module is the independent implementation of the traversal model that
`Aether.Data.Levels.LevelTraversalSolver` implements for the level pipeline. Two
implementations exist on purpose: the C# one is what the build and the bake tool use, and this
one is what the static gate runs, where it can actually be executed and its numbers reported.
They read the same numbers: the player's capabilities are parsed straight out of
`PlayerTuningData.cs`, so neither implementation carries its own copy of the tuning.

What the model is
-----------------
A level is a grid of one-unit tiles. The player is a box `BodyWidth` x `BodyHeight` that moves
under the tuning in `PlayerTuningData`. The solver enumerates every place the player can stand,
simulates representative *control sequences* between those places, and then searches the
resulting graph.

Why a fixed set of control sequences
------------------------------------
A full model would have to search continuous input over continuous time. Instead the solver
simulates a fixed, documented set of sequences that span the player's actual control authority:

* approach speed   -- full run speed, or half speed
* jump strength    -- full jump, or a jump cut 0.10 s after take-off
* horizontal input -- hold direction, brake to a stop 0.15 s in, or start from a standstill

That is 12 sequences per direction, plus walking off an edge without jumping. The model is
conservative by construction in one important way: anything it cannot prove is reported as
`unreachable`, never as reachable. It is optimistic in exactly one way, and that is deliberate:
it credits the *best* sequence available, because the question the level asks is "can the player
get there at all".

The dodge is deliberately not credited as extra distance: the shipped tuning sets
`_allowAirDodge = false`, so the dodge cannot extend a jump. If that changes, this model and the
C# one must change with it.
"""

from __future__ import annotations

import math
import os
import re
from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

# ---------------------------------------------------------------------------------------
# level data
# ---------------------------------------------------------------------------------------

KIND_MAP = {
    "Empty": 0,
    "Ground": 1,
    "DeepRock": 2,
    "Platform": 3,
    "CanopyBack": 4,
    "CanopyFront": 5,
}

SOLID = {1, 2, 3}


def is_solid(kind: int) -> bool:
    return kind in SOLID


@dataclass
class LevelEntity:
    kind: str
    id: str
    x: int
    y: int
    attrs: Dict[str, str] = field(default_factory=dict)


@dataclass
class ReachClaim:
    from_id: str
    to_id: str
    must_walk: bool = False
    one_way: bool = False
    line: int = 0


@dataclass
class Level:
    path: str
    meta: Dict[str, str]
    legend: Dict[str, int]
    tiles: List[List[int]]
    entities: List[LevelEntity]
    claims: List[ReachClaim]

    @property
    def width(self) -> int:
        return len(self.tiles[0]) if self.tiles else 0

    @property
    def height(self) -> int:
        return len(self.tiles)

    def kind_at(self, col: int, row: int) -> int:
        if 0 <= col < self.width and 0 <= row < self.height:
            return self.tiles[row][col]
        return 0  # outside the map is empty: the player falls out and dies

    def solid_at(self, col: int, row: int) -> bool:
        return is_solid(self.kind_at(col, row))

    def entity(self, entity_id: str) -> Optional[LevelEntity]:
        for e in self.entities:
            if e.id == entity_id:
                return e
        return None

    def tile_size(self) -> float:
        try:
            return float(self.meta.get("tile_size", "1"))
        except ValueError:
            return 1.0


class LevelParseError(Exception):
    def __init__(self, path: str, line_no: int, message: str):
        super().__init__(f"{path}:{line_no}: {message}")
        self.path = path
        self.line_no = line_no
        self.message = message


def parse_level(path: str) -> Level:
    """Parse a .level.txt file. Every malformed line is an error: silently skipping a line is
    how a piece of a level disappears without anyone noticing."""
    with open(path, "r", encoding="utf-8") as handle:
        raw = handle.read().split("\n")

    meta: Dict[str, str] = {}
    legend: Dict[str, int] = {}
    tiles: List[List[int]] = []
    entities: List[LevelEntity] = []
    claims: List[ReachClaim] = []

    section = None
    declared_version = None
    seen_header = False
    seen_entities = False
    tiles_done = False

    for index, line in enumerate(raw):
        line_no = index + 1
        stripped = line.strip()

        if stripped.startswith("[") and stripped.endswith("]"):
            section = stripped[1:-1]
            if section == "entities":
                seen_entities = True
            continue

        if stripped == "":
            if section == "tiles":
                next_index = index + 1
                while next_index < len(raw) and raw[next_index].strip() == "":
                    next_index += 1
                next_is_section = (
                    next_index < len(raw)
                    and raw[next_index].strip().startswith("[")
                    and raw[next_index].strip().endswith("]")
                )
                if next_index < len(raw) and not next_is_section:
                    raise LevelParseError(path, line_no,
                                          "tile row is empty; use tile characters for empty cells")
            continue

        if section == "tiles":
            # Inside the tile block nothing is a comment: '#' can be remapped by the legend,
            # so treating it as a comment here would corrupt maps whose legend uses it.
            tiles.append(_parse_row(path, line_no, line, legend))
            continue

        if stripped.startswith("#") and section != "legend":
            # Comments are allowed between sections, but not inside [legend]: '#' is a legal
            # legend key, and treating it as a comment there would drop the ground tile.
            continue

        if not seen_header:
            parts = stripped.split()
            if len(parts) != 2 or parts[0] != "aether-level":
                raise LevelParseError(path, line_no, f"expected 'aether-level <version>', got '{stripped}'")
            try:
                declared_version = int(parts[1])
            except ValueError:
                raise LevelParseError(path, line_no, f"level version is not a number: '{parts[1]}'")
            if declared_version != 1:
                raise LevelParseError(path, line_no,
                                      f"level format version {declared_version} is not supported by this tool")
            seen_header = True
            continue

        if section == "legend":
            # The legend key is a single character and one of the legal keys is '=' itself, so
            # this cannot be split on the first '='. Everything after the key must start with '='.
            key = stripped[0]
            rest = stripped[1:].lstrip()
            if not rest.startswith("="):
                raise LevelParseError(path, line_no, f"expected '<char> = <Kind>', got '{stripped}'")
            value = rest[1:].strip()
            if value not in KIND_MAP:
                raise LevelParseError(path, line_no,
                                      f"unknown tile kind '{value}'; known kinds: {', '.join(sorted(KIND_MAP))}")
            if key in legend:
                raise LevelParseError(path, line_no,
                                      f"legend symbol '{key}' is declared more than once")
            legend[key] = KIND_MAP[value]
            continue

        if section == "meta":
            if "=" not in stripped:
                raise LevelParseError(path, line_no, f"expected 'key = value', got '{stripped}'")
            key, _, value = stripped.partition("=")
            key = key.strip()
            value = value.strip()
            if key not in META_ATTRIBUTES:
                raise LevelParseError(path, line_no, f"unknown metadata key '{key}'")
            if key in meta:
                raise LevelParseError(path, line_no, f"metadata key '{key}' is declared more than once")
            meta[key] = value
            continue

        if section == "entities":
            entities.append(_parse_entity(path, line_no, stripped))
            continue

        if section == "validation":
            claims.append(_parse_claim(path, line_no, stripped))
            continue

        if tiles_done or section is None:
            if not tiles:
                raise LevelParseError(path, line_no, f"line appears before any section: '{stripped}'")
        raise LevelParseError(path, line_no, f"line is not inside a known section: '{stripped}'")

    if not seen_header:
        raise LevelParseError(path, 1, "missing 'aether-level 1' header")
    if not tiles:
        raise LevelParseError(path, 1, "the [tiles] section is empty")
    if not seen_entities:
        raise LevelParseError(path, 1, "missing the [entities] section")
    if "id" not in meta:
        raise LevelParseError(path, 1, "the [meta] section must declare an id")
    if not legend:
        raise LevelParseError(path, 1, "the [legend] section is empty")

    width = len(tiles[0])
    for i, row in enumerate(tiles):
        if len(row) != width:
            raise LevelParseError(path, 1, f"tile row {i} has {len(row)} columns, expected {width}")

    return Level(path=path, meta=meta, legend=legend, tiles=tiles, entities=entities, claims=claims)


def _parse_row(path: str, line_no: int, line: str, legend: Dict[str, int]) -> List[int]:
    row = []
    for ch in line.rstrip("\r"):
        if ch not in legend:
            raise LevelParseError(path, line_no, f"character '{ch}' is not in the [legend]")
        row.append(legend[ch])
    return row


ENTITY_KINDS = {
    "player_start": "PlayerStart",
    "checkpoint": "Checkpoint",
    "enemy": "Enemy",
    "discovery": "Discovery",
    "story": "StoryMarker",
    "exit": "Exit",
    "anchor": "Anchor",
}

ENTITY_ATTRIBUTES = {
    "id", "x", "y", "type", "kind", "flag", "note", "patrol", "respawn",
}

META_ATTRIBUTES = {"id", "display_name", "region", "tile_size"}


def _parse_entity(path: str, line_no: int, text: str) -> LevelEntity:
    key, _, rest = text.partition("=")
    key = key.strip()
    if key not in ENTITY_KINDS:
        raise LevelParseError(path, line_no,
                              f"unknown entity kind '{key}'; known kinds: {', '.join(sorted(ENTITY_KINDS))}")
    attrs: Dict[str, str] = {}
    for piece in rest.split(","):
        piece = piece.strip()
        if not piece:
            raise LevelParseError(path, line_no, f"empty attribute in '{text}'")
        if "=" not in piece:
            raise LevelParseError(path, line_no, f"attribute '{piece}' is not 'key=value'")
        k, _, v = piece.partition("=")
        k, v = k.strip(), v.strip()
        if k in attrs:
            raise LevelParseError(path, line_no, f"attribute '{k}' given twice")
        attrs[k] = v

    for attribute in attrs:
        if attribute not in ENTITY_ATTRIBUTES:
            raise LevelParseError(path, line_no,
                                  f"{key} has unknown attribute '{attribute}'")

    if "id" not in attrs:
        raise LevelParseError(path, line_no, f"{key} has no id")
    for coord in ("x", "y"):
        if coord not in attrs:
            raise LevelParseError(path, line_no, f"{key} '{attrs['id']}' has no '{coord}'")
    try:
        x = int(attrs["x"])
        y = int(attrs["y"])
    except ValueError:
        raise LevelParseError(path, line_no, f"{key} '{attrs['id']}' has a non-integer position")

    return LevelEntity(kind=ENTITY_KINDS[key], id=attrs["id"], x=x, y=y, attrs=attrs)


def _parse_claim(path: str, line_no: int, text: str) -> ReachClaim:
    key, _, rest = text.partition("=")
    if key.strip() != "reach":
        raise LevelParseError(path, line_no, f"unknown validation entry '{key.strip()}'")
    pieces = [p.strip() for p in rest.split(",") if p.strip()]
    if not pieces:
        raise LevelParseError(path, line_no, "reach entry is empty")
    link = pieces[0]
    if "->" not in link:
        raise LevelParseError(path, line_no, f"reach entry needs 'from -> to', got '{link}'")
    src, _, dst = link.partition("->")
    src, dst = src.strip(), dst.strip()
    if not src or not dst:
        raise LevelParseError(path, line_no, f"reach entry has an empty endpoint: '{link}'")
    claim = ReachClaim(from_id=src, to_id=dst, line=line_no)
    for flag in pieces[1:]:
        if flag == "must=walk":
            claim.must_walk = True
        elif flag == "oneway":
            claim.one_way = True
        else:
            raise LevelParseError(path, line_no, f"unknown reach flag '{flag}'")
    return claim


# ---------------------------------------------------------------------------------------
# player capabilities, read from the shipped tuning asset source
# ---------------------------------------------------------------------------------------

@dataclass
class Tuning:
    body_width: float
    body_height: float
    run_speed: float
    ground_acceleration: float
    air_control_multiplier: float
    air_deceleration_multiplier: float
    jump_height: float
    jump_time_to_apex: float
    rise_gravity_multiplier: float
    fall_gravity_multiplier: float
    max_fall_speed: float
    jump_cut_multiplier: float
    coyote_time: float
    dodge_speed: float
    allow_air_dodge: bool

    @property
    def jump_velocity(self) -> float:
        return (2.0 * self.jump_height) / max(0.05, self.jump_time_to_apex)

    @property
    def rise_gravity(self) -> float:
        return ((2.0 * self.jump_height) / (self.jump_time_to_apex ** 2)) * self.rise_gravity_multiplier

    @property
    def fall_gravity(self) -> float:
        return self.rise_gravity * self.fall_gravity_multiplier

    @property
    def air_acceleration(self) -> float:
        return self.ground_acceleration * self.air_control_multiplier

    @property
    def air_deceleration(self) -> float:
        return self.ground_acceleration * self.air_deceleration_multiplier


_TUNING_FIELDS = {
    "_bodyWidth": ("body_width", float),
    "_bodyHeight": ("body_height", float),
    "_runSpeed": ("run_speed", float),
    "_groundAcceleration": ("ground_acceleration", float),
    "_airControlMultiplier": ("air_control_multiplier", float),
    "_airDecelerationMultiplier": ("air_deceleration_multiplier", float),
    "_jumpHeight": ("jump_height", float),
    "_jumpTimeToApex": ("jump_time_to_apex", float),
    "_riseGravityMultiplier": ("rise_gravity_multiplier", float),
    "_fallGravityMultiplier": ("fall_gravity_multiplier", float),
    "_maxFallSpeed": ("max_fall_speed", float),
    "_jumpCutMultiplier": ("jump_cut_multiplier", float),
    "_coyoteTime": ("coyote_time", float),
    "_dodgeSpeed": ("dodge_speed", float),
    "_allowAirDodge": ("allow_air_dodge", bool),
}


def read_tuning(path: str) -> Tuning:
    """Read serialized values out of PlayerTuningData.cs.

    Parsing source is not elegant, but it is the only way for this tool to use the same numbers
    the game does. A field that has been renamed is reported instead of silently defaulting.
    """
    with open(path, "r", encoding="utf-8") as handle:
        source = handle.read()
    values: Dict[str, object] = {}
    for field_name, (attr, cast) in _TUNING_FIELDS.items():
        match = re.search(
            r"private\s+[\w<>\[\]\.]+\s+" + re.escape(field_name) + r"\s*=\s*([^;]+);", source)
        if not match:
            raise LevelParseError(path, 0, f"tuning field '{field_name}' not found; the solver must not guess it")
        literal = match.group(1).strip()
        if cast is bool:
            values[attr] = literal.lower().startswith("true")
        else:
            literals = re.findall(r"-?\d+(?:\.\d+)?f?", literal)
            if not literals:
                raise LevelParseError(path, 0, f"tuning field '{field_name}' has no numeric literal")
            values[attr] = float(literals[0].rstrip("f"))
    return Tuning(**values)  # type: ignore[arg-type]


# ---------------------------------------------------------------------------------------
# solver
# ---------------------------------------------------------------------------------------

DT = 1.0 / 120.0
MAX_STEPS = 600          # 5 seconds: far longer than any jump this tuning allows
EPS = 1e-4


@dataclass(frozen=True, order=True)
class Node:
    col: int
    row: int

    def __str__(self) -> str:  # pragma: no cover - diagnostics only
        return f"({self.col},{self.row})"


@dataclass
class Edge:
    """One way the player can get from one standable tile to another.

    `span` is the horizontal travel the manoeuvre demands: for a jump, the distance to the near
    edge of the destination tile, because a player whose jump strength is continuous only has to
    *reach* the tile, not land on its centre. `reach` is the farthest the same take-off point can
    manage in the same direction, so `margin` answers the only question that matters for
    fairness: how much slack does this hop leave?
    """

    target: Node
    kind: str          # "walk", "drop", "leap"
    profile: str
    span: float        # travel the level demands, in world units
    rise: float
    reach: float       # farthest the player can come to rest in this direction

    @property
    def jumps(self) -> int:
        return 0 if self.kind == "walk" else 1

    @property
    def margin(self) -> float:
        # Only a leap spends the player's ability. Walking and stepping off a ledge cost
        # nothing, so measuring them against jump reach would produce nonsense numbers.
        if self.kind != "leap" or self.reach <= EPS:
            return 1.0
        return (self.reach - self.span) / self.reach


PROFILES: List[Tuple[str, float, str, str]] = []
for _speed_name, _speed in (("full", 1.0), ("half", 0.5)):
    for _jump in ("full", "cut"):
        for _horiz in ("hold", "brake", "ramp"):
            PROFILES.append((f"{_speed_name}-speed/{_jump}-jump/{_horiz}", _speed, _jump, _horiz))

BRAKE_AT = 0.15
CUT_AT = 0.10

#: A hop that spends more than this fraction of the player's reach is treated as unfair. It is
#: not that the jump is impossible -- it is that the level would break the moment anything about
#: the tuning, the physics or the frame rate changed even slightly.
MARGIN_FLOOR = 0.15

#: How heavily the route search penalises a hop that leaves little slack. Walking to a better
#: take-off point costs distance; this is what makes that trade worthwhile.
FRAGILITY_WEIGHT = 4.0


class Solver:
    """Enumerates where the player can stand and how they can move between those places."""

    def __init__(self, level: Level, tuning: Tuning):
        self.level = level
        self.tuning = tuning
        self._nodes: Optional[List[Node]] = None
        self._node_set: Optional[set] = None
        self._edges: Optional[Dict[Node, List[Edge]]] = None
        self._reach: Dict[Tuple[Node, int], float] = {}

    # -- geometry ------------------------------------------------------------------------

    def feet_y(self, row: int) -> float:
        """World y of the surface a body stands on when it occupies tile (col, row)."""
        return (self.level.height - row) * self.level.tile_size()

    def body_blocked(self, cx: float, feet: float) -> bool:
        """True when the body box at this position touches any solid tile."""
        half = self.tuning.body_width * 0.5
        tile = self.level.tile_size()
        c0 = math.floor((cx - half + EPS) / tile)
        c1 = math.floor((cx + half - EPS) / tile)
        r_bottom = self.level.height - 1 - math.floor((feet + EPS) / tile)
        r_top = self.level.height - 1 - math.floor((feet + self.tuning.body_height - EPS) / tile)
        for col in range(c0, c1 + 1):
            if col < 0 or col >= self.level.width:
                return True   # the map edge is a wall, not a hole
            for row in range(r_top, r_bottom + 1):
                if self.level.solid_at(col, row):
                    return True
        return False

    def has_support(self, cx: float, feet: float) -> bool:
        """True when a solid tile sits directly under the body's feet."""
        tile = self.level.tile_size()
        half = self.tuning.body_width * 0.5
        c0 = math.floor((cx - half + EPS) / tile)
        c1 = math.floor((cx + half - EPS) / tile)
        row = self.level.height - 1 - math.floor((feet - 0.02) / tile)
        for col in range(c0, c1 + 1):
            if self.level.solid_at(col, row):
                return True
        return False

    def snap_surface(self, cx: float, feet: float) -> float:
        """Top surface of the highest solid tile the body has just penetrated."""
        tile = self.level.tile_size()
        half = self.tuning.body_width * 0.5
        c0 = math.floor((cx - half + EPS) / tile)
        c1 = math.floor((cx + half - EPS) / tile)
        r_bottom = self.level.height - 1 - math.floor((feet + EPS) / tile)
        r_top = self.level.height - 1 - math.floor((feet + self.tuning.body_height - EPS) / tile)
        best_row = None
        for col in range(c0, c1 + 1):
            for row in range(r_top, r_bottom + 1):
                if self.level.solid_at(col, row) and (best_row is None or row < best_row):
                    best_row = row
        if best_row is None:
            return feet
        return (self.level.height - best_row) * tile

    def node_of(self, cx: float, feet: float) -> Optional[Node]:
        """Stand node a resting body is on, or None if it is not resting on anything standable."""
        tile = self.level.tile_size()
        col = int(math.floor(cx / tile))
        row = self.level.height - int(round(feet / tile))
        candidate = Node(col, row)
        return candidate if candidate in self.node_set() else None

    def stand_nodes(self) -> List[Node]:
        if self._nodes is None:
            nodes: List[Node] = []
            tile = self.level.tile_size()
            for row in range(1, self.level.height):
                for col in range(self.level.width):
                    if not self.level.solid_at(col, row) or self.level.solid_at(col, row - 1):
                        continue
                    if not self.body_blocked((col + 0.5) * tile, self.feet_y(row)):
                        nodes.append(Node(col, row))
            self._nodes = nodes
        return self._nodes

    def node_set(self) -> set:
        if self._node_set is None:
            self._node_set = set(self.stand_nodes())
        return self._node_set

    # -- walking -------------------------------------------------------------------------

    def can_walk(self, a: Node, b: Node, limit: int = 4) -> bool:
        """True when the player can run from node a to node b without leaving the ground.

        This is a straight geometric test along level ground: the floor must be continuous, the
        headroom must be clear, and the body must not clip anything on the way. It is the
        definition behind every `must=walk` claim in the level data.
        """
        if a.row != b.row:
            return False
        step = 1 if b.col > a.col else -1
        if abs(b.col - a.col) > limit:
            return False
        tile = self.level.tile_size()
        feet = self.feet_y(a.row)
        for col in range(a.col, b.col + step, step):
            if not self.level.solid_at(col, a.row):
                return False
            if self.level.solid_at(col, a.row - 1):
                return False
        for i in range(0, 21):
            t = i / 20.0
            cx = ((a.col + 0.5) + t * (b.col - a.col)) * tile
            if self.body_blocked(cx, feet):
                return False
        return True

    # -- simulation ----------------------------------------------------------------------

    def run_sequence(self, start: Node, direction: int, speed_scale: float, jump: str, horiz: str,
                     jumped: bool) -> Tuple[Optional[Node], float, float, Optional[float], str]:
        """Simulate one control sequence from a standing start.

        Returns (landing node, span, rise, feet at the end, note). `rise` is negative for a drop
        and `feet` is None when the sequence left the map, which is how a fall out of the level
        is distinguished from a landing.
        """
        tile = self.level.tile_size()
        t = self.tuning
        cx = (start.col + 0.5) * tile
        feet = self.feet_y(start.row)
        start_cx, start_feet = cx, feet
        # A jump leaves the ground on the first step; a walk starts out standing on it.
        grounded = not jumped

        if jumped:
            vy = t.jump_velocity
            vx = 0.0 if horiz == "ramp" else t.run_speed * speed_scale * direction
        else:
            vy = 0.0
            vx = t.run_speed * direction

        cut_used = False
        time = 0.0

        for _ in range(MAX_STEPS):
            time += DT

            if jumped:
                if horiz == "ramp" and time < 0.15:
                    vx = _approach(vx, 0.0, t.air_acceleration * DT)
                elif horiz == "ramp":
                    vx = _approach(vx, t.run_speed * speed_scale * direction, t.air_acceleration * DT)
                elif horiz == "brake" and time >= BRAKE_AT:
                    vx = _approach(vx, 0.0, t.air_deceleration * DT)
                if jump == "cut" and not cut_used and time >= CUT_AT and vy > 0.0:
                    vy *= t.jump_cut_multiplier
                    cut_used = True

            # horizontal
            moved = cx + vx * DT
            if self.body_blocked(moved, feet):
                vx = 0.0                      # stopped by a wall; keep whatever height we have
            else:
                cx = moved

            # vertical
            if grounded:
                grounded = self.has_support(cx, feet)
                if grounded:
                    continue                  # still running along the ground
                vy = 0.0                      # just ran off an edge
            gravity = t.rise_gravity if vy > 0.0 else t.fall_gravity
            vy = max(vy - gravity * DT, -t.max_fall_speed)
            feet += vy * DT
            if self.body_blocked(cx, feet):
                if vy > 0.0:                  # head bump
                    feet -= vy * DT
                    vy = 0.0
                else:                         # landed
                    feet = self.snap_surface(cx, feet)
                    vy = 0.0
                    grounded = True
                    break
            if feet < -float(self.level.height):
                return None, abs(cx - start_cx), 0.0, None, "left the level through the floor"

        note = "" if grounded else "sequence did not come to rest within 5s"
        landing = self.node_of(cx, feet) if grounded else None
        if landing is None and grounded:
            note = "came to rest somewhere that is not a standable tile"
        span = abs(cx - start_cx)
        rise = feet - start_feet
        return landing, span, rise, feet, note

    def max_reach(self, node: Node, direction: int) -> float:
        """Farthest distance from this tile at which the player can come to rest, in this
        direction, over every modelled control sequence. This is the number every margin in the
        report is measured against."""
        key = (node, direction)
        if key in self._reach:
            return self._reach[key]
        best = 0.0
        for profile in PROFILES:
            _, speed_scale, jump, horiz = profile
            landing, span, _rise, _feet, _note = self.run_sequence(node, direction, speed_scale, jump, horiz, True)
            if landing is not None and (landing.col - node.col) * direction > 0:
                best = max(best, span)
        self._reach[key] = best
        return best

    def edges(self) -> Dict[Node, List[Edge]]:
        if self._edges is not None:
            return self._edges
        graph: Dict[Node, List[Edge]] = {n: [] for n in self.stand_nodes()}
        nodes = self.node_set()

        for node in graph:
            for direction in (-1, 1):
                reach = self.max_reach(node, direction)

                # walking: level ground, no jump input, no gap
                for step in (1, 2, 3, 4):
                    target = Node(node.col + direction * step, node.row)
                    if target in nodes and self.can_walk(node, target):
                        graph[node].append(Edge(target, "walk", "walk", abs(step) * self.level.tile_size(), 0.0, reach))

                # stepping or dropping off an edge, still no jump input. A descent is required:
                # if the sequence ends level with the take-off it was just running, and that is
                # already covered by the walking edges above.
                landing, span, rise, _feet, _note = self.run_sequence(
                    node, direction, 1.0, "full", "hold", jumped=False)
                if (landing is not None and landing != node and (landing.col - node.col) * direction > 0
                        and rise < -EPS):
                    # A one-tile step down is still a walk: it needs no jump input and no
                    # courage. Anything deeper is a drop, and is scored as leaving the ground.
                    kind = "walk" if -rise <= self.level.tile_size() + EPS else "drop"
                    graph[node].append(Edge(landing, kind, "walk-off", abs(span), rise, reach))

                # jumps
                for profile in PROFILES:
                    _, speed_scale, jump, horiz = profile
                    landing, span, rise, _feet, _note = self.run_sequence(
                        node, direction, speed_scale, jump, horiz, jumped=True)
                    if landing is None or landing == node:
                        continue
                    if (landing.col - node.col) * direction <= 0:
                        continue                    # a jump that goes the wrong way is not a link
                    # What the level demands is reaching the destination tile at all, so the
                    # demanded travel is the distance to that tile's near edge.
                    near_edge = landing.col if direction > 0 else landing.col + 1
                    needed = abs(near_edge - (node.col + 0.5) * self.level.tile_size())
                    graph[node].append(Edge(landing, "leap", profile[0], needed, rise, reach))

        self._edges = graph
        return graph

    # -- search --------------------------------------------------------------------------

    @staticmethod
    def _cost(edge: Edge) -> Tuple[int, int, float]:
        """Lexicographic route cost: unfair hops first, then jump count, then weighted distance.

        Counting unfair hops before jumps is deliberate. A route made of two comfortable jumps
        is what a player will actually take; a route with one jump that spends all of the
        player's reach is not, and reporting *that* as the route would hide the very thing the
        solver exists to find.

        Distance is weighted by how little slack the hop leaves, so that walking two tiles to a
        better take-off point beats jumping from where the player happens to be standing. Without
        that weight the two options tie on distance, and the solver would report a marginal jump
        as the level's demand when a comfortable one is right there.
        """
        unfair = 1 if (edge.kind == "leap" and edge.margin < MARGIN_FLOOR) else 0
        weight = 1.0 + FRAGILITY_WEIGHT * (1.0 - edge.margin)
        return unfair, edge.jumps, edge.span * weight

    def search(self, start: Node, goal: Node, walk_only: bool) -> Optional[List[Edge]]:
        """Cheapest route, or the route that uses no jump input at all when walk_only is set."""
        import heapq
        graph = self.edges()
        if start not in graph or goal not in graph:
            return None
        worst = (1 << 30, 1 << 30, 1e9)
        best: Dict[Node, Tuple[int, int, float]] = {start: (0, 0, 0.0)}
        queue: List[Tuple[int, int, float, Node]] = [(0, 0, 0.0, start)]
        parent: Dict[Node, Tuple[Node, Edge]] = {}
        while queue:
            unfair, jumps, cost, node = heapq.heappop(queue)
            if best.get(node, worst) < (unfair, jumps, cost):
                continue
            if node == goal:
                route: List[Edge] = []
                cur = node
                while cur in parent:
                    prev, edge = parent[cur]
                    route.append(edge)
                    cur = prev
                route.reverse()
                return route

            for edge in graph[node]:
                if walk_only and edge.kind != "walk":
                    continue
                e_unfair, e_jumps, e_cost = self._cost(edge)
                nxt = (unfair + e_unfair, jumps + e_jumps, cost + e_cost)
                if nxt < best.get(edge.target, worst):
                    best[edge.target] = nxt
                    parent[edge.target] = (node, edge)
                    heapq.heappush(queue, (nxt[0], nxt[1], nxt[2], edge.target))
        return None

    def nearest_node(self, col: int, row: int) -> Optional[Node]:
        nodes = self.node_set()
        exact = Node(col, row)
        if exact in nodes:
            return exact
        best, best_d = None, 1 << 30
        for node in nodes:
            d = abs(node.col - col) + abs(node.row - row)
            if d < best_d:
                best, best_d = node, d
        return best

    def enemies_near(self, path: Sequence[Edge], start: Node) -> List[str]:
        """Enemy ids standing on the route. They are combat obstacles, not geometry, so the
        solver reports them instead of letting them block a traversal claim."""
        by_tile = {(e.x, e.y): e.id for e in self.level.entities if e.kind == "Enemy"}
        found: List[str] = []
        current = start
        for edge in path:
            lo, hi = sorted((current.col, edge.target.col))
            for col in range(lo - 1, hi + 2):
                for row in (edge.target.row - 1, edge.target.row, edge.target.row + 1):
                    enemy_id = by_tile.get((col, row))
                    if enemy_id is not None and enemy_id not in found:
                        found.append(enemy_id)
            current = edge.target
        return found


def _approach(current: float, target: float, rate: float) -> float:
    if current < target:
        return min(current + rate, target)
    return max(current - rate, target)


# ---------------------------------------------------------------------------------------
# reports
# ---------------------------------------------------------------------------------------

@dataclass
class ClaimResult:
    claim: ReachClaim
    state: str                 # Reachable / Unreachable / Blocked / RequiresAbility / Ambiguous
    detail: str
    jumps: int = 0
    hops: int = 0
    worst_margin: float = 1.0
    one_way: bool = False
    enemies: List[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return self.state == "Reachable"

    def describe(self) -> str:
        return (f"{self.claim.from_id} -> {self.claim.to_id}: {self.state} "
                f"({self.detail})")


@dataclass
class EnemyKind:
    """One enemy archetype, read from the asset the game loads at runtime."""

    type_id: str
    path: str
    max_health: float
    detection_range: float
    detection_height: float
    move_speed: float
    attack_range: float
    windup: float
    recovery: float
    patrol_distance: int

    # The attack timeline the archetype points at. Without an attack an enemy cannot teach
    # anything, so the tool treats a missing one as a problem rather than as a silent default.
    has_attack: bool = False
    attack_id: str = ""
    attack_startup: float = 0.0
    attack_active: float = 0.0
    attack_recovery: float = 0.0
    attack_damage: float = 0.0

    @property
    def telegraph(self) -> float:
        """How long the player has to read the attack before it can hurt them."""
        return self.windup + self.attack_startup

    @property
    def counter_window(self) -> float:
        """How long the enemy is open after the hit frame ends."""
        return self.attack_recovery + self.recovery


def read_enemy_kinds(root: str) -> Dict[str, EnemyKind]:
    """Parse the enemy definition assets.

    The level reasons about enemies — how far they see, how long they pause — so the numbers have
    to come from the same asset the game reads, exactly like the player's tuning. A hard-coded
    copy here would drift the first time somebody retuned the archetype.
    """
    folder = os.path.join(root, "Assets/Aether/Resources/Content/Enemies")
    kinds: Dict[str, EnemyKind] = {}
    if not os.path.isdir(folder):
        return kinds

    def number(text: str, key: str, default: float) -> float:
        match = re.search(rf"^\s*{re.escape(key)}:\s*(-?[0-9.]+)", text, re.M)
        return float(match.group(1)) if match else default

    # Every asset by guid, so an archetype's attack reference can be followed to its timeline.
    by_guid: Dict[str, str] = {}
    content_root = os.path.join(root, "Assets/Aether/Resources/Content")
    for walk_root, _dirs, files in os.walk(content_root):
        for name in files:
            if not name.endswith(".asset.meta"):
                continue
            found = re.search(r"^guid: ([0-9a-f]{32})$",
                              open(os.path.join(walk_root, name), encoding="utf-8").read(), re.M)
            if found:
                by_guid[found.group(1)] = os.path.join(walk_root, name[:-5])

    for name in sorted(os.listdir(folder)):
        if not name.endswith(".asset"):
            continue
        path = os.path.join(folder, name)
        text = open(path, encoding="utf-8").read()
        type_match = re.search(r"^\s*_typeId:\s*(\S+)", text, re.M)
        if not type_match:
            continue
        type_id = type_match.group(1)

        reference = re.search(r"^\s*_attack:\s*\{fileID: (\d+), guid: ([0-9a-f]{32})", text, re.M)
        attack_text = ""
        has_attack = bool(reference) and reference.group(1) != "0"
        if has_attack:
            attack_path = by_guid.get(reference.group(2))
            if attack_path and os.path.exists(attack_path):
                attack_text = open(attack_path, encoding="utf-8").read()
            else:
                has_attack = False

        kinds[type_id] = EnemyKind(
            type_id=type_id,
            path=os.path.relpath(path, root).replace(os.sep, "/"),
            max_health=number(text, "_maxHealth", 1.0),
            detection_range=number(text, "_detectionRange", 0.0),
            detection_height=number(text, "_detectionHeight", 0.0),
            move_speed=number(text, "_moveSpeed", 0.0),
            attack_range=number(text, "_attackRange", 0.0),
            windup=number(text, "_attackWindupDelay", 0.0),
            recovery=number(text, "_recoveryPause", 0.0),
            patrol_distance=int(number(text, "_patrolDistance", 0.0)),
            has_attack=has_attack,
            attack_id=(re.search(r"^\s*_id:\s*(\S+)", attack_text, re.M).group(1)
                       if attack_text else ""),
            attack_startup=number(attack_text, "_startup", 0.0),
            attack_active=number(attack_text, "_active", 0.0),
            attack_recovery=number(attack_text, "_recovery", 0.0),
            attack_damage=number(attack_text, "_damage", 0.0),
        )
    return kinds


def _seen_by(entity: LevelEntity, kind: EnemyKind, col: int, row: float) -> bool:
    """Would this enemy notice a player standing at (col, row)?"""
    dx = abs(col - entity.x)
    dy = abs(row - entity.y)
    return dx <= kind.detection_range and dy <= kind.detection_height


def check_encounters(level: Level, kinds: Dict[str, EnemyKind]) -> Tuple[List[str], List[str]]:
    """Encounter pacing and fairness, using the enemies' real senses.

    Returns (problems, notes). A problem is something that makes the level unfair or unreadable; a
    note is something true about the pacing that a designer should see but that no rule can judge.
    """
    problems: List[str] = []
    notes: List[str] = []

    enemies = [e for e in level.entities if e.kind == "Enemy"]
    if not enemies:
        return problems, notes

    def kind_of(entity: LevelEntity) -> EnemyKind:
        return kinds.get(entity.attrs.get("type", ""))

    # 1. Two encounters must not be able to see the same tile. A fight the player has just survived
    #    must not hand them straight to the next one.
    for i, a in enumerate(enemies):
        kind_a = kind_of(a)
        if kind_a is None:
            continue
        for b in enemies[i + 1:]:
            kind_b = kind_of(b)
            if kind_b is None:
                continue
            # Tiles both enemies can see, on the ground either of them stands on.
            overlap = []
            for col in range(max(0, min(a.x, b.x) - 12), min(level.width, max(a.x, b.x) + 13)):
                for row in (a.y, b.y):
                    if _seen_by(a, kind_a, col, row) and _seen_by(b, kind_b, col, row):
                        overlap.append((col, row))
            if overlap:
                col, row = overlap[0]
                problems.append(
                    f"encounters '{a.id}' and '{b.id}' both see ({col},{row}): the player can be "
                    f"attacked by the second before the first is over, with no safe ground between")

    # 2. Respawning must not put the player inside somebody's senses.
    for entity in level.entities:
        if entity.kind != "Checkpoint":
            continue
        respawn = entity.attrs.get("respawn")
        if not respawn:
            continue
        try:
            rx, ry = (int(v) for v in respawn.split(":"))
        except ValueError:
            problems.append(f"checkpoint '{entity.id}' has an unreadable respawn '{respawn}'")
            continue

        for enemy in enemies:
            kind = kind_of(enemy)
            if kind is None:
                continue
            if _seen_by(enemy, kind, rx, ry):
                problems.append(
                    f"checkpoint '{entity.id}' respawns the player at ({rx},{ry}), where "
                    f"'{enemy.id}' already has them in sight: dying would be punished twice")

    # 3. The exit must be reachable without fighting, and must read as an arrival.
    exit_entity = next((e for e in level.entities if e.kind == "Exit"), None)
    if exit_entity is not None:
        for enemy in enemies:
            kind = kind_of(enemy)
            if kind is None:
                continue
            if _seen_by(enemy, kind, exit_entity.x, exit_entity.y):
                problems.append(
                    f"the exit at ({exit_entity.x},{exit_entity.y}) is inside '{enemy.id}'s "
                    "senses, so the region ends in a fight the player did not choose")

    # 3b. An enemy with no attack teaches nothing: it can chase, but the player never has to learn
    #     when to dodge, and there is no recovery to punish.
    for enemy in enemies:
        kind = kind_of(enemy)
        if kind is not None and not kind.has_attack:
            problems.append(
                f"enemy '{enemy.id}' uses archetype '{enemy.attrs.get('type')}', which points at no "
                "attack: it can chase but it cannot attack, so the encounter cannot teach its lesson")

    # 3c. The counter-attack window has to be felt. If the enemy is only open for less time than it
    #     spent warning the player, the fight teaches dodging and never teaches punishing, which is
    #     the second half of what the first encounter exists for.
    for enemy in enemies:
        kind = kind_of(enemy)
        if kind is None or not kind.has_attack:
            continue
        if kind.counter_window <= kind.telegraph:
            problems.append(
                f"enemy '{enemy.id}': telegraph {kind.telegraph:.2f}s vs counter window "
                f"{kind.counter_window:.2f}s — the enemy is open for less time than it warned for, "
                "so punishing a miss is not something the player can learn here")

    # 4. Escalation, reported honestly. If every encounter uses one archetype with one set of
    #    numbers, then difficulty cannot be coming from stats — it is coming from placement, which
    #    is the design the milestone asks for. If that ever stops being true, say so.
    used = {}
    for enemy in enemies:
        name = enemy.attrs.get("type", "?")
        used[name] = used.get(name, 0) + 1
    if len(used) == 1:
        type_id = next(iter(used))
        kind = kinds.get(type_id)
        if kind is not None:
            notes.append(
                f"all {used[type_id]} encounters use '{type_id}' with identical numbers "
                f"(hp {kind.max_health:g}, speed {kind.move_speed:g}, sight "
                f"{kind.detection_range:g}x{kind.detection_height:g}): escalation is positional, "
                "not statistical")
    else:
        notes.append("encounters use more than one archetype: " +
                     ", ".join(f"{name} x{count}" for name, count in sorted(used.items())))

    # 5. Per-encounter context: what the fight actually is.
    for index, enemy in enumerate(enemies):
        kind = kind_of(enemy)
        if kind is None:
            continue
        patrol = enemy.attrs.get("patrol", "0")
        previous = enemies[index - 1] if index > 0 else None
        spacing = f"{abs(enemy.x - previous.x)} tiles from '{previous.id}'" if previous else \
            "the first encounter"
        if kind.has_attack:
            notes.append(
                f"'{enemy.id}' at ({enemy.x},{enemy.y}) patrol {patrol}: {spacing}; attack "
                f"'{kind.attack_id}' — telegraph {kind.telegraph:.2f}s "
                f"({kind.windup:g}s approach + {kind.attack_startup:g}s wind-up), hit frame "
                f"{kind.attack_active:g}s, counter window {kind.counter_window:.2f}s, "
                f"{kind.attack_damage:g} damage")
        else:
            notes.append(
                f"'{enemy.id}' at ({enemy.x},{enemy.y}) patrol {patrol}: {spacing}, no attack")

    return problems, notes


def check_secret(level: Level, solver: Solver) -> Tuple[List[str], List[str]]:
    """Whether the level's first secret behaves like a secret.

    Two things are decidable: it must not sit on the route to the exit, and getting to it must
    cost something the main path does not — otherwise it is a thing the player passes anyway.
    Whether it *feels* hidden needs eyes, and is reported as such.
    """
    problems: List[str] = []
    notes: List[str] = []

    discoveries = [e for e in level.entities if e.kind == "Discovery"]
    if not discoveries:
        return problems, notes

    start = next((e for e in level.entities if e.kind == "PlayerStart"), None)
    exit_entity = next((e for e in level.entities if e.kind == "Exit"), None)
    if start is None or exit_entity is None:
        return problems, notes

    start_node = solver.nearest_node(start.x, start.y)
    exit_node = solver.nearest_node(exit_entity.x, exit_entity.y)
    if start_node is None or exit_node is None:
        return problems, notes

    exit_route = solver.search(start_node, exit_node, walk_only=False)
    if exit_route is not None:
        travelled = {start_node} | {edge.target for edge in exit_route}
    else:
        travelled = set()

    for discovery in discoveries:
        node = solver.nearest_node(discovery.x, discovery.y)
        if node is None:
            problems.append(f"discovery '{discovery.id}' is not on a tile the player can stand on")
            continue

        route = solver.search(start_node, node, walk_only=False)
        if route is None:
            problems.append(
                f"discovery '{discovery.id}' at ({discovery.x},{discovery.y}) cannot be reached "
                "at all from the start")
            continue

        jumps = sum(edge.jumps for edge in route)
        if jumps == 0:
            # The only thing standing between the player and this secret is the walk itself, so it
            # is not hidden — it is on the way, and the region's first discovery is supposed to
            # reward looking rather than passing through.
            problems.append(
                f"discovery '{discovery.id}' at ({discovery.x},{discovery.y}) can be reached "
                "without jumping at all: it is not hidden, it is on the floor the player already "
                "walks along")
            continue

        if node in travelled:
            problems.append(
                f"discovery '{discovery.id}' at ({discovery.x},{discovery.y}) sits on the "
                "cheapest route to the exit, so every player walks over it")
            continue

        notes.append(
            f"'{discovery.id}' at ({discovery.x},{discovery.y}) is off the exit route: "
            f"{len(route)} hop(s) and {jumps} jump(s) of detour, so it is found by exploring "
            "rather than by passing through")
        notes.append(
            f"whether '{discovery.id}' is hidden from the path is a sight-line question this tool "
            "cannot answer: it compares tiles, not what the camera renders. Confirm in the Editor.")

    return problems, notes


def check_claims(level: Level, solver: Solver) -> List[ClaimResult]:
    results: List[ClaimResult] = []
    for claim in level.claims:
        results.append(_check_claim(level, solver, claim))
    return results


def _check_claim(level: Level, solver: Solver, claim: ReachClaim) -> ClaimResult:
    src = level.entity(claim.from_id)
    dst = level.entity(claim.to_id)
    if src is None:
        return ClaimResult(claim, "Blocked", f"'{claim.from_id}' does not exist in this level")
    if dst is None:
        return ClaimResult(claim, "Blocked", f"'{claim.to_id}' does not exist in this level")

    start = solver.nearest_node(src.x, src.y)
    goal = solver.nearest_node(dst.x, dst.y)
    if start is None or goal is None:
        return ClaimResult(claim, "Blocked", "no standable tile anywhere in the level")
    if start.col != src.x or start.row != src.y:
        return ClaimResult(claim, "Blocked",
                           f"'{claim.from_id}' at ({src.x},{src.y}) is not somewhere the player can stand; "
                           f"nearest standable tile is {start}")
    if goal.col != dst.x or goal.row != dst.y:
        return ClaimResult(claim, "Blocked",
                           f"'{claim.to_id}' at ({dst.x},{dst.y}) is not somewhere the player can stand; "
                           f"nearest standable tile is {goal}")

    route = solver.search(start, goal, walk_only=claim.must_walk)
    if route is None:
        any_route = solver.search(start, goal, walk_only=False)
        if claim.must_walk and any_route is not None:
            jumps = sum(e.jumps for e in any_route)
            return ClaimResult(
                claim, "Blocked",
                f"route exists but needs {jumps} jump(s); this link is declared must=walk. "
                f"First jump: {any_route[0].profile} over {any_route[0].span:.2f}",
                jumps=jumps, hops=len(any_route))
        return ClaimResult(claim, "Unreachable",
                           "no sequence of the player's jumps and walks connects these two tiles")

    jumps = sum(e.jumps for e in route)
    leaps = [e for e in route if e.kind == "leap"]
    worst = min((e.margin for e in leaps), default=1.0)
    worst_edge = min(leaps, key=lambda e: e.margin) if leaps else None
    one_way = not _has_return(solver, route, start, goal)
    enemies = solver.enemies_near(route, start)
    unfair = [e for e in leaps if e.margin < MARGIN_FLOOR]

    if one_way and not claim.one_way:
        return ClaimResult(claim, "Ambiguous",
                           "traversable, but there is no way back and the data does not declare oneway",
                           jumps=jumps, hops=len(route), worst_margin=worst, one_way=True,
                           enemies=enemies)
    if unfair:
        return ClaimResult(
            claim, "Ambiguous",
            f"best route needs {len(unfair)} hop(s) that spend more than {1 - MARGIN_FLOOR:.0%} of "
            f"the player's reach; the worst is {worst_edge.profile} spanning {worst_edge.span:.2f} "
            f"of {worst_edge.reach:.2f} ({worst:.0%} margin left). The level must not depend on the "
            f"player being perfect",
            jumps=jumps, hops=len(route), worst_margin=worst, one_way=one_way, enemies=enemies)

    if jumps == 0:
        detail = f"walk only: {len(route)} step(s) of ground, no jump input needed"
    else:
        detail = (f"{len(route)} hop(s), {jumps} jump(s); tightest jump must clear "
                  f"{worst_edge.span:.2f} of the {worst_edge.reach:.2f} available, leaving "
                  f"{worst:.0%} slack ({worst_edge.profile})")
    if one_way:
        detail += "; declared one-way"
    if enemies:
        detail += f"; {len(enemies)} enemy on the route ({', '.join(enemies)})"

    return ClaimResult(claim, "Reachable", detail, jumps=jumps, hops=len(route),
                       worst_margin=worst, one_way=one_way, enemies=enemies)


def _has_return(solver: Solver, route: Sequence[Edge], start: Node, goal: Node) -> bool:
    return solver.search(goal, start, walk_only=False) is not None


# ---------------------------------------------------------------------------------------
# structural checks on the level data itself
# ---------------------------------------------------------------------------------------

def structural_problems(level: Level) -> List[str]:
    problems: List[str] = []
    ids = [e.id for e in level.entities]
    seen = set()
    for entity in level.entities:
        if entity.id in seen:
            problems.append(f"duplicate entity id '{entity.id}'")
        seen.add(entity.id)
        if not (0 <= entity.x < level.width and 0 <= entity.y < level.height):
            problems.append(f"entity '{entity.id}' at ({entity.x},{entity.y}) is outside the map")

    for kind in ("PlayerStart", "Exit"):
        count = sum(1 for e in level.entities if e.kind == kind)
        if count != 1:
            problems.append(f"level must declare exactly one {kind}, found {count}")

    for entity in level.entities:
        if entity.kind in ("Enemy", "Discovery", "StoryMarker", "Anchor", "Checkpoint", "PlayerStart", "Exit"):
            if not level.solid_at(entity.x, entity.y):
                problems.append(f"entity '{entity.id}' stands on empty tile ({entity.x},{entity.y})")
                continue
            # One empty tile directly above is enough: the body is 1.4 tall on 1.0 tiles.
            if level.solid_at(entity.x, entity.y - 1):
                problems.append(f"entity '{entity.id}' is buried: tile above ({entity.x},{entity.y - 1}) is solid")

    for entity in level.entities:
        if entity.kind == "Checkpoint":
            respawn = entity.attrs.get("respawn")
            if respawn is None:
                problems.append(f"checkpoint '{entity.id}' does not declare an explicit respawn tile")
                continue
            parts = [p.strip() for p in respawn.split(":")]
            if len(parts) != 2:
                problems.append(f"checkpoint '{entity.id}' respawn '{respawn}' is not 'x:y'")
                continue
            try:
                rx, ry = int(parts[0]), int(parts[1])
            except ValueError:
                problems.append(f"checkpoint '{entity.id}' respawn '{respawn}' is not numeric")
                continue
            if not level.solid_at(rx, ry):
                problems.append(f"checkpoint '{entity.id}' respawns onto empty tile ({rx},{ry})")
            elif level.solid_at(rx, ry - 1):
                problems.append(f"checkpoint '{entity.id}' respawns into a solid tile ({rx},{ry - 1})")
        if entity.kind == "Enemy":
            if "type" not in entity.attrs:
                problems.append(f"enemy '{entity.id}' declares no type")
            if "patrol" in entity.attrs:
                try:
                    patrol = int(entity.attrs["patrol"])
                except ValueError:
                    problems.append(f"enemy '{entity.id}' patrol is not an integer")
                    continue
                for dx in range(-patrol, patrol + 1):
                    col = entity.x + dx
                    if not level.solid_at(col, entity.y):
                        problems.append(
                            f"enemy '{entity.id}' would patrol off the ground at ({col},{entity.y})")
                        break
                    if level.solid_at(col, entity.y - 1):
                        problems.append(
                            f"enemy '{entity.id}' would walk into a wall at ({col},{entity.y - 1})")
                        break
        if entity.kind == "Discovery":
            if not entity.attrs.get("flag"):
                problems.append(f"discovery '{entity.id}' declares no flag, so finding it would not persist")

    for claim in level.claims:
        if level.entity(claim.from_id) is None:
            problems.append(f"line {claim.line}: reach references unknown id '{claim.from_id}'")
        if level.entity(claim.to_id) is None:
            problems.append(f"line {claim.line}: reach references unknown id '{claim.to_id}'")

    # connectivity of the walking surface: every standable tile should be reachable from the
    # start through the solver's own graph, otherwise a piece of the level is orphaned
    return problems


def reachable_nodes(level: Level, solver: Solver) -> set:
    """Every standable tile the player can actually get to from the start."""
    start_entity = next((e for e in level.entities if e.kind == "PlayerStart"), None)
    if start_entity is None:
        return set()
    start = solver.nearest_node(start_entity.x, start_entity.y)
    if start is None:
        return set()
    graph = solver.edges()
    seen = {start}
    stack = [start]
    while stack:
        node = stack.pop()
        for edge in graph.get(node, []):
            if edge.target not in seen:
                seen.add(edge.target)
                stack.append(edge.target)
    return seen


def classify_orphans(level: Level, solver: Solver) -> Tuple[List[str], List[str]]:
    """Split unreachable standable tiles into real problems and explained ones.

    The distinction is whether the tile *looks* reachable. A wall crown eight tiles above the
    only floor the player can stand on is not a place anyone expects to go: it is architecture.
    A ledge one or two tiles past a gap is a place the player will absolutely try to reach, so
    being unable to is a level bug.

    The test uses the solver's own numbers rather than a new constant: a tile counts as
    reachable-looking when some reachable surface sits within one jump of it — `jump_height`
    tiles in any direction, since that is the distance the tuning itself calls a jump.

    Returns (problems, explained).
    """
    reachable = reachable_nodes(level, solver)
    if not reachable:
        return [], []

    envelope = solver.tuning.jump_height
    problems: List[str] = []
    explained: List[str] = []

    for node in solver.stand_nodes():
        if node in reachable:
            continue

        nearest = None
        nearest_gap = None
        for other in reachable:
            dx = abs(other.col - node.col)
            dy = abs(other.row - node.row)
            # Chebyshev distance reads as "how far do I have to travel", which is the question a
            # player asks when they look at a ledge.
            gap = max(dx, dy)
            if nearest_gap is None or gap < nearest_gap:
                nearest_gap = gap
                nearest = (other, dx, dy)

        label = f"({node.col},{node.row})"
        if nearest is not None and nearest_gap <= envelope:
            other, dx, dy = nearest
            problems.append(
                f"{label} is unreachable but looks reachable: the player can stand on "
                f"({other.col},{other.row}) just {dx} across and {dy} up, inside the "
                f"{envelope:g}-tile jump envelope, and still cannot get there")
        else:
            other, dx, dy = nearest
            explained.append(
                f"{label}: {nearest_gap} tiles from the nearest reachable surface "
                f"({other.col},{other.row}) — further than the {envelope:g}-tile jump envelope, "
                "so it is out of reach by design rather than sealed off")

    return problems, explained


def report(level: Level, solver: Solver, root: str) -> Tuple[
        List[ClaimResult], List[str], List[str], List[str]]:
    """Every check, in one place: claims, structure, reachability, encounters, the secret."""
    kinds = read_enemy_kinds(root)
    encounter_problems, encounter_notes = check_encounters(level, kinds)
    secret_problems, secret_notes = check_secret(level, solver)
    orphan_problems, orphan_notes = classify_orphans(level, solver)

    problems = structural_problems(level) + encounter_problems + secret_problems
    notes = encounter_notes + secret_notes + orphan_notes
    return check_claims(level, solver), problems, orphan_problems, notes


def main() -> int:
    import sys
    here = __file__
    root = here.rsplit("/tools/", 1)[0]
    level_path = sys.argv[1] if len(sys.argv) > 1 else \
        f"{root}/Assets/Aether/Resources/Levels/region1.greenway.level.txt"
    tuning_path = f"{root}/Assets/Aether/Code/Aether.Data/Runtime/Config/PlayerTuningData.cs"

    # Level data is hand-written, so a typo is the most likely way this tool is ever run. A
    # traceback would technically be a failure too, but it hides which character on which line is
    # wrong; the parser already knows, so report it and stop.
    try:
        level = parse_level(level_path)
        tuning = read_tuning(tuning_path)
    except LevelParseError as error:
        print(f"  FAIL level data: {error}")
        return 1

    solver = Solver(level, tuning)

    print(f"level      : {level.meta.get('display_name', '?')} ({level.meta.get('id', '?')})")
    print(f"size       : {level.width}x{level.height} tiles")
    print(f"entities   : {len(level.entities)}")
    print(f"standable  : {len(solver.stand_nodes())} tiles")
    print(f"tuning     : body {tuning.body_width}x{tuning.body_height}, run {tuning.run_speed}, "
          f"jump v {tuning.jump_velocity:.2f} apex {tuning.jump_height} in {tuning.jump_time_to_apex}s, "
          f"rise g {tuning.rise_gravity:.1f}, fall g {tuning.fall_gravity:.1f}, "
          f"air dodge {tuning.allow_air_dodge}")
    print()

    results, problems, orphan_problems, notes = report(level, solver, root)
    for result in results:
        print(f"  {'OK  ' if result.ok else 'FAIL'} {result.describe()}")
    print()
    problems = problems + orphan_problems
    for problem in problems:
        print(f"  STRUCTURAL {problem}")
    if notes:
        print("  pacing, encounters and secrets:")
        for note in notes:
            print(f"    - {note}")
    failed = [r for r in results if not r.ok]
    print()
    print(f"{len(results) - len(failed)}/{len(results)} traversal claims proven, "
          f"{len(problems)} structural problem(s)")
    return 0 if not failed and not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
