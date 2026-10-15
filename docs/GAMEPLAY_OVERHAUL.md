# Gameplay Engineering & Quality Overhaul

This document details the root-cause analysis, mechanical design decisions, and architectural implementation of the gameplay engineering and quality overhaul in Project Aether on branch `arena/b1259ee6-project-aether`.

---

## 1. Executive Summary & Defect Register

Prior to this overhaul, gameplay audits across movement, combat, enemy AI, level director lifecycles, and mobile touch presentation identified 12 genuine functional defects:

| ID | Subsystem | Defect / Failure Mode | Root Cause | Resolution |
|---|---|---|---|---|
| **GP-01** | Player Movement | Rapid exponential decay of vertical velocity on short hop / variable jump | `PlayerController.HandleJumpCut()` executed `CutJump()` unconditionally every physics step while rising without tracking if the cut was already applied | Added `_jumpCutApplied` flag; ensures `CutJump()` scales upward velocity exactly once per jump arc |
| **GP-02** | Player Movement | Mid-air ground jump exploit (coyote time leakage after jump) | `TryJump()` did not reset `_lastGroundedAt` on take-off, leaving `InCoyoteWindow` active for 0.12s in mid-air | Explicitly invalidate `_lastGroundedAt = float.NegativeInfinity;` on jump take-off |
| **GP-03** | Player Events | Multi-fire spam of `LeftGround` (6 frames) & dropped `Landed` on short hops | `FixedUpdate` conflated `wasGrounded` with coyote window time delta instead of discrete state machine transitions | Re-anchored `LeftGround` and `Landed` to state transitions in `StateMachine.Changed` (`Grounded <-> Airborne`) |
| **GP-04** | Player Combat / Defense | Hitstun cancellation exploit & dodge-out-of-hurt | `TryStartDodge()` and `TryStartAttack()` did not check `PlayerStateId.Hurt` or `PlayerStateId.Dodge`, allowing players to bypass `_hurtLockout` on frame 1 | Added guards against `Hurt`, `Dodge`, and `Dead` states in `TryStartDodge()` and `TryStartAttack()` |
| **GP-05** | Player Combat | Chained combo attacks failed to apply forward lunge impulse | Lunge application was isolated in `TryStartAttack()` which only executes for the first hit; `AttackRunner.Tick` begins combo follow-ups via `Begin(NextInCombo)` | Connected lunge application to `AttackRunner.AttackStarted` event so all attacks (initial and chained) receive authored lunges |
| **GP-06** | Combat Feedback | Lack of hitstop impact pause / crunch on weapon connects | `AttackRunner` had no impact freeze mechanism, causing hits to resolve instantly with no mechanical weight | Added `_hitstopRemaining` (0.05s / 50ms) pause to `AttackRunner` timeline upon landing hits; dampened player velocity in `PlayerCombat.OnHitLanded` |
| **GP-07** | Enemy AI | Complete enemy blindness; all enemies in levels were inert | `EnemyFactory.Create` initialized enemies with `player = null`, and `LevelDirector.Initialize` never passed `player.transform` to spawned enemies | Added `EnemyController.SetTarget(Transform)` and wired player target to all level enemies in `LevelDirector.Initialize`, `RestartRegion`, and `PerformRespawn` |
| **GP-08** | Enemy Presentation | `EnemyTelegraph` never animated or reflected state changes | `AddComponent<EnemyTelegraph>()` ran `Awake()` before `Configure()` was invoked, so `_controller` was null in `Awake` and events were never subscribed | Added event subscription in `EnemyTelegraph.Configure(renderer, controller)` to bind `StateChanged` and `PlayerSpotted` |
| **GP-09** | Enemy AI | Chase state permanent attack lockout; enemy ran through player | When in `InAttackRange()`, enemy was moving at full speed and checked `VelocityX < MoveSpeed * 0.5f` without braking, repeatedly skipping `TryBeginAttack()` and continuing full-speed movement | When in attack range, enemy actively brakes (`Move(0f, dt)`), settling into attack stance before executing `TryBeginAttack()` |
| **GP-10** | Enemy Physics | Ambush dropper corrupted 0-gravity physics engine model | `AmbushState` toggled `gravityScale = 1f`, conflicting with `EnemyMotor2D`'s zero-engine-gravity design and causing doubled gravity (-42 m/s² + engine gravity) | Maintained `gravityScale = 0f` invariant; ambush perch holds in mid-air and transitions to `ChaseState` which handles custom physics step gravity |
| **GP-11** | Enemy Physics & Damage | Ground probe offset misplacement & missing vertical damage lift | `RefreshGrounded` subtracted 0.5m offset from `bounds.min.y` instead of center; `EnemyHealth.TakeDamage` ignored `damage.LiftSpeed` | Fixed ground probe origin to collider base; added `VelocityY` / `SetVelocity` to `EnemyMotor2D` and applied vertical pop on hit |
| **GP-12** | Mobile Controls | Safe area notch / cutout desynchronization of touch glyphs | `TouchControlsView.Place` used raw screen fractions to position sprites in camera world space, while `CircleHit` hit-testing applied safe area insets | Screen pixel coordinates from `ToScreen(fraction)` are converted through viewport coordinates in `Place`, aligning visual glyphs with touch target geometry across all device cutouts |

