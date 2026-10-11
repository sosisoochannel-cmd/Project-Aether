# Varellon Signature Motion — Production Plan

Status: design/engineering specification only. No APK build requested or performed.
Target: existing Varellon studio mark in `Assets/Aether/Resources/Brand/VarellonLogo.png`.
Hard constraint: preserve the supplied artwork and its wordmark. Animation may derive movement from isolated visual regions, but must not redraw, distort, recolour permanently, or replace the logo.

## Creative objective
Create a memorable studio signature through authored movement, weight, timing and synchronized sound—not a generic fade/scale reveal. Learn principles from cinematic game-studio marks without copying their artwork, exact choreography, sound, or distinctive signature.

Working concept: **Rift-Lock**. The mark feels as if a dormant mechanism awakens, its visual regions briefly gain kinetic independence, then return to a precise, satisfying locked composition. The final frame is the original artwork, clean and undistorted.

## Sequence (target 4.2–5.0 seconds; tune only after reviewing real renders)
1. **Tension / negative space (0.00–0.35s):** black frame; restrained low tonal bed begins subtly. No logo fade-in.
2. **Impulse (0.35–0.75s):** a short, directional reveal starts from the mark's own geometry. A camera-space parallax/rotation illusion is permitted, but avoid a flat logo spinning like a card. Sound transient must begin at the visible impulse.
3. **Kinetic separation (0.75–1.45s):** isolate only regions that can be separated without visibly damaging the mark. Each region follows a distinct, short arc with controlled acceleration and a clear spatial direction. Avoid arbitrary shattering, debris, or excessive particles. If the supplied image does not support clean separation, do not fake it with ugly cuts; use a geometry-driven matte/occlusion and depth illusion instead.
4. **Return and lock (1.45–2.15s):** regions converge with staggered timing, arrive on a shared beat, compress subtly (scale change target under 3%), overshoot minimally, then settle. The final artwork must match source pixels/shape.
5. **Wordmark response (2.05–2.65s):** wordmark motion is a secondary response, not a separate generic slide. Keep original spacing and alignment at rest.
6. **Signature resonance (2.65–3.35s):** a controlled light/material response follows the lock, clipped to the logo's ink. It should clarify the form, not obscure it.
7. **Resolve and hold (3.35–4.5s):** a short, designed sonic tail; clean final logo held long enough to register. Do not automatically fade out unless the following scene requires it.

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
- a short low-frequency body for the impulse;
- a crisp, non-harsh transient at the lock;
- a restrained tonal/resonant layer that resolves into a distinct interval;
- a short natural tail with no stock-logo cliché.
Sound events must align to rendered motion frames, not merely to approximate timeline percentages. Keep peaks controlled, avoid clipping, test mono playback and phone speakers. Prefer authored original synthesis/recording over unlicensed sound assets.

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

## Next engineering steps
1. Inspect the existing sprite and import metadata; confirm whether mark and wordmark can be separated cleanly.
2. Create a branch dedicated to this motion work.
3. Implement the smallest robust prototype of the primary movement and impact sound.
4. Render, review, and tune; add secondary accents only when they improve readability.
5. Run existing verification and Android build workflow only after the visual and technical gates are satisfied.
