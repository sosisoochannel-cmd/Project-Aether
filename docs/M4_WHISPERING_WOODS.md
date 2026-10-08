# M4 — Whispering Woods

This branch is the first content pass for M4. It adds the second region as data without changing
the already-working Greenway runtime, menu flow, player rules, enemy implementations, or save
contracts.

## Design contract

The region is intentionally a layout/content milestone, not a new mechanics milestone.

- Entry is readable and initially walkable.
- The route becomes narrower and more enclosed than The Greenway.
- Encounters alternate with breathing space.
- The Silent Zone is a story marker with a stable semantic id; it does not invent an audio system.
- The first discovery is off the obvious route and requires exploration.
- A second checkpoint is established before the region exit.
- The exit is not inside an enemy's detection area.
- No new ability is required to complete the region.
- The shortcut is reserved as a progression-space hook; the first data pass does not pretend that a
  return teleporter or persistent shortcut exists before the runtime has a real representation for it.

## Verification

Run the existing independent gate against the new source-of-truth file:

python3 tools/verify/levelcheck.py Assets/Aether/Resources/Levels/region2.whispering_woods.level.txt

A green static result proves only the data/model checks. Unity import, Play Mode and Android
behaviour remain separate verification levels and must not be described as tested until they are.

## Next M4 pass

After the data gate is green, the next change should be the smallest runtime seam that makes the
region selectable from the existing flow. Only then should presentation replace the placeholders:
forest silhouettes, layered canopy, readable foreground occlusion, checkpoint language, and a
distinct Silent Zone treatment.

The rule is content before decoration: no art pass is allowed to hide a traversal or encounter
problem.
