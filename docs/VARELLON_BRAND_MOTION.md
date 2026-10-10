# Varellon Brand Motion

## Intent

Varellon is the studio identity behind Aether. The logo must read as a designed signature, not a showcase of effects. The motion language is therefore limited to one controlled arrival, one masked light pass, and a barely perceptible idle drift. No particles, lens flares, noisy glitching, spinning, or repeated shine loops are part of the identity.

## Main-menu mark

- The approved logo artwork is still the source of truth; its pale background is keyed out and its ink coverage becomes the alpha mask.
- The mark arrives with a short eased rise and a small settle.
- A single sage highlight crosses only the existing logo alpha. It is a UI shader, clipped to the logo's own sprite, not a free-standing bar.
- The highlight uses a private runtime material and two scalar parameters. It does not rebuild or upload a texture every frame.
- After the pass, the logo rests with only a very low-amplitude drift and restrained halo.
- Reduced Motion removes the movement and sweep. The logo itself remains visible and readable.

## App-start ident

- The mark leads; the wordmark completes the lockup a beat later.
- A single diagonal, soft highlight crosses a downsampled static alpha mask. A dedicated sprite shader animates the band on the GPU; the mask is not rewritten per frame.
- The pass is narrower and less intense than the former broad wash. The mark's luminance dip remains subtle, so the logo itself stays the hero.
- The sequence still respects the existing under-three-second timing budget, skip behavior, safe-area fit, and cleanup before scene handover.

## Runtime and lifecycle

Both shader assets are under `Assets/Aether/Resources/Brand` and are loaded from Resources so they remain discoverable in a player build. Each logo presentation owns its runtime material and destroys it with the presentation. The source PNG remains untouched. The intro sheen mask is generated once at a capped resolution; the menu sheen samples the already-generated logo sprite.

## Verification status

Run `python3 tools/verify/brand_motion.py` for the static regression gate. It checks the shader/resource names, logo-alpha masking, Canvas stencil and clip-rect contracts, absence of per-frame pixel uploads, runtime material cleanup, Reduced Motion retention, and the restrained intro timing/shine constants. This gate is intentionally source-level and does not claim a Unity render test.

Static source checks confirm the shaders are referenced by their Resources paths, the sweep samples logo alpha, the UI shader preserves Canvas stencil and clip-rect behavior, material cleanup exists, and neither animation rewrites its pixel buffer every frame. The intro's light constants remain inside the limits checked by `tools/verify/intro.py`, and its overall timing remains within the 2.8–3.0 second gate.

**Not yet verified:** Unity shader compilation, rendered appearance, frame-time on a physical Android device, and an actual player build. These require running the Unity project; no APK has been built as part of this change.
