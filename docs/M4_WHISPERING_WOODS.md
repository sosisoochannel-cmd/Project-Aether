# M4 — Whispering Woods

This branch adds the second region as source-of-truth level data and begins the presentation pass
without changing the Greenway runtime, player rules, enemy implementations, or save contracts.

## Design contract

The region is intentionally a layout/content milestone, not a new mechanics milestone.

- Entry is readable and initially walkable.
- The route becomes narrower and more enclosed than The Greenway.
- Encounters alternate with breathing space.
- The Silent Zone is a real authored story marker with a stable semantic id; it does not invent an audio system.
- The first discovery is off the obvious route and requires exploration.
- A second checkpoint is established before the region exit.
- The exit is not inside an enemy's detection area.
- No new ability is required to complete the region.
- The shortcut is reserved as a progression-space hook; the first data pass does not pretend that a
  return teleporter or persistent shortcut exists before the runtime has a real representation for it.

## Presentation pass

`WhisperingWoodsAtmosphere` now reads the `silent_zone` story marker from level data and creates
a localized, feathered two-layer mist around that location. The radial texture is generated once at
runtime at 64×64, reused by both mist layers, and released with the atmosphere component. Fireflies
gradually shift from warm green to a dimmer, cooler tint as they approach the zone. The treatment is
deterministic, presentation-only, and does not change collision, enemy behaviour, or traversal.

This remains a procedural placeholder treatment, not final environment art. It is intended to make
the authored story space readable while the traversal and encounter layout are validated; it should
not be used to conceal a route or combat problem.

## Verification

The independent data gate is:

```sh
python3 tools/verify/levelcheck.py Assets/Aether/Resources/Levels/region2.whispering_woods.level.txt
```

A green static result proves only the data/model checks. Unity import, compilation, Play Mode, visual
quality on a phone, and Android behaviour remain separate verification levels. The Silent Zone
presentation change has been committed, but no Unity Editor test or Android build was run for it.

## Next M4 pass

After the data gate is green, inspect traversal and encounter readability before extending the art
pass. Then improve the placeholders with layered forest silhouettes, readable foreground occlusion,
and consistent checkpoint/landmark language. Keep all environmental effects inexpensive on mobile.

The rule is content before decoration: no art pass is allowed to hide a traversal or encounter
problem.
