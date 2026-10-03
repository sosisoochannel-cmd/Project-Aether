# Project Aether

Foundation repository for a professional 2D action-platformer built with Unity.

> **Status: Milestone 1 of The Rootbound Wilds — core architecture and the player.**
> There is still **no playable level, no enemies, no boss and no art** in this repository.
> What exists is the foundation plus the systems the region will be assembled from.
> See [`docs/MILESTONES.md`](docs/MILESTONES.md) for what is done and what is next, and
> [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the decisions behind the structure.

## Milestone status

| | Milestone | State |
|---|---|---|
| M0 | Unity 6.3 LTS project foundation (2D, URP 2D Renderer, Android) | ✅ done |
| M1 | Core architecture, player traversal + combat, camera, progression hooks | ✅ done |
| M2 | Level pipeline + The Greenway (next) | ⬜ |
| M3 | Enemy archetypes + encounter integration | ⬜ |
| M4 | Whispering Woods + shortcut | ⬜ |
| M5 | Old Settlement + NPC hook + secrets | ⬜ |
| M6 | Deep Forest + environmental storytelling | ⬜ |
| M7 | Root Guardian arena + three-phase boss | ⬜ |
| M8 | Rootbind unlock + gated backtracking | ⬜ |
| M9 | Region exit + full audit + touch controls | ⬜ |

Nothing in this repository has been run in Play Mode. No Unity Editor exists in the
environment it was authored in, so every "verified" claim in the docs is a claim about
static analysis, and is labelled as such.

---

## Engine

| | |
|---|---|
| **Unity** | `6000.3.24f1` (Unity 6.3 LTS, revision `4e7b9b5b6244`) |
| **Render pipeline** | Universal Render Pipeline (URP) `17.3.0` + **2D Renderer** |
| **Primary platform** | Android |
| **Colour space** | Linear |
| **Scene template** | 2D (`EditorSettings.m_DefaultBehaviorMode = 1`) |

Unity 6.3 LTS is the supported choice for this project: Unity 6.0 LTS reaches
end-of-support on 2026-10-16, while 6.3 LTS is supported until December 2027.

---

## Opening the project

1. Install **Unity 6000.3.24f1** — use *Unity Hub → Installs → Install Editor → Archive*,
   or download it from the Unity release archive. (Any other `6000.3.x` patch will also
   open the project; Unity will simply offer to upgrade the project version.)
2. In Unity Hub choose **Add → Add project from disk** and select this repository root.
3. Open the project. First import pulls packages from `packages.unity.com` and generates
   `Library/` (git-ignored). No compile errors are expected — there is no first-party code yet.

## Building for Android

1. Install the **Android Build Support** module (with *OpenJDK* and *Android SDK & NDK Tools*)
   for this Editor version via Unity Hub.
2. The project is already configured for Android (see below). Select the Android platform
   and build: **File → Build Profiles → Android → Build**.
3. The initial scene, `Assets/Aether/Scenes/Boot.unity`, is the only scene in the build list
   and is the app entry point.

---

## Repository layout

```
Assets/
├── Aether/                     First-party project content — the only place game work goes
│   ├── Art/                    Sprites, tilesets, materials, shaders, animation clips
│   ├── Audio/                  Music, SFX, mixers
│   ├── Code/                   All first-party C# — one folder per assembly
│   │   ├── Aether.Core/        No dependencies. Events, state machine, pooling, combat
│   │   │                       primitives, progression, physics-query isolation
│   │   ├── Aether.Data/        ScriptableObject configuration. Data only, no behaviour
│   │   ├── Aether.Gameplay/    Runtime behaviour. Depends on Core + Data
│   │   ├── Aether.Editor/      Editor-only tooling. Never ships
│   │   └── Aether.Tests.EditMode/
│   ├── Data/                   ScriptableObject *asset instances* (not definitions)
│   ├── Prefabs/                Reusable composed GameObjects
│   ├── Scenes/                 Boot.unity — the app entry scene
│   └── Settings/               URP pipeline assets (see below)
└── ThirdParty/                 Imported third-party packages/assets, kept out of Aether/
Packages/
└── manifest.json               Package + module set (see "Packages" below)
ProjectSettings/                Unity project configuration
docs/                           Architecture and milestone records
tools/verify/                   Static verification. Not part of the Unity project
```

`Assets/Aether/**` is the first-party tree: everything we author lives there, so it is always
unambiguous which files are ours and which were imported. Keep the tree shallow — a new folder
is only justified by real content, not by anticipation.

### Code layers

`Aether.Core` must never depend on gameplay; `Aether.Data` must never contain behaviour.
Both rules are enforced mechanically by the verifier, not by convention. See
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) §1 for the reasoning and for when to promote a
system to its own assembly.

