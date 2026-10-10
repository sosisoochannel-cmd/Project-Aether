# Varellon Brand Motion

## Intent

Varellon is the studio identity behind Aether. The logo must read as a designed signature, not a showcase of effects. The motion language is built as a short choreography: a soft diagonal matte reveal, a delayed wordmark arrival, one masked light pass, and a barely perceptible menu idle drift. Each phase has a different job; there are no particles, lens flares, noisy glitches, spinning, or repeated shine loops.

## Main-menu mark

- The approved logo artwork is still the source of truth; its pale background is keyed out and its ink coverage becomes the alpha mask.
- The mark arrives with a short eased rise and a small settle.
- A single sage highlight crosses only the existing logo alpha. It is a UI shader, clipped to the logo's own sprite, not a free-standing bar.
- The highlight uses a private runtime material and two scalar parameters. It does not rebuild or upload a texture every frame.
- After the pass, the logo rests with only a very low-amplitude drift and restrained halo.
- Reduced Motion removes the movement and sweep. The logo itself remains visible and readable.

## App-start ident

- The mark leads; the wordmark completes the lockup a beat later.
- Each part is uncovered by a soft diagonal matte that follows the same eased progress as its fade, rise, and scale settle. The matte is clipped by the source alpha, so it reveals the logo's own silhouette rather than drawing a wipe across the black screen.
- The wordmark starts 250 ms after the symbol, preserving a deliberate hierarchy instead of animating the entire lockup as one flat image.
- After the reveal resolves, one diagonal, soft highlight crosses a downsampled static alpha mask. A dedicated sprite shader animates the band on the GPU; neither the reveal nor the sheen rewrites a texture per frame.
- The pass is narrower and less intense than the former broad wash. The mark's luminance dip remains subtle, so the logo itself stays the hero.
- The sequence still respects the existing under-three-second timing budget, skip behavior, safe-area fit, and cleanup before scene handover.

## Runtime and lifecycle

The shader assets are under `Assets/Aether/Resources/Brand` and are loaded from Resources so they remain discoverable in a player build. The intro owns one private reveal material per rendered logo part and one material for the sheen; all are destroyed with the intro. The source PNG remains untouched. The intro sheen mask is generated once at a capped resolution; the menu sheen samples the already-generated logo sprite. The matte and sheen add only logo-sized transparent draws during the short intro, not full-screen post-processing or a particle system.

## Verification status

Run `python3 tools/verify/brand_motion.py` for the static regression gate. It checks the shader/resource names, logo-alpha masking, the matte's soft edge and progress wiring, Canvas stencil and clip-rect contracts, absence of per-frame pixel uploads, runtime material cleanup, Reduced Motion retention, skip continuity, and the restrained intro timing/shine constants. This gate is intentionally source-level and does not claim a Unity render test.

Static source checks confirm the shaders are referenced by their Resources paths, the sweep samples logo alpha, the UI shader preserves Canvas stencil and clip-rect behavior, material cleanup exists, and neither animation rewrites its pixel buffer every frame. The intro's light constants remain inside the limits checked by `tools/verify/intro.py`, and its overall timing remains within the 2.8–3.0 second gate.

**Not yet verified:** Unity shader compilation, rendered appearance, frame-time on a physical Android device, and an actual player build. These require running the Unity project; no APK has been built as part of this change.
