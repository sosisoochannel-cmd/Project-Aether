# AETHER — Startup Language Selector Removed

The dedicated gold/black language-selection screen is no longer part of the startup flow and has been removed from the runtime screen registry.

## Current startup flow

1. The Varellon studio ident plays.
2. The app opens directly to the main menu (or directly to Settings when returning from a pause-menu hand-off).

## Changing language

Language selection remains available under **Settings → Language**. It continues to use `LanguageService`, the supported language catalogue, and the existing settings persistence pipeline. Removing the separate startup page does not remove localization or reset the player's saved language.

## Verification

The PlayMode startup test now asserts that a normal menu launch opens `MenuScreenId.MainMenu` immediately. No Android APK was built as part of this change.