---

## 2. Detailed Technical Architecture

### 2.1 Player State Machine & Jump Arc Mechanics
The player's state machine enforces strict invariants:
- **Variable Jump Height**: Jump cut reduces upward velocity once by `JumpCutMultiplier` upon button release. Subsequent physics frames during the ascent are not repeatedly multiplied.
- **Ledge Forgiveness (Coyote Time)**: Walking off a platform retains the grounded state for up to `CoyoteTime` seconds. However, once a jump take-off occurs, the coyote timestamp is cleared, preventing double ground jumps in mid-air.
- **Jump Buffering**: Inputs pressed within `JumpBufferTime` before landing are stored as timestamps and executed immediately upon grounding.

### 2.2 Combat Pipeline & Hit Resolution
- **Attack Timelines**: Melee attacks follow three deterministic windows: Startup (wind-up telegraph), Active (hitbox overlap query), and Recovery (lockout with combo input window).
- **Combo Chains**: When chaining from `Attack.Strike` to `Attack.StrikeFollowUp`, `PlayerCombat` listens to `AttackRunner.AttackStarted` to consistently apply forward lunge momentum for every strike in the chain.
- **Hitstop & Impact Weight**: Landing an attack on a valid target triggers a brief 50ms hitstop window in `AttackRunner` and velocity dampening in `PlayerCombat`, providing tactile crunch without desynchronizing physics state.

### 2.3 Enemy Perception & Behavior
- **Target Tracking**: `LevelDirector` binds the instantiated `PlayerController.transform` to all enemies via `EnemyController.SetTarget()`.
- **Perception Box & Obstruction**: Sight lines use forward-facing bounds and linecast checks against `GameplayLayers.Ground` to guarantee enemies cannot see or attack through solid walls.
- **Chase & Attack Commitment**: Chasing enemies entering attack range decelerate to settle their footing before initiating attacks, ensuring readable telegraphs and fair player counter-play windows.

### 2.4 Mobile Touch Controls & Safe Area Geometry
- **Layout Invariants**: Preserves independent left/right directional touch circles and distinct jump, attack, and dodge touch zones. Virtual joysticks remain strictly prohibited.
- **Cutout Invariance**: `TouchControlsView.Place` computes screen-pixel anchors through `ToScreen()` and normalizes against screen resolution, ensuring visible button glyphs stay 100% aligned with hit-test circles on 16:9, 18:9, 19.5:9, 20:9 phones, and notched tablets.

---

## 3. Verification & Test Coverage

All repairs are validated by static contract suites, syntax analyzers, and automated PlayMode tests:
1. `Assets/Aether/Code/Aether.Tests.PlayMode/Tests/PlayerMovementTests.cs`:
   - `JumpCut_AppliedOnce_DoesNotDecayIndefinitely`
   - `JumpFromGround_ClearsCoyoteWindow`
   - `JumpBuffering_FiresWhenGrounded`
   - `StateEvents_FireExactlyOncePerTransition`
   - `HurtState_LocksOutDodgeAndAttack`
2. `Assets/Aether/Code/Aether.Tests.PlayMode/Tests/CombatPipelineTests.cs`:
   - `CombatComboChain_AppliesFollowUpLunge`
   - `EnemyDamage_AppliesVerticalLiftSpeed`
   - `Enemy_ReceivesPlayerTarget_CanPerceivePlayer`
   - `Enemy_ChaseBrakesWhenInAttackRange`
3. Repository Gate Suite:
   - `verify.py`: 157 files analysed, 260 types modelled, 0 errors.
   - `gameplay_runtime.py`: PASS across camera, pause, controls, and RTL contracts.
   - `touchlayout.py`: 24/24 device shapes sound, 0 problems.
   - `levelcheck.py`: 8/8 traversal claims proven on The Greenway.
   - `mainmenu.py`: 714/714 checks passed.
   - `localization.py`: 10/10 languages verified.
   - `content_advisory.py`: PASSED.
   - `intro.py`: 140/140 synthetic device shapes sound.
