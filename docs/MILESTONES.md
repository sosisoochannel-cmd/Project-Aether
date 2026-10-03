# The Rootbound Wilds — milestone plan

The first region, built in milestones that each end in a working state and a commit.
Ordering is deliberate: **the thing that makes everything else verifiable is built first.**

```
M0  Foundation ...................... DONE
M1  Core architecture + player ...... DONE  <- this milestone
M2  Level pipeline + The Greenway ... NEXT
M3  Enemies + combat integration
M4  Whispering Woods + shortcut
M5  Old Settlement + NPC hook + secrets
M6  Deep Forest + environmental storytelling
M7  Root Guardian arena + 3-phase boss
M8  Rootbind unlock + gated backtracking
M9  Region exit + audit + docs
```

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

## M2 — Level pipeline + The Greenway (next)

- Level data format (ASCII maps + structured metadata) and the deterministic builder.
- **The reachability solver**, which is the point of the whole approach: it proves every gap
  is jumpable with the tuned jump arc, every ledge is reachable, no jump is impossible.
- The Greenway: ancient forest, natural paths, small elevations, gaps, natural platforms,
  distant visible landmarks. Teach by geometry, not by pop-ups.
- Checkpoints, camera bounds per area, the first secret.
- Runtime bootstrap so the region actually launches from `Boot.unity`.

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
