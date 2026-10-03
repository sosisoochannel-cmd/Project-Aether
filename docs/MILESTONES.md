# The Rootbound Wilds — milestone plan

The first region, built in milestones that each end in a working state and a commit.
Ordering is deliberate: **the thing that makes everything else verifiable is built first.**

```
M0  Foundation ...................... DONE
M1  Core architecture + player ...... DONE  <- this milestone
M2  Level pipeline + The Greenway ... DONE (see notes)
M3  Enemies + combat integration .... DONE
M4  Whispering Woods + shortcut
M5  Old Settlement + NPC hook + secrets
M6  Deep Forest + environmental storytelling
M7  Root Guardian arena + 3-phase boss
M8  Rootbind unlock + gated backtracking
M9  Region exit + audit + docs
```

### On the order

M3 (enemies) was built before M2 (the level) deliberately. The level pipeline includes a
reachability solver that has to reason about what the player can *do* — and enemies are part of
that: an encounter is a traversal obstacle as much as combat is. Building the archetypes first
means the solver is written against real enemy behaviour instead of a placeholder abstraction.

M2 followed and closed the loop: the region is playable from `Boot.unity` with movement, combat,
checkpoints, a secret and an exit, and the solver proves the level against the tuned player.
The next region (M4) can now be authored entirely as data.

---

## M0 — Foundation ✅

Unity 6.3 LTS (6000.3.24f1), URP with the 2D Renderer, Android target, 2D project mode,
clean repository hygiene. Committed as `a5a013e`.

## M1 — Core architecture + player ✅

Establishes the structures every later milestone plugs into, and proves the codebase is
structurally sound.

**Created**

| Area | What |
|---|---|
| Assemblies | `Aether.Core`, `Aether.Data`, `Aether.Gameplay`, `Aether.Editor`, `Aether.Tests.EditMode` |
| Core / Events | `EventBus`, `AbilityUnlockedEvent`, `CheckpointActivatedEvent` |
| Core / States | `StateMachine<TContext, TKey>`, `IState<TContext>` |
| Core / Combat | `DamageInfo`, `IDamageable`, `DamageKind`, `DamageResolver`, `Physics2DQuery` |
| Core / Pooling | `ComponentPool<T>`, `IPooledObject` |
| Core / Progression | `AbilityId`, `ProgressionState`, `WorldState`, `SaveData`, `ISaveStore` |
| Data / Config | `PlayerTuningData`, `AttackDefinition` |
| Gameplay | `PlayerController`, `PlayerMotor`, `PlayerCombat`, `PlayerHealth`, `PlayerInputReader`, `CameraFollow2D`, `GameSession` |
| Tooling | `tools/verify/verify.py`, `tools/verify/gen_meta.py` |

**Deliberate design calls**

- Idle and Run are not separate states (see `docs/ARCHITECTURE.md` §2).
- Attack is not a state; combat overlays movement via `MovementSpeedMultiplier`.
- Jump feel is authored as *height* and *time to apex*; gravity and impulse are derived.
- Every forgiveness window is an absolute timestamp, not a countdown.
- Gravity is applied by `PlayerMotor`, not by `Rigidbody2D.gravityScale`, so rise and fall
  can differ. This is why the body is dynamic and all motion goes through the motor.

**Verified**

- 19 files analysed; 33 project types modelled; 217 member references on project-owned
  types resolved with arity checked; assemblies acyclic with correct visibility.
- Two real compile errors were found and fixed by the analyser during this milestone
  (`PlayerCombat.Tick` and `PlayerCombat.TryStartAttack` called with stale signatures).
- The analyser itself was validated with four negative controls; all four are caught.

**Not done in M1 (and why)**

- No enemy archetypes: they need the level pipeline to place and exercise them (M3).
- No touch controls: keyboard/gamepad only, so the director can test in the Editor
  immediately. Touch is required before this region is declared done — tracked in M9.
- No level, no prefabs, no scene content.

## M3 — Enemy archetypes ✅

Three archetypes, one shared AI, one shared attack timeline.

**Created**

| Area | What |
|---|---|
| Data | `EnemyDefinition` — the config asset for one archetype, plus the `EnemyBehaviour` enum |
| Core | `AttackRunner` + `AttackPhase`, extracted so the player and enemies share one attack timeline |
| Gameplay / Enemies | `EnemyController`, `EnemyMotor2D`, `EnemyHealth` |
| Gameplay / Sound | `SoundDirector` — the placeholder audio seam (no audio system) |
| Tests | `AttackRunnerTests` covering the timeline and combo rules |

