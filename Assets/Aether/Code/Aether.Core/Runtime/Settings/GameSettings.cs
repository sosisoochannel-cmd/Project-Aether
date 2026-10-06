using System;
using UnityEngine;

namespace Aether.Core.Settings
{
    /// <summary>How much of the visual layer the player wants, and the tier the game runs at.</summary>
    /// <remarks>
    /// Three tiers rather than a slider of quality knobs, because a phone either has the headroom or
    /// it does not, and a player should not have to become a graphics programmer to make the game
    /// run. <see cref="Medium"/> is the default: it is the tier the project was tuned against.
    /// </remarks>
    public enum GraphicsTier
    {
        /// <summary>Fewest effects: no atmosphere motion, no parallax, shortest fades.</summary>
        Low = 0,

        /// <summary>The tuned default.</summary>
        Medium = 1,

        /// <summary>Everything on, including the atmosphere layers and the longest fades.</summary>
        High = 2,
    }

    /// <summary>What the interface is allowed to do with the screen's cutouts.</summary>
    /// <remarks>
    /// Respecting the safe area is the default because a notch over a menu item is a broken menu,
    /// but a player who wants the artwork to run edge to edge can say so. Nothing else in the game
    /// reads this: gameplay draws its own overlay and has its own rule.
    /// </remarks>
    public enum SafeAreaMode
    {
        /// <summary>Ignore cutouts: the interface fills the whole screen.</summary>
        FullScreen = 0,

        /// <summary>Keep every element inside <c>Screen.safeArea</c>. The default.</summary>
        Respect = 1,
    }

    /// <summary>The six volume buses the game mixes through.</summary>
    /// <remarks>
    /// One value per bus, and every sound in the game reaches the speakers through exactly one of
    /// them. The split exists because the useful question a player asks is never "how loud is
    /// everything" but "make the music quieter, I am trying to hear the dialogue".
    /// </remarks>
    public enum AudioBusId
    {
        /// <summary>Applies to everything. Multiplied into every other bus.</summary>
        Master = 0,

        Music = 1,
        Sfx = 2,

        /// <summary>Interface confirmations, taps and transitions.</summary>
        Ui = 3,

        /// <summary>Spoken dialogue.</summary>
        Voice = 4,

        /// <summary>Environmental beds, including the main menu's.</summary>
        Ambience = 5,
    }

    /// <summary>
    /// The player's settings: everything they can change, in one serialisable object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is deliberately dumb. It holds values with sensible defaults, it can copy itself,
    /// and it can clamp itself into range. It does not know about files, about Unity, about a
    /// settings screen, or about who reads it — which is what lets the same object be written to
    /// disk by <c>FileSettingsStore</c>, checked by an edit-mode test, and read by gameplay
    /// systems without any of them depending on the others.
    /// </para>
    /// <para>
    /// Fields are public and serialisable on purpose: this is a data object, and the property
    /// ceremony that would hide them buys nothing except a longer file.
    /// </para>
    /// <para>
    /// <b>Adding a field is a save-format change.</b> Bump <see cref="Version"/> and handle the
    /// older shape in <see cref="SettingsService"/>'s migration step, or values will silently
    /// revert to defaults on the next launch. The gate checks that the version and the field count
    /// are stated together.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class GameSettings
    {
        /// <summary>The shape of this object as it was written to disk.</summary>
        public const int CurrentVersion = 1;

        /// <summary>What the file was written with. Compared on load, never trusted afterwards.</summary>
        public int Version = CurrentVersion;

        public AudioSettings Audio = new AudioSettings();
        public GameplaySettings Gameplay = new GameplaySettings();
        public ControlSettings Controls = new ControlSettings();
        public CameraSettings Camera = new CameraSettings();
        public GraphicsSettings Graphics = new GraphicsSettings();
        public DisplaySettings Display = new DisplaySettings();
        public AccessibilitySettings Accessibility = new AccessibilitySettings();

        /// <summary>Language code the interface is shown in, for example <c>en</c>.</summary>
        public string Language = "en";

        /// <summary>Every field back to the value a fresh install has.</summary>
        public void Reset()
        {
            Audio.Reset();
            Gameplay.Reset();
            Controls.Reset();
            Camera.Reset();
            Graphics.Reset();
            Display.Reset();
            Accessibility.Reset();
            Language = "en";
            Version = CurrentVersion;
        }

        /// <summary>Copies <paramref name="other"/> into this instance, field by field.</summary>
        public void CopyFrom(GameSettings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            Audio.CopyFrom(other.Audio);
            Gameplay.CopyFrom(other.Gameplay);
            Controls.CopyFrom(other.Controls);
            Camera.CopyFrom(other.Camera);
            Graphics.CopyFrom(other.Graphics);
            Display.CopyFrom(other.Display);
            Accessibility.CopyFrom(other.Accessibility);
            Language = other.Language;
            Version = other.Version;
        }

        /// <summary>
        /// Pulls every value back into the range it is allowed to have.
        /// </summary>
        /// <remarks>
        /// Called after every load and every write. A hand-edited file, an older version's value or
        /// a slider that was dragged past its end must not be able to put the game in a state the
        /// interface cannot show and the player cannot leave — a volume of 4, a UI scale of zero.
        /// </remarks>
        public void Clamp()
        {
            Audio.Clamp();
            Gameplay.Clamp();
            Controls.Clamp();
            Camera.Clamp();
            Graphics.Clamp();
            Display.Clamp();
            Accessibility.Clamp();
            if (string.IsNullOrEmpty(Language)) Language = "en";
        }
    }

