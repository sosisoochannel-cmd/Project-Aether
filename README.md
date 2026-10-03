# Project Aether

Foundation repository for a professional 2D action-platformer built with Unity.

> **Status: project foundation only.**
> No gameplay has been implemented. There are no player characters, enemies, combat,
> levels, story content, UI, art, VFX or audio in this repository — by design.
> This commit establishes a clean, buildable, correctly configured project that future
> work can be built on top of.

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
│   ├── Code/                   All first-party C# (assemblies / asmdefs live here)
│   ├── Data/                   ScriptableObject config & tuning data
│   ├── Prefabs/                Reusable composed GameObjects
│   ├── Scenes/                 Boot.unity — the app entry scene
│   └── Settings/               URP pipeline assets (see below)
└── ThirdParty/                 Imported third-party packages/assets, kept out of Aether/
Packages/
└── manifest.json               Package + module set (see "Packages" below)
ProjectSettings/                Unity project configuration
```

`Assets/Aether/**` is the first-party tree: everything we author lives there, so it is always
unambiguous which files are ours and which were imported. Keep the tree shallow — a new folder
is only justified by real content, not by anticipation.

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

These are the rules the foundation is built to support. None of them require code yet.

- **One feature, one folder.** A future system (e.g. movement, combat, save) owns a folder under
  `Assets/Aether/Code/` and, where useful, an assembly definition so it compiles independently
  of unrelated systems and cannot take accidental dependencies on them.
- **Data over code for tuning.** Tunables belong in `Assets/Aether/Data/` as ScriptableObjects,
  not as constants, so designers can iterate without touching code paths.
- **No cross-system reach-through.** Systems communicate through explicit interfaces/events;
  `Assets/Aether/Code` must never depend on `Assets/ThirdParty` internals without a wrapper.
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

## Verification status

This foundation was authored and validated by structural inspection, not by launching the
Editor — **no Unity Editor is available in the environment where it was created.** What was
checked: asset GUID references are self-consistent across all project and pipeline assets, no
asset references anything that is missing, `ProjectSettings` files use Unity 6.3 serialization
versions verified against real Unity 6.3 projects, every YAML document parses, and the initial
scene is registered in the build list. The first Editor launch should be treated as the final
confirmation step and is expected to report no errors.
