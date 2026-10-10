# Varellon Identity Refinement — Acceptance Brief

## Goal
Build a distinctive, premium studio identity for Varellon that feels intentional without depending on glow, gradients, or animation to look finished. A score of 95/100 is a target, not a claim; it is only awarded after visual inspection and practical scale tests.

## Current status
- `VARELLON_IDENTITY_STUDY_01.svg` is a new vector concept, not an approved replacement.
- The production PNG at `Assets/Aether/Resources/Brand/VarellonLogo.png` is intentionally untouched.
- Intro reveal and sheen source checks and the temporary Unity render workflow passed on commit `fa9abe12`.
- No APK was built.

## Non-negotiable design tests
1. **Ownable silhouette (20 pts):** one clear, recognizable mark; no decorative detail that does not strengthen the idea.
2. **Wordmark quality (20 pts):** exact spelling, confident proportions, optical kerning, deliberate tracking, clean hierarchy between VARELLON and STUDIOS.
3. **Small-size legibility (15 pts):** test at 256, 128, 64, and 32 px; the symbol must remain recognizable and the full lockup must not become a grey blur.
4. **Monochrome performance (10 pts):** solid black and solid white versions must remain balanced and legible.
5. **Light/dark adaptability (10 pts):** test on near-black, neutral mid-tone, and off-white backgrounds; contrast must not depend on a halo.
6. **Composition and spacing (10 pts):** optical centering, clear space, disciplined symbol-to-wordmark ratio, no awkward dead zones.
7. **Distinctiveness and fit (10 pts):** must feel like a serious game studio, not a generic esports badge or a stock tech startup.
8. **Production readiness (5 pts):** clean vector master, transparent exports, app-start lockup, compact icon, and documented colors.

## Current concept notes
- Palette direction: near-black graphite, restrained platinum, muted sage accent.
- Form direction: a geometric V with a single clear read and a compact accent cut; no star, sparks, extra rings, or gratuitous 3D bevels.
- Motion should reveal the actual mark; it must not hide weak geometry.
- Typography in the SVG is a system-font fallback stack for concept preview only. Before production approval, convert the chosen wordmark to vector outlines or select and verify a licensed font, then optically adjust spacing.
- This is one direction for review, not proof of a 95+ final identity. Compare it with at least one alternate based on the original logo before replacing the approved PNG.

## Required evidence before 95+
- View rendered SVG at full size and on a phone-sized preview.
- Compare against the existing mark side-by-side without animation.
- Produce small-icon and monochrome proofs.
- Check that the wordmark is spelled correctly and that typography renders consistently on the build machine.
- Review actual Unity render frames and confirm the production source uses the selected artwork.
- Keep the production PNG unchanged until the new design wins these tests.

## Research principles
Logo quality is not the number of effects. Strong silhouette, readable typography, scalable versions, and monochrome/background tests matter more than decorative polish. See the practical game-logo guidance and cross-size principles in:
- https://presskit.gg/field-guides/game-logo-design-guide
- https://www.gamedeveloper.com/art/designing-game-logos
- https://gdevelop.io/en-gb/blog/memorable-logo-design