    /// <summary>Volume per bus, on [0, 1]. Multiplied together, never additive.</summary>
    [Serializable]
    public sealed class AudioSettings
    {
        public float Master = 0.8f;
        public float Music = 0.7f;
        public float Sfx = 0.9f;
        public float Ui = 0.8f;
        public float Voice = 0.9f;
        public float Ambience = 0.6f;

        public void Reset()
        {
            Master = 0.8f;
            Music = 0.7f;
            Sfx = 0.9f;
            Ui = 0.8f;
            Voice = 0.9f;
            Ambience = 0.6f;
        }

        public void CopyFrom(AudioSettings other)
        {
            Master = other.Master;
            Music = other.Music;
            Sfx = other.Sfx;
            Ui = other.Ui;
            Voice = other.Voice;
            Ambience = other.Ambience;
        }

        public void Clamp()
        {
            Master = Mathf.Clamp01(Master);
            Music = Mathf.Clamp01(Music);
            Sfx = Mathf.Clamp01(Sfx);
            Ui = Mathf.Clamp01(Ui);
            Voice = Mathf.Clamp01(Voice);
            Ambience = Mathf.Clamp01(Ambience);
        }
    }

    /// <summary>How the game itself behaves, as opposed to how it looks or sounds.</summary>
    /// <remarks>
    /// One real option today. The category exists — and is honest about its own emptiness — because
    /// difficulty and assistance are decisions the region has not asked yet: it ships with one
    /// tuned difficulty and no tutorial system, so there is nothing truthful to put here.
    /// </remarks>
    [Serializable]
    public sealed class GameplaySettings
    {
        /// <summary>
        /// Whether progress records itself as the player plays.
        /// </summary>
        /// <remarks>
        /// Off means a session is not written until the player asks for it from Data/Save, which is
        /// what a player replaying a region without committing to it wants. The gate in
        /// <c>GameSession</c> does not change: this is the store declining the write, not gameplay
        /// stopping its calls.
        /// </remarks>
        public bool Autosave = true;

        public void Reset()
        {
            Autosave = true;
        }

        public void CopyFrom(GameplaySettings other)
        {
            Autosave = other.Autosave;
        }

        public void Clamp()
        {
            // A boolean cannot hold an illegal value. Present so every section answers alike.
        }
    }

    /// <summary>The touch controls, as the player wants them rather than as they were tuned.</summary>
    [Serializable]
    public sealed class ControlSettings
    {
        /// <summary>
        /// Fraction of the stick's travel that is ignored, so a resting thumb does not walk.
        /// </summary>
        public float StickDeadZone = 0.14f;

        /// <summary>Multiplier on every on-screen control's radius. 1 is the tuned layout.</summary>
        public float ButtonSize = 1f;

        /// <summary>Multiplier on every on-screen control's opacity.</summary>
        public float ButtonOpacity = 1f;

        /// <summary>Whether the device is allowed to buzz on hits and confirmations.</summary>
        public bool Haptics = true;

        /// <summary>
        /// Mirror the on-screen controls: movement under the right thumb, actions under the left.
        /// </summary>
        /// <remarks>
        /// A real layout change rather than a cosmetic one, and the reason the layout is expressed
        /// as fractions of the safe area in the first place: mirroring is <c>x -> 1 - x</c> applied
        /// to every anchor, so the whole overlay moves without a second set of numbers to maintain.
        /// </remarks>
        public bool LeftHanded;

        public void Reset()
        {
            StickDeadZone = 0.14f;
            ButtonSize = 1f;
            ButtonOpacity = 1f;
            Haptics = true;
            LeftHanded = false;
        }

        public void CopyFrom(ControlSettings other)
        {
            StickDeadZone = other.StickDeadZone;
            ButtonSize = other.ButtonSize;
            ButtonOpacity = other.ButtonOpacity;
            Haptics = other.Haptics;
            LeftHanded = other.LeftHanded;
        }

        public void Clamp()
        {
            StickDeadZone = Mathf.Clamp(StickDeadZone, 0f, 0.45f);
            // The ceiling is a geometry limit, not a taste limit: above it the attack button
            // reaches outside the safe area on a 4:3 tablet with a notch, which
            // tools/verify/touchlayout.py proves at both ends of this range.
            ButtonSize = Mathf.Clamp(ButtonSize, 0.85f, 1.15f);
            ButtonOpacity = Mathf.Clamp(ButtonOpacity, 0.3f, 1f);
        }
    }

