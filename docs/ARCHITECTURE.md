# Project Aether — Architecture

This document records the decisions that shape the codebase and, where a decision was
not obvious, why the alternatives were rejected. It is meant to be read before adding a
system, so that new work extends the existing shape instead of working around it.

---

## 0. Environment constraint that shapes everything

**The environment this foundation was authored in has no Unity Editor and no C# compiler.**
`unity.com` and `packages.unity.com` are blocked, and no Mono/.NET toolchain is installable.

That is not a footnote — it directly determines an architectural decision (see §3). It also
defines what "verified" is allowed to mean in this repository:

| Claim | Status |
|---|---|
| Asset GUIDs resolve, no dangling references | **Verified** by `tools/verify/verify.py` |
| Assemblies reference each other legally, no cycles | **Verified** by the same tool |
| Members accessed on project-owned types exist, with correct arity | **Verified** by the same tool |
| Unity API signatures are used correctly | **NOT verified** — requires a real compile |
| The level is traversable, secrets reachable, no soft-locks | **Verified** by the level reachability solver |
| Play Mode behaviour | **NOT verified** — requires the Editor |

`verify.py` is deliberately honest about its limits: it only checks receivers whose type is
one of ours, so it produces no false positives but cannot validate Unity calls. It found two
real compile errors in the first draft of the player controller, so it is not decoration.

---

## 1. Assembly model

```
Aether.Core      no dependencies      engine-agnostic building blocks
Aether.Data      -> Core              ScriptableObject configuration only, no behaviour
Aether.Gameplay  -> Core, Data        all runtime behaviour
Aether.Editor    -> Core, Data, Gameplay   editor-only tooling (never ships)
Aether.Tests.EditMode -> Core, Data, Gameplay, TestRunner
```

This is a **layer** split, not a feature split. Feature-per-assembly is the maximally
modular answer, but with a handful of systems it produces a web of asmdefs whose only
benefit is faster incremental compiles. The layering above gives the property that actually
matters early: **`Aether.Core` cannot depend on gameplay, and `Aether.Data` cannot depend on
behaviour** — both enforced by the verifier, not by convention.

Promote a system to its own assembly when it becomes large enough that its compile time or
its dependency surface is a problem. Do not do it speculatively.

### Namespace discipline

Namespaces never end in a segment that collides with a UnityEngine type name. `Cameras`
rather than `Camera`, because a namespace called `Camera` forces fully-qualified references
at every use site and produces confusing CS0104 errors. The verifier warns on this.

---

## 2. Player, enemies and the state machine

`StateMachine<TContext, TKey>` (in `Aether.Core.States`) is shared by the player, every
enemy archetype and the boss. One implementation, one place to observe transitions.

**Player states are `Grounded`, `Airborne`, `Dodge`, `Hurt`, `Dead`.** Idle and running are
*not* separate states. Nothing in gameplay behaves differently between standing still and
moving at full speed, so splitting them would duplicate the movement code and add a
transition to keep in sync; presentation derives "idle vs run" from horizontal speed, which
is how it blends the two animations anyway.

**Attacking is not a state.** `PlayerCombat` runs its own timeline and communicates with the
state machine through `MovementSpeedMultiplier`. Making attack a state would require every
transition to know about attack, and would make "attack out of a dodge" a special case
instead of a property.

### Timing

All forgiveness windows (coyote time, jump buffer, dodge cooldown, invulnerability) are
stored as **absolute `Time.time` timestamps**, never as countdowns decremented in
`FixedUpdate`. A countdown is decremented on one clock and read on another; on an Android
device that is thermally throttling, the two disagree and the player experiences it as
"the jump randomly didn't come out". Timestamps are frame-rate independent by construction.

Movement integrates in `FixedUpdate` (stability). Attack timing runs in `Update` (wind-up
frames are a promise about what the player sees). The split is intentional.

---

## 3. Level authoring: data-driven assembly

> **This is the largest architectural decision in the project. It is driven by §0, and it
> is reversible — see "Migration path" below.**

