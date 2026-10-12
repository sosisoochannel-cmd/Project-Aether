# AETHER — Full Quality Audit (APK builds paused)

**Status:** First-pass repository audit started. This document records source-level evidence, not claims of a successful Unity/device test. Do not publish or request an Android APK until the release gates at the end are met and the project owner explicitly asks to build.

## Verified first-pass findings

### P0 — Visual identity and in-game presentation are not production-ready
- The player factory assigns `PlaceholderVisuals.Square` as the player's sprite.
- The enemy factory also assigns `PlaceholderVisuals.Square`; enemy silhouettes are not distinct.
- `PlaceholderVisuals` explicitly documents itself as a temporary flat-colour shape generator.
- The current tracked image inventory contains the Varellon logo and a menu forest background, but no production player/enemy sprites, animation clips/controllers, tileset, or gameplay VFX assets were found in the source tree.
- Consequence: even if movement/combat systems work, the moment-to-moment game cannot yet meet the intended hand-drawn atmospheric action-platformer bar. More menu polish cannot solve this.

### P0 — Audio is not a complete game-wide system yet
- The Varellon intro now synthesizes its own short cue in code, but that is only a studio-ident cue.
- The tracked asset inventory has no gameplay music/SFX files. The milestone documentation describes gameplay sound/music as placeholder hooks.
- Consequence: do not treat the new logo sting as completion of AETHER's sound design.

### P1 — Content breadth and production state need honest scoping
- The Greenway has source-of-truth grid data and runtime construction code, and a Forest Stalker definition exists. This is meaningful gameplay scaffolding, not proof that the whole game is complete.
- The README and milestone document describe several planned regions, the boss, Rootbind/backtracking, and other milestones as unfinished. The menu may expose screens or progression hooks for systems whose full content is not yet present.
- Every menu destination must be classified as fully implemented, deliberately locked, or clearly marked as unavailable; no decorative button should imply finished content that is not playable.

### P1 — Verification gap
- The latest localization workflow passed for commit `f5b3db745b0b855d290e08b785d06fefdc060b65`.
- The intro-render and Android workflows also completed successfully for that commit. These results prove only that those jobs completed; they do not establish visual quality, good controls on a real phone, gameplay balance, or production readiness.
- The project documents explicitly warn that static checks are not a substitute for running the game in Unity Play Mode. Device-level touch, safe-area, text shaping, audio, performance and persistence still require verification.

## Audit plan — do not skip stages

1. **Presentation inventory:** inspect every scene, menu, backdrop, level visual, character/enemy visual, VFX and transition. Replace flat placeholder forms with a coherent art direction; use actual in-game captures/renders to judge the result.
2. **Core feel:** inspect movement, jump arc, coyote time, jump buffering, dodge, attack wind-up/active/recovery, hit feedback, enemy telegraphs, knockback, damage and death/respawn. Add focused Edit/Play Mode tests for bugs and edge cases.
3. **Touch controls:** verify thumb reach, button hit targets, simultaneous inputs, screen aspect ratios, safe areas, pause/resume and input cancellation when UI overlays open.
4. **Level quality:** inspect collision/grounding, jumps and landings, checkpoints, secrets, exits, enemy placement, encounter pacing, camera framing, navigation readability and failure recovery. The level must be fun to traverse, not merely parse successfully.
5. **Menus and UX:** verify the whole first-launch flow, language selection and persistence, RTL shaping, ten language tables, navigation/back behaviour, settings persistence, save slots, continue/new-game prompts, chapter locks, accessibility and every advertised destination.
6. **Audio and feedback:** define the audio palette and implement coherent music, ambience, UI, combat, enemy and environmental cues. Respect master/music/SFX settings, pause/mute, mobile audio focus and lifecycle transitions.
7. **Engineering/performance:** inspect lifecycle cleanup, duplicated objects, event subscriptions, pooling, allocations, save corruption recovery, null/missing content handling, build settings, Android orientation, quality tiers, frame pacing and low-memory behaviour.
8. **Evidence:** run static verifiers and Unity compilation/tests where possible; review rendered screenshots and logs; test on representative Android hardware when available. Record what was and was not actually verified.

## Release gates

- [ ] No player/enemy/terrain made from generic square placeholders in the intended playable slice.
- [ ] A coherent art direction across characters, terrain, environment, UI and VFX.
- [ ] Gameplay audio system and settings behave correctly; intro audio is not the only finished cue.
- [ ] Main gameplay loop and mobile controls pass repeatable tests.
- [ ] Menu/localization/save/scene-transition regression checks pass.
- [ ] Unity import/compile and Play Mode tests are confirmed, not inferred from static checks.
- [ ] Rendered frames reviewed for visual defects; no known blocking bugs.
- [ ] Android runtime smoke/performance checks pass.
- [ ] Only then, and only after explicit approval, build a candidate APK.

## Build policy during this audit

The Android APK workflow has been changed to manual dispatch only. Source edits during this audit should not automatically consume Android build minutes or publish another misleading development APK. This is a pause on APK delivery, not a claim that the existing code is production-ready.