### URP assets — `Assets/Aether/Settings/`

| Asset | Purpose |
|---|---|
| `Renderer2D.asset` | The **2D Renderer** data (`Renderer2DData`), no custom renderer features |
| `URP-Aether.asset` | The `UniversalRenderPipelineAsset`; points at `Renderer2D.asset` |
| `UniversalRenderPipelineGlobalSettings.asset` | URP global settings (shader stripping, default resources) |
| `DefaultVolumeProfile.asset` | Empty default post-processing volume profile |

There is deliberately **one** pipeline asset today. Creating low/medium/high URP variants is a
data-driven decision to make once there is content to measure; doing it now would be arbitrary.

---

## Android configuration

Configured in `ProjectSettings/ProjectSettings.asset`:

| Setting | Value | Note |
|---|---|---|
| Application identifier | `com.DefaultCompany.ProjectAether` | **Placeholder — see "Needs a human decision"** |
| Target architectures | `ARMv7 + ARM64` | ARM64 is required by Google Play; ARMv7 retained for device coverage |
| Minimum API level | 25 (Android 7.1) | Unity 6.3 default |
| Target API level | 0 = *Auto (highest installed)* | Avoids pinning an SDK level that Play would later reject |
| Graphics APIs | Vulkan, with OpenGLES3 fallback | Unity 6.3 URP template default |
| Bundle version | `0.1.0` / code `1` | |
| Orientation | Auto-rotation (all four) | **Not yet decided — see "Needs a human decision"** |
| Scripting backend | IL2CPP (Unity default for Android) | |
| Keystore | none committed | Supply release signing credentials via CI/local config |

`Packages/manifest.json`, `ProjectSettings/GraphicsSettings.asset` and
`ProjectSettings/QualitySettings.asset` complete the pipeline wiring.

## 2D configuration

- `EditorSettings.m_DefaultBehaviorMode = 1` — new scenes/assets default to **2D**.
- `GraphicsSettings.m_TransparencySortAxis = (0, 0, 1)` and sprite transparency sorting left at defaults.
- `Physics2DSettings.asset` is pinned at Unity's defaults (gravity `-9.81`, 8 velocity / 3 position
  iterations). No tuning applied — solver tuning is a gameplay-feel decision.
- The initial scene has no skybox, baking disabled, and a single **orthographic** camera
  (size `5`) with an `AudioListener`. Nothing else.

## Packages

Deliberately minimal. Only packages that cannot be retrofitted cheaply were added:

| Package | Why |
|---|---|
| `com.unity.render-pipelines.universal` `17.3.0` | The chosen render pipeline. Provides the 2D Renderer (2D lights/shadows) |
| `com.unity.inputsystem` `1.20.0` | Mobile input backend. Changes the project-wide *Active Input Handling* setting, so adopting it later is disruptive rather than cheap |
| `com.unity.ide.rider`, `com.unity.ide.visualstudio` | IDE project generation for the team. No runtime footprint |
| `com.unity.test-framework` `1.6.0` | Test infrastructure for a long-lived codebase |
| `com.unity.ugui` `2.0.0` | Unity 6 baseline package (also hosts TextMeshPro) |
| `com.unity.modules.*` | Unity's default built-in module set, kept whole. Trimming modules is a build-size optimisation that should be driven by measurements, not guesswork |