The region is authored as **compact, diffable data** (ASCII tile maps plus a small amount of
structured metadata), and a deterministic builder constructs the playable scene from it.

### Why not hand-authored Unity scenes and Tilemaps

Because in this environment a hand-written `.unity` file would be **unverifiable fiction**.
A Unity scene is a flat YAML document of thousands of fileIDs and GUID references; writing
one by hand, with no Editor to open it in, produces something that looks plausible and is
almost certainly broken — invisible colliders, wrong sorting, dangling references. The
instruction was explicit: *do not fake quality.* A scene that cannot be opened, inspected or
played is fake quality regardless of how carefully the YAML was typed.

### Why data-driven is genuinely the better choice here, not just the available one

1. **It is verifiable without Unity.** The same data that builds the level can be *solved*:
   every gap checked against the player's real jump arc, every secret checked for
   reachability, every gate checked for correct gating before and after Rootbind. This
   catches "impossible jump", "unreachable secret" and "progression blocker" — three of the
   specific failure modes the brief lists — as build failures rather than as playtest
   discoveries.
2. **It reviews as text.** A level change appears in a pull request as a readable diff.
3. **It is mobile-friendly by construction.** Uniform chunked geometry, one material, no
   per-object prefab wiring, and a structure that pools and streams naturally.
4. **It decouples art from layout.** Placeholders live in one art-mapping table. Replacing
   them with final art touches no gameplay code and no level data.
5. **It enforces the progression graph.** Gates, shortcuts and ability locks are declared
   relationships that the solver validates, rather than colliders someone remembered to add.

### Migration path

The data is the source of truth; the builder is an implementation detail behind it. If
Scene-view editing becomes a hard requirement, an Editor command can bake the same data into
real Tilemap scenes, and the runtime can then load either. **Adding that bake tool does not
change a single line of gameplay code.** If the director prefers the bake-tool workflow from
the start, it can be added as an extra milestone without redoing this work.

### What was actually built (M2)

There are two consumers of the level data and they must never disagree, so the split is
explicit:

| Consumer | Where | Job |
|---|---|---|
| Runtime builder | `Aether.Gameplay/Levels/LevelRuntimeBuilder` | Turns data into colliders and gameplay objects, in play mode |
| Editor bake | `Aether.Editor/LevelBaker` | Bakes data into Tilemaps + a scene for inspection and scene-based workflows |
| Solver (gate) | `tools/verify/levelcheck.py` | **Authority.** Proves every `[validation]` link with the real movement model; non-zero exit on failure |
| Solver (Unity) | `Aether.Data/Levels/PlayerTraversalSolver` | Same model in C# for the bake command and EditMode tests; single precision, so it is treated as advisory |

Both solvers read their numbers from `PlayerTuningData` rather than hard-coding them, so a
tuning change cannot silently invalidate the level.

**The bake refuses to run on a level that does not solve.** A bake that produced a beautiful
scene containing an impossible jump would be worse than no bake at all: it would hide the
problem behind something that looks finished. Baking stops with the list of unproven links and
a non-zero result, and the scene stays untouched.

**Baked geometry is not the source of truth.** A baked scene contains the Tilemaps for the
region, and `LevelBootstrap` has a `_buildGeometry` switch that is turned off in baked scenes
so the geometry is not built twice. Entities — enemies, checkpoints, the secret, the exit —
are instantiated from level data in every case, because behaviour must never exist only inside
a scene file that nothing regenerates.

### Content assets

`PlayerTuning`, `Attack.Strike`, `Attack.StrikeFollowUp` and `Enemies/ForestStalker` are
`ScriptableObject` assets written as Unity YAML with real script GUIDs and deterministically
generated `.meta` files. They are ordinary project assets from the moment the project is
opened and can be retuned in the Inspector. Their numbers are **identical to the tuning the
level was designed against**; the gate proves every link against exactly these values, so
editing one of them is a level-design change and must be followed by a solver run
(`Aether > Verify Region 1 (Greenway)` or `python3 tools/verify/levelcheck.py`).

---

## 4. Combat model

