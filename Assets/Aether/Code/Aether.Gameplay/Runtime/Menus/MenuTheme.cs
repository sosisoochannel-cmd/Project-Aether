using UnityEngine;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Every colour, size and duration the main menu uses, in one file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu has no prefabs and no scene-authored layout: it is built by code, on the device, so
    /// that what the repository contains is what the player sees. That makes a single source of
    /// truth for the look mandatory rather than tidy — a colour used in two screens has to be the
    /// same colour, and a size that changes has to change everywhere at once.
    /// </para>
    /// <para>
    /// All sizes are in <b>reference units</b> against a 1920x1080 canvas, which is what the canvas
    /// scaler is configured for. One reference unit is one pixel on a 1080p phone and the scaler
    /// keeps a unit roughly the same physical size everywhere else, which is why the metrics here
    /// can be reasoned about in dp: <c>tools/verify/mainmenu.py</c> converts them for the device
    /// resolutions Android actually ships and refuses any that would leave a row too small to hit.
    /// </para>
    /// </remarks>
    public static class MenuTheme
    {
        /// <summary>The colour of the interface, and the one accent it is allowed.</summary>
        /// <remarks>
        /// Soot-dark greenway tones, warm ivory type, and one restrained sage accent. The accent
        /// appears only where the interface needs to direct the eye: a selected row, a section mark,
        /// or an enabled control. Everything else stays quiet, so the menu feels like part of the
        /// world rather than a separate blue-tinted template.
        /// </remarks>
        public static class Palette
        {
            /// <summary>Midnight blue-green, giving the forest a royal-fantasy frame.</summary>
            public static readonly Color Ground = new Color(0.018f, 0.027f, 0.043f, 1f);

            /// <summary>The distant layer: deep lapis and pine, just above the ground.</summary>
            public static readonly Color Horizon = new Color(0.043f, 0.071f, 0.091f, 1f);

            /// <summary>A restrained antique-gold wash, wide enough to read as warm air.</summary>
            public static readonly Color Atmosphere = new Color(0.48f, 0.30f, 0.10f, 0.13f);

            /// <summary>Primary text: warm ivory for the title and the important choices.</summary>
            public static readonly Color Ink = new Color(0.97f, 0.94f, 0.84f, 1f);

            /// <summary>Secondary text: softer ivory for subtitles, values and help lines.</summary>
            public static readonly Color InkMuted = new Color(0.78f, 0.79f, 0.76f, 1f);

            /// <summary>Tertiary text: footnotes and version details, still readable on the ground.</summary>
            public static readonly Color InkFaint = new Color(0.62f, 0.62f, 0.57f, 1f);

            /// <summary>Hairlines, separators and the tracks of sliders.</summary>
            public static readonly Color Line = new Color(0.48f, 0.35f, 0.16f, 0.72f);

            /// <summary>The one accent: muted sage for focus and enabled controls.</summary>
            public static readonly Color Accent = new Color(0.91f, 0.70f, 0.34f, 1f);

            /// <summary>The accent at a lower opacity for selected-row surfaces.</summary>
            public static readonly Color AccentWash = new Color(0.91f, 0.70f, 0.34f, 0.16f);

            /// <summary>Locked rows stay distinct, but their label and reason remain legible.</summary>
            public static readonly Color Locked = new Color(0.49f, 0.50f, 0.49f, 1f);

            /// <summary>The near-black scrim used by modal and scene transitions.</summary>
            public static readonly Color Scrim = new Color(0.010f, 0.014f, 0.025f, 0.94f);

            /// <summary>Raise every text colour for the high-contrast accessibility setting.</summary>
            public static Color WithContrast(Color colour, bool highContrast)
            {
                if (!highContrast) return colour;

                return new Color(
                    Mathf.Lerp(colour.r, 1f, 0.55f),
                    Mathf.Lerp(colour.g, 1f, 0.55f),
                    Mathf.Lerp(colour.b, 1f, 0.55f),
                    colour.a);
            }
        }

        /// <summary>
        /// How big everything is, in reference units of a 1920x1080 canvas.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The canvas is 1920 units wide and 1080 units tall at interface scale 1.</b> The
        /// scaler is set to match height, which is the decision the rest of these numbers depend
        /// on: whatever the device's aspect ratio, the canvas is always 1080 units tall and gains
        /// or loses width. A landscape menu is read across, so a taller phone should get more
        /// horizontal room rather than taller rows, and a tablet should not get a menu that is
        /// stretched vertically. It also means a row's height in units has one meaning on every
        /// device, which is what lets the gate convert these numbers into dp and check them.
        /// </para>
        /// <para>
        /// <b>Every interactive row occupies a pitch, not just a label.</b> A pitch is the full
        /// height of the rectangle a row's hit test covers — <see cref="PrimaryPitch"/>,
        /// <see cref="SecondaryPitch"/>, <see cref="SettingRowHeight"/> — and rows are placed
        /// exactly one pitch apart, so two targets can never overlap and steal each other's taps.
        /// The visual (the text, the caret, the switched-on control) may be much smaller than the
        /// pitch; the target never is. <c>tools/verify/mainmenu.py</c> converts each pitch into dp
        /// for the phones and tablets the project supports, at both ends of the interface scale
        /// range, and fails on any combination that leaves a target under 48dp — the one case where
        /// a comfortable-looking row becomes a miss under a thumb.
        /// </para>
        /// <para>
        /// Type sizes are small on purpose. A menu with 60-unit row text looks like a form; 40-unit
        /// capitals with wide tracking, plenty of space and one accent looks like a title screen.
        /// </para>
        /// </remarks>
        public static class Metrics
        {
            /// <summary>Minimum touch target the project accepts, in dp (Android's own guidance).</summary>
            public const float MinimumTouchDp = 48f;

            /// <summary>
            /// The narrowest horizontal shape the layout is built for, as width / height.
            /// </summary>
            /// <remarks>
            /// A 4:3 window is the narrowest thing the menu is expected to appear in — a tablet in
            /// landscape. The main menu splits into a left cluster and a right block, and this is
            /// the ratio where the two are closest together, so it is the case the gate checks
            /// hardest. Below it (a phone-sized window in split screen) the menu is out of scope:
            /// the app is landscape-locked, and multi-window is not a shape either screen is
            /// designed for.
            /// </remarks>
            public const float NarrowestAspect = 4f / 3f;

            // -- the brand block
            /// <summary>Height of the cropped studio lockup, in units. Its aspect is preserved.</summary>
            public const float LogoHeight = 112f;

            /// <summary>Widest the mark may be drawn before it is capped to fit its slot.</summary>
            public const float LogoMaxWidth = 420f;

            /// <summary>Distance from the brand block to the title.</summary>
            public const float LogoGap = 18f;

            public const float TitleSize = 56f;
            public const float TitleTracking = 18f;

            /// <summary>The line under the title: which region the game is standing in.</summary>
            public const float SubtitleSize = 22f;
            public const float SubtitleTracking = 12f;
            public const float TitleGapRule = 18f;
            public const float TitleRuleWidth = 300f;
            public const float TitleRuleHeight = 3f;

            /// <summary>Distance from the brand block down to the entry clusters.</summary>
            public const float TitleGapBody = 24f;

            // -- section labels
            public const float SectionLabelSize = 22f;
            public const float SectionLabelTracking = 14f;

            /// <summary>Height of a section label's own line.</summary>
            public const float SectionLabelHeight = 30f;

            /// <summary>Distance from a section label to the first row under it.</summary>
            public const float SectionLabelGap = 16f;

            /// <summary>Distance between two clusters (the primary block and the system block).</summary>
            public const float ClusterGap = 28f;

            // -- the rows
            /// <summary>Primary rows: CONTINUE, NEW GAME.</summary>
            public const float PrimarySize = 46f;
            public const float PrimaryTracking = 8f;
            public const float PrimaryPitch = 188f;

            /// <summary>Secondary and system rows: the explore grid and the two system entries.</summary>
            public const float SecondarySize = 30f;
            public const float SecondaryTracking = 6f;
            public const float SecondaryPitch = 148f;

            /// <summary>The fewest columns the two entry clusters are ever given.</summary>
            public const int SecondaryColumns = 2;

            // -- the caret that marks the highlighted row
            public const float CaretWidth = 18f;
            public const float CaretHeight = 24f;

            /// <summary>How far left of the text the caret is drawn, so text never moves.</summary>
            public const float CaretOutdent = 26f;

            /// <summary>How far a highlighted row's text slides, in units. Small on purpose.</summary>
            public const float HighlightNudge = 10f;

            // -- the two clusters, as fractions of the content box
            /// <summary>Width of the primary column (the spine on the left).</summary>
            public const float LeftColumnFraction = 0.40f;

            /// <summary>Width of the right block (the explore grid and the system pair).</summary>
            public const float RightBlockFraction = 0.54f;

            // -- the frame
            public const float ScreenMarginLeft = 180f;
            public const float ScreenMarginRight = 180f;
            public const float ScreenMarginTop = 96f;
            public const float ScreenMarginBottom = 72f;

            /// <summary>The version line, bottom right, in units from the content box corner.</summary>
            public const float VersionSize = 20f;
            public const float VersionTracking = 10f;

            // -- the other screens
            public const float ScreenTitleSize = 56f;
            public const float ScreenTitleTracking = 14f;

            /// <summary>Height of a screen's header, which is also its Back button's hit rect.</summary>
            public const float ScreenHeaderPitch = 176f;

            /// <summary>Width of the Back button's hit rect.</summary>
            public const float BackHitWidth = 320f;

            public const float ScreenHeaderGap = 28f;

            /// <summary>Pitch of one setting row. Also its hit height: the two are the same.</summary>
            public const float SettingRowHeight = 176f;

            /// <summary>The label column of a setting row, as a fraction of the content width.</summary>
            public const float SettingLabelFraction = 0.46f;

            public const float SettingLabelSize = 28f;
            public const float SettingHelpSize = 22f;

            /// <summary>A category header: the tracked label and the rule that opens the group.</summary>
            public const float CategoryLabelHeight = 104f;

            public const float CategoryLabelSize = 22f;
            public const float CategoryLabelTracking = 14f;

            /// <summary>One paragraph line: a category's note, or a credits block.</summary>
            public const float ParagraphSize = 26f;
            public const float ParagraphLineHeight = 42f;

            /// <summary>Height of a block of prose, per line, including its leading.</summary>
            public const float ParagraphBlockPadding = 16f;

            /// <summary>Pitch of a read-only information row (a version, a platform, a path).</summary>
            public const float InfoRowHeight = 112f;

            /// <summary>The scrim behind a modal, and the panel that sits on it.</summary>
            public const float DialogWidth = 900f;
            public const float DialogMinHeight = 420f;
            public const float DialogPadding = 56f;

            // -- the widgets
            /// <summary>Track of a switch, drawn inside a row that is much taller than it is.</summary>
            public const float SwitchWidth = 108f;
            public const float SwitchHeight = 56f;
            public const float SwitchKnobInset = 6f;

            public const float SliderWidth = 460f;
            public const float SliderTrackHeight = 6f;
            public const float SliderKnobSize = 34f;

            /// <summary>Space between the low and high ends of a selector's arrows.</summary>
            public const float SelectorArrowGap = 300f;
            public const float SelectorArrowSize = 18f;

            // -- fades
            public const float ScreenFadeSeconds = 0.22f;
            public const float TransitionFadeSeconds = 0.26f;
        }

        /// <summary>
        /// How long everything takes. Short by design: this is a menu, not a cutscene.
        /// </summary>
        /// <remarks>
        /// The whole entrance is over in well under a second, and a press is acknowledged in a
        /// frame. Anything longer than about a quarter of a second stops reading as polish and
        /// starts reading as the game not responding — which is why the "reduced motion" setting can
        /// shorten these further without the menu becoming unrecognisable.
        /// </remarks>
        public static class Motion
        {
            /// <summary>Fade of one row, or of one group, as it arrives.</summary>
            public const float EntranceSeconds = 0.26f;

            /// <summary>Gap between the title, the primary block and the secondary block arriving.</summary>
            public const float EntranceStagger = 0.07f;

            /// <summary>How far a row rises as it fades in, in reference units.</summary>
            public const float EntranceRise = 16f;

            /// <summary>Press acknowledgment: how long the row takes to reach its pressed scale.</summary>
            public const float PressSeconds = 0.06f;

            /// <summary>Scale a row is drawn at while held.</summary>
            public const float PressedScale = 0.975f;

            /// <summary>How long the highlight caret and rule take to grow in.</summary>
            public const float HighlightSeconds = 0.14f;

            /// <summary>Idle drift of the backdrop, in reference units.</summary>
            public const float ParallaxAmplitude = 12f;

            /// <summary>Seconds for one full cycle of the backdrop's drift.</summary>
            public const float ParallaxPeriod = 26f;

            /// <summary>Seconds of music fade at the start and the end of the menu.</summary>
            public const float MusicFadeSeconds = 1.6f;

            /// <summary>How far the screen dims while a transition is running.</summary>
            public const float TransitionDim = 0.55f;

            /// <summary>Durations after the reduced-motion setting has had its say.</summary>
            public static float Entrance(bool reducedMotion) => reducedMotion ? 0.12f : EntranceSeconds;

            /// <summary>Screen fade, shortened when the player asked for less movement.</summary>
            public static float ScreenFade(bool reducedMotion)
                => reducedMotion ? 0.12f : Metrics.ScreenFadeSeconds;

            /// <summary>Transition fade, shortened when the player asked for less movement.</summary>
            public static float TransitionFade(bool reducedMotion)
                => reducedMotion ? 0.16f : Metrics.TransitionFadeSeconds;
        }
    }
}
