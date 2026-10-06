using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Sound
{
    /// <summary>
    /// The six volume buses, and the one multiplication every sound in the game goes through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings stores a level per bus; this turns those levels into the gain a source should
    /// actually be played at, which is the bus's level times master. Two places would be one too
    /// many: the menu's music is not the only thing that will read this — gameplay one-shots,
    /// dialogue and ambience zones are all eventually going to ask the same question — and a second
    /// implementation of "how loud is this, really" is how master volume ends up not applying to
    /// something.
    /// </para>
    /// <para>
    /// Everything here is a read of the live settings object, so a change is heard on the next sound
    /// without any notification plumbing. Sources that are already playing are told by whoever owns
    /// them (<c>MenuAudio</c> does) because a gain applied to a change is a property write, not a
    /// mixer change: there is no <c>AudioMixer</c> in this project yet, and pretending there is would
    /// be the fakest thing a settings screen could do.
    /// </para>
    /// </remarks>
    public static class AudioBuses
    {
        /// <summary>Every bus, in the order the audio settings screen lists them.</summary>
        public static readonly AudioBusId[] All =
        {
            AudioBusId.Master,
            AudioBusId.Music,
            AudioBusId.Sfx,
            AudioBusId.Ui,
            AudioBusId.Voice,
            AudioBusId.Ambience,
        };

        /// <summary>The bus's own level, before master. 0 to 1.</summary>
        public static float Level(AudioBusId bus)
        {
            var audio = AetherSettings.Ensure().Values.Audio;
            switch (bus)
            {
                case AudioBusId.Music: return audio.Music;
                case AudioBusId.Sfx: return audio.Sfx;
                case AudioBusId.Ui: return audio.Ui;
                case AudioBusId.Voice: return audio.Voice;
                case AudioBusId.Ambience: return audio.Ambience;
                default: return audio.Master;
            }
        }

        /// <summary>
        /// What a sound on this bus should be played at: its level times master, or a flat 1 for
        /// master itself.
        /// </summary>
        public static float Gain(AudioBusId bus)
        {
            var audio = AetherSettings.Ensure().Values.Audio;
            if (bus == AudioBusId.Master) return Mathf.Clamp01(audio.Master);

            return Mathf.Clamp01(Level(bus) * audio.Master);
        }

        /// <summary>Applies a bus's gain to a source. The only way a source's volume is set here.</summary>
        public static void Apply(AudioSource source, AudioBusId bus)
        {
            if (source == null) return;
            source.volume = Gain(bus);
        }

        /// <summary>Whether a bus is inaudible right now, so callers can skip work.</summary>
        public static bool IsSilent(AudioBusId bus)
        {
            return Gain(bus) <= 0.0001f;
        }

        /// <summary>
        /// A gain as a percentage, for a settings row.
        /// </summary>
        /// <remarks>
        /// Percent rather than a 0-to-1 number: a player setting a volume is thinking in "about
        /// three quarters", and the number is a readout, not a value to be typed. The percentage is
        /// rounded to a whole number so a slider does not report 67.00001 to anyone.
        /// </remarks>
        public static string Describe(AudioBusId bus)
        {
            return Mathf.RoundToInt(Gain(bus) * 100f) + "%";
        }
    }
}
