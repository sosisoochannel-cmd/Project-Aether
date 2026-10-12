# Varellon Signature Motion — Production Plan

Status: first code prototype implemented; static CI gates pass. Rendered visual/audio review and real Android-device validation remain outstanding. No APK build has been started.
Target: existing Varellon studio mark in `Assets/Aether/Resources/Brand/VarellonLogo.png`.
Hard constraint: preserve the supplied artwork and its wordmark. Animation may derive movement from isolated visual regions, but must not redraw, distort, recolour permanently, or replace the logo.

## Creative objective
Create a memorable studio signature through authored movement, weight, timing and synchronized sound—not a generic fade/scale reveal. Learn principles from cinematic game-studio marks without copying their artwork, exact choreography, sound, or distinctive signature.

Working concept: **Rift-Lock**. The mark and wordmark gain brief kinetic independence, travel from opposing directions, and return to a precise, satisfying locked composition. The current prototype uses the existing two-region split only; it does not fracture the artwork or create new logo geometry. The final frame is the original artwork, clean and undistorted.

## Sequence (current prototype: approximately 2.95 seconds; timings must be judged from real renders)
1. **Tension / negative space (0.00–0.30s):** black frame. No logo fade-in.
2. **Emblem impulse (0.30s onward):** the mark enters from a lateral offset with a stronger yaw/roll turn and a diagonal matte reveal. A first synthesized transient is placed near the mark's compression beat.
3. **Wordmark response (0.55–1.15s):** the wordmark moves in from the opposing lateral direction with its own yaw/roll, so the two pieces visibly converge rather than fading together. This is a two-region animation, not a claim that the monogram itself has been split into individual strokes.
4. **Return and lock (approximately 0.75–1.15s):** each moving region uses a short, controlled compression curve before returning to exact resting scale and position. A second synthesized transient marks the wordmark's convergence.
5. **Signature resonance (1.24–1.56s):** the existing low-intensity highlight traverses the ink mask after the lockup has settled.
6. **Hold (1.56–2.25s):** the logo is fully still and unobscured.
7. **Exit and handover (2.25–2.95s):** the current prototype fades to black before loading the next scene. This exit is retained for compatibility with the existing scene flow; the next visual review should decide whether it deserves to remain.

These timings are an initial storyboard, not proof of quality. Adjust after rendered playback on-device.

## Motion principles
- Clear anticipation → impulse → travel → impact → recovery → stillness.
- Distinct acceleration and deceleration curves; no uniform linear movement except where intentionally mechanical.
- Use overlapping action and small staggered offsets; never animate every element in identical synchrony.
- Every movement must have a readable cause and direction.
- One primary event, one secondary accent, one final signature. No visual noise.
- No copied Ubisoft swirl, Minecraft block construction, or other studio's recognizable motion/sound.

## Audio direction
Build an original layered sonic identity:
- a falling sub-bass body for weight, with a fast, controlled decay;
- a coherent D/A/D/F#/A harmonic bed that blooms rather than holding a flat chord;
- a quiet filtered-air rise to support movement without a constant hiss;
- two rounded, frame-aligned lock accents with a restrained upper transient;
- a brief musical resonance and natural early-reflection tail, then silence.
The implementation avoids harsh clicks, loud noise, obvious slapback and uncontrolled peaks. It remains procedural and original, with no third-party recording. Sound events must still be judged against rendered motion and listened to on actual phone speakers/headphones before approval.

## Technical implementation in Unity
- Keep changes isolated to the studio-intro flow and brand resources. Do not touch the gameplay, level, localization, menu redesign, or build settings in this pass.
- Inspect source texture alpha, dimensions, import settings and visual silhouette before choosing the segmentation strategy.
- Prefer cached textures/materials and a small number of renderers. No per-frame texture readback or allocations. Keep shader work simple and mobile-friendly; avoid full-screen post-processing and high-overdraw particle layers.
- Use deterministic timeline/easing values and a clear skip path that cleans up generated objects, materials, textures and audio.
- Preserve safe-area fit and final lockup proportions across portrait/landscape and multiple aspect ratios.
- Provide a fallback path if shaders or source texture readability are unavailable; never leave a blank permanent screen.
- Keep the existing scene flow and handoff semantics intact.

## Acceptance gates (all required before claiming done)
1. Source artwork remains unchanged and the final hold visually matches its original silhouette and wordmark.
2. No visible seams, jagged cuts, alpha halos, clipped edges, or accidental stretching.
3. Timing reads at both 30 and 60 fps; impact and sound are synchronized.
4. Skip works immediately and performs complete cleanup.
5. No allocations or texture CPU readbacks in the per-frame animation loop.
6. Render at representative mobile aspect ratios; check small screens and safe areas.
7. Test on an actual Android device before considering a release build.
8. Compare a clean render against the previous intro. Reject if it is merely more effects but has no stronger movement, weight, or memorable audio identity.
9. Build only after the implementation and verification gates pass; no APK should be described as tested without a real successful workflow and artifact.

## Current implementation and next engineering steps
- Implemented on branch `arena/varellon-rift-lock`: opposing lateral movement, stronger dimensional yaw/roll, subtle opposing counter-arcs, controlled lock compression, and two synthesized impact transients aligned to the compression peaks (0.572s and 0.778s into the reveal sting).
- Latest audio revision replaces the static, sine-heavy chord/noise blend with integrated oscillator phases, a falling low-frequency body, a coherent evolving harmonic bed, a shaped filtered-air layer, two rounded lock accents, a restrained upper resonance, quiet reflections, and peak normalization.
- A render workflow has been triggered for the updated intro; this is not an Android APK build.
- Not yet established: the sound's subjective quality in actual playback, visual/audio synchronization in a rendered video, performance on a physical Android device, or a release APK. Do not claim final audio approval until rendered playback is reviewed and listened to on phone speakers/headphones.