**The three archetypes and what each one teaches**

| Archetype | Behaviour | Lesson |
|---|---|---|
| Forest Stalker | `PatrolStriker` | Timing and dodge — long, unmistakable wind-up; honest reach; leaving range ends the threat |
| Thorn Crawler | `SurfaceCrawler` | Positioning — stays on its platform, turns at ledges, so ground is unsafe and platforms are the answer |
| Canopy Hunter | `AmbushDropper` | Environmental awareness — perches above a route and drops after a cue the player can react to |

**Design rules encoded in the implementation**

- **Deterministic.** `EnemyController` contains no random selection at all; every delay comes from
  the archetype asset. An enemy that randomly changes its rules cannot be learned, and teaching is
  the entire purpose of these three.
- **Telegraph before commit.** Every attack path passes through `Alert`, which fires the alert cue
  and waits `AttackWindupDelay` before anything else happens.
- **Perception has a facing.** The notice box sits *in front of* the enemy, and a linecast rejects
  sight through walls. A player can therefore genuinely sneak past a patrolling enemy.
- **The recovery pause is load-bearing.** `RecoveryPause` is the window in which the player answers.
  Removing it turns every exchange into a trade.
- **No stun-locking.** Stagger, knockback and invulnerability are all fixed, short durations.
- **Terrain checks every step, not at waypoints**, so level geometry added later cannot walk an
  enemy off a ledge it used to stop at.

**Deliberate calls**

- `EnemyMotor2D` is **not** merged with `PlayerMotor`. The player needs jump shaping, coyote time
  and a dodge override; an enemy needs ledge and wall probing. Unifying them today would produce a
  class full of capabilities each side ignores. They already share everything genuinely common:
  `Physics2DQuery`, the state machine and the attack timeline.
- `AttackRunner` **is** shared, because two attack implementations is how a project ends up with
  enemy telegraphs that behave differently from the player's.
- The audio seam is `SoundDirector.Request(cueId, position)`. It is deliberately not an audio
  system: no mixer, no bus routing, no clip assets. Cue ids are authored in data, so a future
  audio layer can map them to sounds without touching gameplay code.

**Verified**

- 28 files analysed, 58 types modelled, 673 member references resolved with arity checked;
  no failures. Four negative controls (missing member, wrong arity, removed method, deprecated
  API) all fire correctly.
- Tooling improvement made during this milestone: simple type names declared in more than one
  namespace are now reported as unverifiable rather than checked against the wrong type, so the
  analyser stays free of false results.

## M2 — Level pipeline + The Greenway

The region is defined in data and every claim it makes about the player is proven before it
ships. `Assets/Aether/Resources/Levels/region1.greenway.level.txt` is the source of truth: a
120x26 ASCII tile map, entity declarations and a `[validation]` block that the solver must
prove. Bake (`Aether > Bake Region 1 (Greenway)`) turns that data into Tilemaps for
inspection; it refuses to bake a level that does not solve.

**Created**

| Area | What |
|---|---|
| Data | `LevelTileKind`, `LevelEntities`, `LevelTraversal`, `LevelData`, `LevelParser`, `PlayerTraversalSolver` |
| Gameplay | `LevelRuntimeBuilder`, `LevelContent`, `LevelDirector`, `LevelBootstrap`, `PlayerFactory`, `EnemyFactory` |
| Gameplay / triggers | `CheckpointTrigger`, `DiscoveryTrigger`, `LevelExitTrigger` |
| Controls | `IGameplayInput`, `GameplayInputRouter`, `TouchInputSource`, `TouchControlsView` |
| Editor | `LevelBaker` (bake + verify menu commands) |
| Content | `PlayerTuning`, `Attack.Strike`, `Attack.StrikeFollowUp`, `Enemies/ForestStalker` |
| Level | `region1.greenway.level.txt` — geometry, 3 encounters, 2 checkpoints, 1 secret, 3 story markers, exit |

**Proven by the gate** (`python3 tools/verify/levelcheck.py`, and identically in Unity):
8/8 traversal claims, including a `must=walk` route that requires zero jumps, the secret
terrace reachable via the canopy route, and both checkpoint-to-exit retries. 0 structural
problems. The tightest jump on the route clears 2.50 of 4.14 available, leaving 40% slack.

