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
| A file names only types it can actually see (assembly and namespace) | **Verified** by the same tool |
| A nested type's member is qualified, and engine types are imported before use | **Verified** by the same tool |
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

The verifier does not read intent, so it went looking for every way a file can name a type
it is not allowed to see: a `using` directive, a fully-qualified `Aether.X.Y` reference, and
— added after the first real Unity build stopped on exactly this — a **bare type name** with
neither. Every rule has a negative control, including one that proves the same reference is
*silent* in an assembly that may legally see it.

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

## 6b. Presentation: gameplay is never read by it, and never waits for it

The Greenway is drawn with placeholders, and placeholders still have to *communicate*. The rule
that keeps that from leaking rules into art code:

> **Gameplay publishes events; presentation subscribes. Gameplay never knows presentation exists.**

| Component | Reads | Owns |
|---|---|---|
| `EnemyTelegraph` | `StateChanged`, `PlayerSpotted`, `AttackStartupProgress`, `Damaged`, `Died` | the enemy's colour and scale |
| `PlayerFeedback` | `Damaged`, `HealthChanged`, `Died`, `AttackStarted`, `HitLanded` | the player's colour and scale |
| `CheckpointView` | `CheckpointTrigger.ActivatedChanged` | the checkpoint ring's colour |
| `LevelPalette` | nothing | every placeholder colour in one file |

Three consequences worth stating:

1. **No timing lives in presentation.** Delete all three components and the enemy fights
   identically; that is the test for whether presentation has stayed presentation. In particular
   the wind-up ramp reads `AttackStartupProgress` from the shared `AttackRunner` — it does not
   model the attack a second time.
2. **They switch themselves off.** Each animates only while something is actually changing, then
   sets `enabled = false` and waits for the next event. An idle level costs nothing per frame,
   which matters more than it looks on a phone.
3. **The palette is shared with the bake.** `LevelPalette` is read by the runtime builder *and*
   the Editor bake, so the scene an artist opens and the game the player sees agree on what a
   checkpoint looks like. Replacing placeholders with art means editing this one file.

The visual vocabulary the first encounter teaches, in one sentence: **warm and bright means
danger, dull and cool means it is safe to approach, and the shape says what a thing is.**

## 6c. The studio intro: the first thing the app shows

The app opens in `Assets/Aether/Scenes/StudioIntro.unity`, which is scene **0** in the build order.
It is the only scene whose whole job is to be looked at: black, the Varellon mark, the wordmark
settling under it, a short hold, a soft exit, and then it hands the app to `Boot`.

> **A scene that owns its own flow declares itself, and the boot stands down.**
> `SceneFlowOwner` is an empty abstract base; `AetherBoot.BootIfUnclaimed` looks for one and returns
> without doing anything if a scene has it. That is the whole seam. The Main Menu will be the second
> owner, which is why the seam exists before the menu does.

Four decisions worth recording:

1. **Sprites parented to the camera, not uGUI.** Same reasoning as the touch controls: a Canvas, an
   EventSystem and an input-UI module are three things to wire that cannot be checked without an
   Editor, and the intro is two images that fade. Layout is fractions of `Screen.safeArea`, so
   `tools/verify/intro.py` can prove it on 140 synthetic device shapes — and because arithmetic
   cannot see what a renderer draws, a play-mode test (`Assets/Aether/Code/Aether.Tests.PlayMode`)
   plays the real scene and measures the frames that come out: ink present, centred, inside the
   frame, aspect preserved, black ground, and a reveal and an exit that change the picture. That
   test exists because the one bug that got past every static gate here was a placement bug that
   drew the mark several times the width of the screen.
2. **The layout is fitted with one scale factor from both limits.** Two factors — one per axis — is
   the classic way a logo ends up stretched on a tablet. The numbers are public constants in
   `StudioIntroSequence.Layout` so the gate reads the same values the runtime uses.
3. **The artwork is one PNG, and its coverage is the alpha channel.** The Varellon file is a light
   canvas with the mark drawn on it — no transparency — so the intro keys the canvas out by
   luminance at load time and draws the ink. A file supplied with a real alpha channel is used as
   it is. Both paths are decided by what the pixels say, not by a setting someone has to remember.
   The gate decodes the committed PNG in pure Python and re-measures it, which is what stops
   "the logo is in the repository" from being mistaken for "the logo is on screen".
4. **Landscape only, both ways up, and the splash is off.** The player settings — not a script —
   put the app in Auto Rotation with only Landscape Left and Right allowed, and set Auto Rotation
   Behavior to Sensor, so a phone whose rotation lock is on still shows the game the way it was
   drawn instead of refusing to turn. The Made-with-Unity splash is switched off there too, which
   Unity 6 permits on Personal, so the first thing anyone sees is the studio's own intro rather
   than Unity's. `tools/verify/verify.py` checks all of it in the project file, CI inspects the
   built APK's manifest for the orientation it actually shipped with, and the phone is the final
   word.
