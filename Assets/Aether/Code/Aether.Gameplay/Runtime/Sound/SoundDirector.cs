using System;
using UnityEngine;

namespace Aether.Gameplay.Sound
{
    /// <summary>A request for a sound. Deliberately just an id and a position.</summary>
    public readonly struct SoundCue
    {
        /// <summary>Stable cue id, for example <c>enemy.attack</c> or <c>player.land</c>.</summary>
        public readonly string CueId;

        /// <summary>World position the sound originates from.</summary>
        public readonly Vector2 Position;

        /// <summary>Linear volume multiplier on [0, 1].</summary>
        public readonly float Volume;

        /// <summary>Pitch multiplier. 1 is unmodified.</summary>
        public readonly float Pitch;

        public SoundCue(string cueId, Vector2 position, float volume, float pitch)
        {
            CueId = cueId;
            Position = position;
            Volume = volume;
            Pitch = pitch;
        }
    }

    /// <summary>
    /// Placeholder audio seam. Gameplay requests sounds by id; nothing here plays them yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is intentionally not an audio system.</b> No mixer, no bus routing, no ducking, no
    /// music state machine, no pool sizes, no licence-cleared assets — all of that is a separate
    /// deliverable owned by whoever builds audio. What exists here is the one thing that had to be
    /// decided now: <b>how gameplay asks for a sound.</b>
    /// </para>
    /// <para>
    /// The contract is a stable cue id plus a world position, because that is everything a real
    /// backend needs to route, attenuate and mix. Gameplay never holds an <c>AudioClip</c> and never
    /// knows a source exists, so the eventual implementation can pool, stream or replace every sound
    /// without touching a single gameplay file.
    /// </para>
    /// <para>
    /// <see cref="Request"/> is safe to call with no listener attached: it does nothing and is
    /// branch-predictably cheap. That is what lets enemies and the player fire cues today, in a
    /// project with no audio at all, and have them become audible the moment a backend subscribes.
    /// </para>
    /// <para>
    /// <b>Cue ids are authored in data</b> (<c>EnemyDefinition</c>, and later in level data for
    /// ambience zones such as the Silent Zone), not hard-coded at call sites, so the mapping from id
    /// to clip lives with the content rather than in code.
    /// </para>
    /// </remarks>
    public static class SoundDirector
    {
        /// <summary>
        /// Raised for every requested cue. The future audio backend subscribes here.
        /// </summary>
        /// <remarks>
        /// A long-lived subscriber that is never removed will keep its owner alive. The intended
        /// implementation is a single scene-persistent audio service, which is exactly the case
        /// where a static hook is appropriate; do not subscribe short-lived objects to this.
        /// </remarks>
        public static event Action<SoundCue> CueRequested;

        /// <summary>Raised when a traversal/ambience zone changes, for example entering the Silent Zone.</summary>
        public static event Action<string> ZoneChanged;

        /// <summary>
        /// When true, requested cues are logged so level designers can verify that gameplay is firing
        /// the sounds they expect before any audio exists. Off by default; the audio layer will make
        /// it unnecessary.
        /// </summary>
        public static bool LogCues { get; set; }

        /// <summary>
        /// Requests a sound by cue id. No-op when nothing is listening.
        /// </summary>
        public static void Request(string cueId, Vector2 position, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(cueId)) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (LogCues) Debug.Log($"[audio] {cueId} at {position} (vol {volume:0.##}, pitch {pitch:0.##})");
#endif

            CueRequested?.Invoke(new SoundCue(cueId, position, volume, pitch));
        }

        /// <summary>
        /// Requests a sound attached to a moving object, for example a held charge.
        /// </summary>
        /// <remarks>
        /// Currently identical to <see cref="Request"/>; it exists so call sites that conceptually
        /// want a following source read correctly, and so a backend that supports attached emitters
        /// has a place to distinguish them without a breaking change later.
        /// </remarks>
        public static void RequestAttached(string cueId, Vector2 position, float volume = 1f, float pitch = 1f)
        {
            Request(cueId, position, volume, pitch);
        }

        /// <summary>
        /// Announces that the player entered a new ambience zone, for example
        /// <c>zone.whispering.silence</c>.
        /// </summary>
        /// <remarks>
        /// The Silent Zone in the Whispering Woods is authored as a real gameplay space first and an
        /// audio feature second. This is the handshake between the two: level geometry reports the
        /// zone, and a future sound designer decides what that sounds like — including making it
        /// genuinely silent, which is a mix decision this project deliberately does not make.
        /// </remarks>
        public static void RequestZone(string zoneId, bool entered)
        {
            if (string.IsNullOrEmpty(zoneId)) return;

            string message = entered ? $"enter {zoneId}" : $"exit {zoneId}";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (LogCues) Debug.Log($"[audio] zone {message}");
#endif

            ZoneChanged?.Invoke(message);
        }

        /// <summary>Removes every subscriber. Used on hard teardown, so tests do not leak listeners.</summary>
        public static void Reset()
        {
            CueRequested = null;
            ZoneChanged = null;
        }
    }
}