    /// <summary>How the follow camera behaves. Read by <c>CameraFollow2D</c>.</summary>
    /// <remarks>
    /// There is no "camera sensitivity" here because there is no second stick to be sensitive: the
    /// camera follows the player. What a player actually feels in a platformer is how much the
    /// camera lags and how far it looks ahead, so those are the two values exposed, named after
    /// what they do.
    /// </remarks>
    [Serializable]
    public sealed class CameraSettings
    {
        /// <summary>Seconds the camera takes to catch up. Larger is softer, smaller is tighter.</summary>
        public float FollowSmoothing = 0.16f;

        /// <summary>How far ahead of the player the camera leads, in world units.</summary>
        public float LookAhead = 1.7f;

        public void Reset()
        {
            FollowSmoothing = 0.16f;
            LookAhead = 1.7f;
        }

        public void CopyFrom(CameraSettings other)
        {
            FollowSmoothing = other.FollowSmoothing;
            LookAhead = other.LookAhead;
        }

        public void Clamp()
        {
            FollowSmoothing = Mathf.Clamp(FollowSmoothing, 0.04f, 0.6f);
            LookAhead = Mathf.Clamp(LookAhead, 0f, 3.5f);
        }
    }

    /// <summary>The tier, the frame cap and whether the menu's atmosphere moves.</summary>
    [Serializable]
    public sealed class GraphicsSettings
    {
        public GraphicsTier Tier = GraphicsTier.Medium;

        /// <summary>Target frame rate. 0 means "do not cap". 30 and 60 are the useful values.</summary>
        public int FrameRateLimit = 60;

        /// <summary>
        /// Whether the main menu's background is allowed to move.
        /// </summary>
        /// <remarks>
        /// A separate switch from the tier, and on by default, because the two questions are
        /// different: a player on a mid-range phone may want 60fps <i>and</i> the atmosphere. The
        /// tier decides what is drawn; this decides whether it drifts. It is also what the
        /// accessibility setting "reduced motion" turns off.
        /// </remarks>
        public bool AtmosphereMotion = true;

        public void Reset()
        {
            Tier = GraphicsTier.Medium;
            FrameRateLimit = 60;
            AtmosphereMotion = true;
        }

        public void CopyFrom(GraphicsSettings other)
        {
            Tier = other.Tier;
            FrameRateLimit = other.FrameRateLimit;
            AtmosphereMotion = other.AtmosphereMotion;
        }

        public void Clamp()
        {
            if (Tier < GraphicsTier.Low || Tier > GraphicsTier.High) Tier = GraphicsTier.Medium;
            FrameRateLimit = Mathf.Clamp(FrameRateLimit, 0, 120);
        }
    }

    /// <summary>UI scale, cutout behaviour and whether the screen is allowed to sleep.</summary>
    [Serializable]
    public sealed class DisplaySettings
    {
        /// <summary>
        /// Multiplier on the interface's reference resolution.
        /// </summary>
        /// <remarks>
        /// Above 1 the interface gets bigger, which is what a player on a small phone or with
        /// imperfect eyesight is actually asking for. It is a scale on the whole canvas rather than
        /// a change to any one screen, so it cannot break a layout: everything is laid out in
        /// reference units and then multiplied.
        /// </remarks>
        public float UiScale = 1f;

        public SafeAreaMode SafeArea = SafeAreaMode.Respect;

        /// <summary>Whether the screen may sleep while the game is open.</summary>
        public bool KeepScreenAwake = true;

        public void Reset()
        {
            UiScale = 1f;
            SafeArea = SafeAreaMode.Respect;
            KeepScreenAwake = true;
        }

        public void CopyFrom(DisplaySettings other)
        {
            UiScale = other.UiScale;
            SafeArea = other.SafeArea;
            KeepScreenAwake = other.KeepScreenAwake;
        }

        public void Clamp()
        {
            UiScale = Mathf.Clamp(UiScale, 0.8f, 1.4f);
            if (SafeArea != SafeAreaMode.FullScreen && SafeArea != SafeAreaMode.Respect)
                SafeArea = SafeAreaMode.Respect;
        }
    }

    /// <summary>What the player needs the interface to do differently.</summary>
    /// <remarks>
    /// "Reduced motion" is the one that matters most and the one most often faked: here it
    /// genuinely stops every moving element in the menu and shortens every fade, and the render
    /// test proves the atmosphere really does stand still when it is on.
    /// </remarks>
    [Serializable]
    public sealed class AccessibilitySettings
    {
        /// <summary>Stop the menu's atmosphere drifting and shorten transitions.</summary>
        public bool ReducedMotion;

        /// <summary>Raise the interface's text contrast for readability.</summary>
        public bool HighContrast;

        public void Reset()
        {
            ReducedMotion = false;
            HighContrast = false;
        }

        public void CopyFrom(AccessibilitySettings other)
        {
            ReducedMotion = other.ReducedMotion;
            HighContrast = other.HighContrast;
        }

        public void Clamp()
        {
            // Nothing to clamp: two booleans cannot hold an illegal value. The method exists so
            // every section answers the same question and the caller never has to special-case one.
        }
    }
}