- Damage flows through `IDamageable` and a `readonly struct DamageInfo`, so combat allocates
  nothing per hit.
- The player's attack hitbox is a **box query positioned from an `AttackDefinition` asset**,
  not a child trigger collider. An attack asset therefore fully describes itself, enemies and
  the player share one code path, and the level builder never has to wire colliders per attack.
- **Each target is hit at most once per attack.** A wide hitbox active for several frames
  would otherwise deal its damage repeatedly — the classic "why did one swing take half my
  health" bug.
- Attack timing is data (`startup` / `active` / `recovery`), and combos are a linked list of
  assets. Adding a fourth hit to the chain is authoring, not programming.
- Knockback direction is derived from the damage source position, so melee and hazards share
  one code path and the direction is correct for both.

---

## 5. Progression and persistence

`ProgressionState` (abilities) and `WorldState` (one-shot world facts) are plain
serialisable classes with no Unity references and no I/O.

- Abilities use an **enum with explicit, frozen numeric values**, because changing a value
  breaks saves. New abilities append.
- World facts use **stable string ids authored in level data** (`secret.greenway.hollow`),
  not enum members, so new content requires no code change. The verifier rejects duplicate
  or dangling ids.
- `ISaveStore` is the storage hook. **No persistence is implemented**, deliberately: shipping
  a save format before region progression is final would guarantee a migration.

`GameSession` is the single choke point for mutating progression, so autosave triggers and
"first time this happened" presentation have one place to live.

---

## 6. Mobile / Android performance rules

Applying these from the start, without premature micro-optimisation:

- **Pool anything spawned repeatedly** (`ComponentPool<T>`), which is where GC-induced
  hitching on low-end devices actually comes from. `IPooledObject` makes the
  reset-transient-state requirement visible on the component rather than a convention.
- **No allocation in per-frame paths.** Physics queries write into caller-owned buffers
  (`Physics2DQuery`); evasion hit lists are reused.
- **`EventBus` is for low-frequency cross-system signals only** — ability unlocks, checkpoints,
  boss phases — never per-frame traffic. That rule is documented on the type itself because
  it is the one that gets violated.
- **Structural choices over flag tweaks.** Batching-friendly geometry, uniform sprite sizes and
  shared materials buy more than disabling shadows ever will.
- **Deliberately not done yet:** quality-tier differentiation, module stripping, texture format
  matrices, Addressables. All are decisions that need content and a target device to measure;
  making them now would be guessing.

---

## 7. Verification strategy

Three gates, all runnable without a Unity licence:

```
python3 tools/verify/gen_meta.py --check         # every asset has a .meta
python3 tools/verify/verify.py                   # assets, assemblies, project-type symbols
python3 tools/verify/levelcheck.py               # the level is playable, proven by simulation
python3 tools/verify/negative_controls.py        # the gates above actually fail when they should
```

**The gates are validated against injected faults.** `negative_controls.py` copies the
repository to a temporary directory, injects one fault at a time — a dangling GUID, a missing
`.meta`, a call to a member that does not exist, a wrong argument count, an empty folder, an
illegal cross-assembly reference written both with `using` and inline, a gap widened until the
exit is unreachable, a step added to a `must=walk` route, the canopy route to the secret
removed, an entity inside solid rock, an unknown tile character — and asserts that the matching
tool reports it. It touches nothing in the working tree. Twelve controls, all detected at the
time of writing.

Two checks were added to `verify.py` because those controls failed the first time they were
run: a dangling asset GUID is now a **failure** rather than a warning, because Unity resolves
it to nothing and the asset silently loses a field; and cross-assembly visibility is enforced
for **fully-qualified references**, not just `using` directives. A verifier whose own holes are
unknown is not a verifier.

`verify.py`'s scope and blind spots are documented in §0. Everything the gates cannot see —
Unity API signatures, import behaviour, Play Mode, touch on a device — is listed as
not verified in every report rather than assumed.

**The first Unity Editor import remains the real test.** No claim of "compiles" or "play
mode tested" is made until someone has actually opened the project.