Deliberately **not** included: `com.unity.feature.2d` (2D tooling such as Tilemap Extras,
Sprite Shape and Pixel Perfect Camera), Cinemachine, Timeline, ProBuilder, Visual Scripting,
Unity Version Control and the multiplayer packages. Each is a one-click addition from the
Package Manager with no migration cost, so they are deferred until a system actually needs them.

`packages-lock.json` is intentionally absent; Unity generates it on first open. Commit it
afterwards so dependency resolution is reproducible across machines and CI.

---

## Architecture conventions

The full reasoning is in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). The short version:

- **Tuning is data, never constants.** Movement, attacks and every forgiveness window live in
  ScriptableObjects under `Aether.Data`, so designers iterate without touching code paths.
- **Timing is absolute, not counted down.** Forgiveness windows are `Time.time` comparisons.
  A countdown decremented in `FixedUpdate` and read in `Update` loses time on a throttling
  phone, and the player experiences it as an input that randomly did not register.
- **Non-obvious constants carry their reasoning in a comment.** Where a value encodes a
  deliberate design decision, the comment explains the decision, not the syntax.
- **Events for state changes, direct calls for per-frame work.** `EventBus` is for ability
  unlocks, checkpoints and boss phases — never for traffic that happens every frame.
- **No cross-system reach-through.** Systems communicate through interfaces and the event bus;
  nothing first-party depends on `Assets/ThirdParty` internals without a wrapper.
- **No architecture for systems that do not exist yet.** Definitions are written when the
  system that needs them is written, not in advance.
- **`Assets/Aether/**` is ours.** Nothing outside it is first-party code.

---

## Needs a human decision

These were intentionally left unset rather than guessed. Each is cheap to change now and
expensive later:

1. **Company name and application identifier.** Currently Unity's `DefaultCompany` placeholder →
   `com.DefaultCompany.ProjectAether`. A real reverse-DNS bundle ID must be chosen *before* the
   first Play Console upload, because it is permanent once published.
2. **Screen orientation.** Left at Unity's auto-rotate default. An action-platformer normally
   ships landscape-only; that is a design decision, not a technical default.
3. **Minimum API level / device floor.** `25` is Unity's default. A real device floor should
   follow from a performance target device.
4. **Quality tiers.** All six built-in quality levels currently point at the same URP asset.
   Differentiating them (and creating low-end URP assets) should happen once there is content
   and a target device to profile.
5. **Splash screen / branding.** Left at Unity defaults.

## Verification

Two gates, both runnable without a Unity licence and both suitable for CI:

```bash
python3 tools/verify/gen_meta.py --check   # every asset under Assets/ has a .meta
python3 tools/verify/verify.py             # assets, assemblies, and project-type symbols
```

`verify.py` checks asset GUID uniqueness and reference resolution, `.meta` coverage, assembly
definition validity (references, cycles and per-file namespace visibility), bracket balance,
and — most usefully — that **every member accessed on a project-owned type actually exists,
with a compatible argument count**. It is deliberately scoped to project-owned receivers only,
so it produces no false positives; the cost is that it cannot validate Unity API calls.

It is validated against **negative controls** (deliberately injected errors it must catch), so a
green run means something. It found two real compile errors in the M1 player code.

### What is *not* verified

**No Unity Editor exists in the environment this project is authored in.** Therefore:

- Unity API signatures are **not** validated by anything here — only a real compile can do that.
- Play Mode behaviour is **not** tested. No claim of "play tested" is made anywhere.
- The first Editor import is the real test and is expected to be the first place an API
  mismatch would surface. Treat that import as a required step, not a formality.