**Deliberate scope decisions**

- Only the Forest Stalker appears. Thorn Crawler and Canopy Hunter are implemented, but The
  Greenway's job is that the player *learns the Forest Stalker* before leaving it; a second
  and third archetype would compete with that lesson and belong in later regions.
- `RecoveryPause` is load-bearing in all three encounters: each one leaves a real counter
  window so the fight teaches dodge-then-punish rather than dodge-only.
- The first secret is a small side terrace with a very quiet Master trace — no exposition, no
  pop-up. Three environmental story markers are geometry-level details (a trail that stops, an
  unnaturally regular growth, a weathered marker on a rock), deliberately easy to walk past.
- No cutscenes, dialogue, final audio or VFX. Cues are referenced by id and stay silent.

**Not verified in this milestone:** the first Unity Editor import, Play Mode behaviour, touch
controls on a device, and every Unity API signature. No claim of "compiles" or "playtested" is
made anywhere in this milestone's report; see the report's two-part structure.

## M3 — Enemies + combat integration

- `Forest Stalker` — teaches timing and dodge.
- `Thorn Crawler` — teaches positioning.
- `Canopy Hunter` — teaches environmental awareness.
- All three: deterministic, telegraph-first, predictable. No random attack selection.
- Encounter placement with safe spaces, combat spaces and breathing spaces.

## M4 — Whispering Woods + shortcut

- Escalated level design: larger roots, denser growth, stranger routes, unnatural clearings.
- A gradual loss of safety, carried by layout rather than by music.
- A **Silent Zone** that is a real gameplay space, with a clean hook for a future audio layer.
- A genuine shortcut back to The Greenway: discoverable, unlockable, real traversal saving.

## M5 — Old Settlement + NPC hook + secrets

- Ruined structures, side paths, exploration space, interaction points.
- NPC **placeholder with a clean integration hook** — no dialogue system in this milestone.
- A secret, and at least one area that is visible but unreachable until Rootbind.

## M6 — Deep Forest + environmental storytelling

- Higher complexity: main route plus side routes, hazards, vertical traversal, hidden areas,
  Master clues, route choices — while keeping readability.
- At least one environmental story sequence readable without exposition.
- Darkness must never make gameplay ambiguous.

## M7 — Root Guardian arena + boss

- Arena with its own identity: organic circular space, ancient tree, large roots, readable
  floor, real traversal room, limited but genuine safe spots.
- Three phases — **Guardian** (pattern reading), **Awakening** (the roots reshape the arena,
  changing movement space and reinforcing attacks), **Heart of the Forest** (speed and
  pressure up, telegraphs still clear).
- The boss is defeated, not destroyed; the environment calms.

## M8 — Rootbind + gated backtracking

- `Rootbind` as a real system: anchors on designed, marked surfaces — not a one-off animation,
  not hard-coded. Data-driven anchor definitions, validated by the solver.
- Three genuine Rootbind-gated discoveries in earlier areas (Root Wall, hidden settlement
  area, early forest gap), each rewarding a real reason to return.
- Backtracking must be fast and pleasant; the shortcut carries the load.

## M9 — Region exit + audit

- Region exit with a new Master clue, and a blocked placeholder transition to region two.
  **Region two is not built.**
- Audio and music remain placeholder hooks only. No final audio system.
- Full audit against the acceptance checklist: progression blockers, impossible jumps,
  unreachable secrets, boss soft-locks, Rootbind progression bugs, missing references,
  invalid scene references, wrong layers, wrong colliders.
- Touch controls land here at the latest.
- README and documentation updated.

---

## Rules that apply to every milestone

1. **Do not fake quality.** Tidy, correctly-sized, correctly-layered placeholders that an
   artist can swap without touching gameplay — never random sprites, cubes and colliders.
2. **No architecture for systems that do not exist yet.** `EnemyDefinition` and
   `AbilityDefinition` are not written until M3 and M8 need them.
3. **Every milestone ends with a passing `verify.py` and a commit.**
4. **Nothing is claimed as tested that has not been run.** Without an Editor in the build
   environment, verification means static analysis plus the level solver, and the report says
   so plainly.
5. **When choosing between "more" and "better" — better.** Drop a low-value feature before
   lowering the quality of the core.