5. **The intro may be skipped, and switched off.** A tap, key or face button after a short grace
   ends it early; `Preference` (a PlayerPref) is what a future settings screen writes. Nothing in
   the intro knows what comes after it: `_nextScene` is a string, and pointing it at a Main Menu is
   the only change the next stage needs.

The intro is **silent**. It requests its cues through `SoundDirector` like the rest of the game, so
it becomes audible when an audio backend exists; no audio asset is invented for it.

## 7. Verification strategy

### The three levels of verification, never substituted for each other

| Level | What it can prove | Tools |
|---|---|---|
| **Static / tooling** | references, assemblies, symbols, asset fields, level reachability, encounter fairness, touch layout | the five commands below |
| **Unity Editor** | that it imports, compiles, and plays | **not run: no Editor in this environment** |
| **Android device** | that it builds, launches, and feels right in a hand | **not run: no device in this environment** |

Nothing in the first row is evidence for the second or third. A green run below means the data,
the references and the geometry are sound — nothing more, and every report says so explicitly.

### The gates

```
python3 tools/verify/gen_meta.py --check         # every asset has a .meta
python3 tools/verify/verify.py                   # assets, assemblies, symbols, asset fields
python3 tools/verify/levelcheck.py               # the level is playable, proven by simulation
python3 tools/verify/touchlayout.py              # the touch layout is usable, proven by geometry
python3 tools/verify/intro.py                    # the studio intro's layout, length and artwork
python3 tools/verify/negative_controls.py        # the tools above actually fail when they should
```

`levelcheck.py` proves more than reachability: it reads the enemy archetypes *and the attack
timeline they point at* and checks the things that make an encounter teach — an enemy with no
attack, two encounters that can see the same ground, a checkpoint that respawns the player inside
an enemy's senses, an exit inside a fight, and a counter window shorter than the telegraph that
preceded it. It also reports the pacing it can see (spacing, detours, telegraph and recovery
times) and names the things only a person can judge.

**The gates are validated against injected faults.** `negative_controls.py` copies the
repository to a temporary directory, injects one fault at a time — a dangling GUID, a missing
`.meta`, a call to a member that does not exist, a wrong argument count, an empty folder, an
illegal cross-assembly reference written both with `using` and inline, a bogus member reached
through a `foreach` variable, a bogus member on a type whose name is shared by two nested
classes, an asset key that does not exist on its script, a gap widened until the exit is
unreachable, a step added to a `must=walk` route, the canopy route to the secret removed, an
entity inside solid rock, an unknown tile character, a platform nothing can reach, an archetype
with no attack, an enemy open for less time than it warned for, two encounters that can see the
same ground, a checkpoint respawning into an enemy's sight, an exit inside a fight, a secret
reachable without jumping — and asserts that the matching tool reports it. Two controls assert
the *opposite* direction, that a legitimate shape produces no complaint at all, because an
over-eager verifier is as broken as a blind one. It touches nothing in the working tree.
Fifty-two controls, all detected at the time of writing.

Several checks were added because a control failed the first time it was run, which is the
whole point of having them: a dangling asset GUID is now a **failure** rather than a warning, because Unity resolves
it to nothing and the asset silently loses a field; and cross-assembly visibility is enforced
for **fully-qualified references** and now for **bare type names**, not just for `using`
directives — and a file that names a UnityEngine type it never imports is now a failure too.
Both of those holes were found by the first two real Unity builds rather than by the gates, and
both are recorded honestly below rather than quietly patched. A verifier whose own holes are
unknown is not a verifier.

`verify.py`'s scope and blind spots are documented in §0. Everything the gates cannot see —
Unity API signatures, import behaviour, Play Mode, touch on a device — is listed as
not verified in every report rather than assumed.

**The gates get stronger when they are wrong, not quieter.** Writing the negative controls for
this milestone exposed four defects in the tools themselves: a dangling GUID was only a warning
when Unity treats it as a silent data loss; cross-assembly visibility was enforced for `using`
directives but not for fully-qualified references; any parenthesised text before a brace was
parsed as a parameter list, so `foreach (PlayableNode other in reachable)` registered a parameter
called `reachable` and produced failures that did not exist; and two nested classes with the same
name in different controllers were treated as an ambiguity when C# resolves them by their
containing type. Each was fixed at the root, and each fix is covered by a control.

**The first Unity Editor import found four defects the gates had missed** — a type named from an assembly that could not see it, and an engine type named with no import — and each now has a rule and a control: a member of a nested type used without its container, and a static method reaching for an instance member through a name that was never imported. It also settled the licensing question: account credentials alone activate a Personal seat, no `.ulf` and no serial involved.

**The first Unity Editor import remains the real test.** No claim of "compiles" or "play
mode tested" is made until someone has actually opened the project.
