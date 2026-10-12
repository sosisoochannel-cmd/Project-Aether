# AETHER Language Selection — Gold / Black Cinematic Direction

## Product decision
The first screen after the Varellon studio ident is a dedicated language-choice scene. It is not the main menu and it must not show the main-menu forest backdrop. The background remains true black (#050505 / near-black only where required for antialiasing); gold is the sole accent family. Purple and blue are not used in this identity.

## Visual system
- Background: near-black, visually black at normal viewing distance; no scenery, particles, or decorative wallpaper.
- Accent: warm champagne gold (#D8B56D), restrained bright highlight (#F4DFA7), deep antique gold (#8B6B32). Avoid flat saturated yellow.
- Text: warm ivory for secondary instruction; primary native language name in champagne gold.
- Ornament: thin, symmetric, architectural linework with a restrained central crest / diamond motif. The frame must be sparse, balanced, and never compete with the language name.
- Composition: centered vertical stack; small uppercase heading; one large native language name; left/right arrow controls; compact input hint; a distinct confirm action. No visible language list or grid.
- Keep safe-area margins for notches and mobile landscape/portrait aspect ratios. Layout must adapt rather than crop.
- The initial frame after the ident is black. The UI enters through opacity and a subtle vertical settle; no hard pop-in.

## Interaction
- Only one language is presented at a time.
- Left/right navigation wraps across the currently selectable languages.
- A language transition uses a short, smooth outgoing/incoming text motion, tiny scale settle, and a restrained gold edge shimmer. No camera shake, strong blur, or large particles.
- Selected language uses its native name (e.g. English, Español, فارسی, العربية, 日本語).
- Confirm applies the language through LanguageService.Set(code) and only proceeds when the service accepts it.
- Touch arrow buttons and keyboard/gamepad navigation must use the same selection path. Confirm input must be debounced to prevent accidental double confirmation.
- Save the selection using the existing settings service through LanguageService; do not introduce a second persistence mechanism.
- Offer only entries for which LanguageService.CanSelect(code) returns true. Never display an unavailable translation as selectable.

## Audio and motion
- Use short, original UI ticks with a warm, delicate metallic character; one softer navigation tick and a slightly richer confirm tone.
- Keep the audio mix restrained and avoid imitating a named game's signature cue.
- Use unscaled time for UI transitions so menu behavior is predictable when time scale changes.
- Cache UI objects and avoid per-frame allocations. Prefer CanvasGroup/RectTransform interpolation and a small number of UI elements over particle-heavy effects.

## Accessibility and quality gates
- Input hints should reflect supported input mode where practical.
- Gold/ivory contrast must remain readable against black.
- Respect safe areas, touch target sizes, dynamic text widths, and native-script font support.
- Arabic and Persian require right-to-left review; changing language must not assume every font on every Android device has all glyphs.
- Test wrap-around, unavailable languages, repeated input, confirming current language, saving/reloading, all offered scripts, and scene handoff.
- Do not merge or build an APK until implementation and visual review pass. Automated tests do not substitute for a rendered-frame review or real-device QA.

## Acceptance checklist
- [ ] Black-only background; no forest or main menu visible.
- [ ] Exactly one language option visible at a time.
- [ ] Left/right navigation and touch controls work and wrap correctly.
- [ ] Native names and gold/black visual identity are consistent.
- [ ] Entry, change, and confirm animations are smooth and restrained.
- [ ] LanguageService owns validation and persistence.
- [ ] RTL scripts and safe-area layouts are reviewed.
- [ ] Tests and a rendered preview are available before approval.
