# AETHER Language Selection — Gold / Black Cinematic Direction

## Core art direction
The first screen after the Varellon studio ident is a dedicated language selector, not the main menu. It should feel like a premium action-adventure game's polished, tactile UI: rich, layered, responsive and memorable—not a flat black screen with plain yellow text.

The palette is exclusively black and gold, but gold must behave like a full interaction system rather than one static color. Do not copy Prince of Persia assets, layouts, symbols, or signature effects; take only the broad lesson that selection states should feel vivid, rewarding, and alive.

## Palette and material
- Foundation: near-black (#050505) with very subtle black-on-black depth; no forest backdrop.
- Primary metal: champagne gold (#D8B56D), bright gold highlight (#F4DFA7), antique gold (#8B6B32).
- Interactive glow: layered amber-to-pale-gold light, never flat saturated yellow. A focused/pressed state may briefly brighten toward ivory-gold, with a thin luminous edge and a small restrained bloom.
- Secondary text: warm ivory. Unselected controls: dim bronze/gold at low intensity.
- No blue or purple in this screen's identity. Achieve the same sense of expressive selection through multiple gold values, contrast, glow, and motion.

## Composition
- One language at a time, centered and large, shown in its native writing system.
- A refined architectural frame with subtle corner detailing and a central diamond/crest-like accent; keep ornament sparse and original.
- Left/right arrow controls are visibly interactive, not generic flat boxes.
- A clear but elegant confirm action.
- Strong visual hierarchy, generous negative space, and adaptive safe-area layouts for mobile portrait and landscape. No language grid or visible list.

## Interaction and animation
- Left/right navigation wraps over only languages currently offered by LanguageService.
- Each language change gets a polished transition: current name moves/fades a few pixels away, next name settles into place; a short gold sweep or edge glint follows. Use restrained easing and a tiny scale settle. Do not use loud camera shake or particle spam.
- Arrow and confirm controls have distinct normal, highlighted, pressed, and disabled states. Highlighting should visibly illuminate the control in gold; pressing gives a brief brighter gold flash/inner edge, then settles smoothly.
- The currently selected language receives a fine gold underline or focused ornamental accent that briefly blooms on change, rather than a permanently huge glow.
- Confirm action gets a richer but short gold illumination and a clean transition into the existing main menu.
- Input hints should adapt to supported input where practical. Touch, keyboard, and gamepad must follow the same selection path. Debounce confirmation.

## Localization and reliability
- Display native names (e.g. English, Español, فارسی, العربية, 日本語).
- LanguageService is the sole authority for selectable languages and persistence. Never expose unavailable translations as selectable.
- Confirm must apply the chosen code and only proceed when accepted by the service.
- Arabic and Persian require right-to-left and glyph/font review; do not assume every Android font supports every script.
- Cache UI objects, use unscaled time for UI motion, avoid per-frame allocations, and preserve accessibility contrast.

## Quality gates
- Black foundation and coherent black/gold palette.
- Responsive gold highlight states for all controls; no generic white flash.
- Exactly one selectable language shown at once.
- Smooth intro, change, highlight, press, and confirm motion.
- Touch targets, safe areas, dynamic language widths, RTL scripts, wrap-around, persistence, repeated input, and menu handoff tested.
- Rendered-frame review and real-device QA required before merge/APK. Source review alone is not proof of runtime quality.
