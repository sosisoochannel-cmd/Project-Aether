using System.Collections;
using Aether.Core.Settings;
using Aether.Gameplay.Sound;
using UnityEngine;
using UnityEngine.Audio;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The menu's audio: one music source, one ambience source, one shots for the interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no music in this repository, and this does not invent any.</b> The brief for the
    /// menu is explicit that nothing is downloaded or composed at random, so what exists here is the
    /// integration point: <see cref="MenuMusicPath"/> is where an approved track goes, and until a
    /// file is dropped there the menu is silent on that channel and says so once. Everything around
    /// it is real — the sources, the six-bus gain, the fade in and out, the seamless loop setting,
    /// the mixer group seam for when an <c>AudioMixer</c> asset arrives.
    /// </para>
    /// <para>
    /// <b>Cues go through the existing seam.</b> Interface sounds are requested with
    /// <see cref="SoundDirector"/>, which is the project's one agreed way for a system to ask for a
    /// sound, and additionally played here if a clip has been dropped into
    /// <c>Resources/Audio/Cues</c>. That means a sound designer can add a click without touching a
    /// line of this file, and the menu is not a special case of the audio architecture.
    /// </para>
    /// <para>
    /// <b>One bus per source, never two.</b> A source's volume is always set by
    /// <see cref="AudioBuses"/>, so master volume reaches everything the menu plays, and the
    /// six-bus structure the settings screen promises is the structure the audio actually uses.
    /// </para>
    /// </remarks>
    public sealed class MenuAudio : MonoBehaviour
    {
        /// <summary>Where an approved menu track is looked for (no extension, as Resources wants).</summary>
        public const string MenuMusicPath = "Audio/Menu/MenuTheme";

        /// <summary>Where interface cues are looked for.</summary>
        public const string UiCueFolder = "Audio/Cues/";

        private const string MusicCue = "ui.music";

        private static MenuAudio _instance;
        private static bool _musicMissingReported;

        private AudioSource _music;
        private AudioSource _ambience;
        private AudioSource _oneShots;
        private AudioMixerGroup _musicGroup;
        private AudioMixerGroup _uiGroup;
        private Coroutine _fade;

        /// <summary>The live menu audio, or null before the menu has been built.</summary>
        public static MenuAudio Instance
        {
            get { return _instance; }
        }

        /// <summary>True when a track is loaded and playing.</summary>
        public bool MusicPlaying
        {
            get { return _music != null && _music.isPlaying; }
        }

        /// <summary>Builds the three sources. Called by the menu host, once.</summary>
        public static MenuAudio Create(Transform parent)
        {
            var host = new GameObject("Menu Audio");
            host.transform.SetParent(parent, false);

            var audio = host.AddComponent<MenuAudio>();
            audio._music = Source(host.transform, "Music", true);
            audio._ambience = Source(host.transform, "Ambience", true);
            audio._oneShots = Source(host.transform, "Interface", false);

            _instance = audio;
            audio.ApplySettings();
            return audio;
        }

        /// <summary>
        /// Hands the sources to an AudioMixer's groups.
        /// </summary>
        /// <remarks>
        /// The seam for the day this project has a mixer: assign the groups once and every later
        /// volume change can be a mixer send as well as a source volume, without any of the call
        /// sites changing. Until then the six-bus gain above is the whole story, which is honest but
        /// is not ducking.
        /// </remarks>
        public void AssignMixerGroups(AudioMixerGroup music, AudioMixerGroup ui)
        {
            _musicGroup = music;
            _uiGroup = ui;

            if (_music != null) _music.outputAudioMixerGroup = music;
            if (_ambience != null) _ambience.outputAudioMixerGroup = music;
            if (_oneShots != null) _oneShots.outputAudioMixerGroup = ui;
        }

        /// <summary>
        /// Starts the menu's music, if an approved track exists.
        /// </summary>
        /// <param name="fadeSeconds">Fade-in length. Zero starts it at full gain.</param>
        /// <returns>True when something started playing.</returns>
        public bool PlayMenuMusic(float fadeSeconds = MenuTheme.Motion.MusicFadeSeconds)
        {
            if (_music == null) return false;

            if (_music.clip == null)
            {
                AudioClip clip = Resources.Load<AudioClip>(MenuMusicPath);
                if (clip == null)
                {
                    if (!_musicMissingReported)
                    {
                        _musicMissingReported = true;
                        Debug.Log(
                            $"[menu] No menu track at Resources/{MenuMusicPath}. The menu runs " +
                            "silently on the music bus; dropping an approved clip at that path is " +
                            "the whole integration.", this);
                    }

                    return false;
                }

                _music.clip = clip;
                _music.loop = true;
            }

            _music.time = 0f;
            _music.volume = 0f;
            _music.Play();

            Request(MusicCue, 0.5f);
            return true;
        }

        /// <summary>Fades the music and ambience out, and stops them.</summary>
        public void FadeOut(float seconds = MenuTheme.Motion.MusicFadeSeconds)
        {
            if (_music == null) return;
            if (_fade != null) StopCoroutine(_fade);

            if (!_music.isPlaying)
            {
                _music.volume = 0f;
                return;
            }

            _fade = StartCoroutine(FadeRoutine(seconds));
        }

        /// <summary>Reapplies the bus gains. Called when a volume setting changes.</summary>
        public void ApplySettings()
        {
            if (_music == null) return;

            AudioBuses.Apply(_music, AudioBusId.Music);
            AudioBuses.Apply(_ambience, AudioBusId.Ambience);
            AudioBuses.Apply(_oneShots, AudioBusId.Ui);
        }

        /// <summary>
        /// Requests one interface cue: through <see cref="SoundDirector"/>, and out loud if a clip
        /// for it exists.
        /// </summary>
        /// <param name="cueId">Stable cue id, for example <c>ui.confirm</c>.</param>
        /// <param name="volume">Extra multiplier on the bus gain, 0 to 1.</param>
        public static void Request(string cueId, float volume = 1f)
        {
            if (string.IsNullOrEmpty(cueId)) return;

            float gain = AudioBuses.Gain(AudioBusId.Ui);
            SoundDirector.Request(cueId, Vector2.zero, Mathf.Clamp01(gain * volume));

            if (gain <= 0.0001f) return;

            MenuAudio audio = _instance;
            if (audio == null || audio._oneShots == null) return;

            AudioClip clip = Resources.Load<AudioClip>(UiCueFolder + cueId);
            if (clip == null) return;

            audio._oneShots.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>
        /// A press sound, at the three moments the interface has: choosing a row, changing a value,
        /// and going back.
        /// </summary>
        /// <remarks>
        /// Three cues, not ten: a menu that clicks at everything is the cheapest way to make a
        /// polished screen feel cheap. Nothing is played on navigation between rows, because a
        /// highlighted row is not an event the player asked to hear.
        /// </remarks>
        public static void Confirm()
        {
            Request("ui.confirm");
        }

        /// <summary>A value changed: a switch, a slider step, a choice.</summary>
        public static void Adjust()
        {
            Request("ui.adjust", 0.7f);
        }

        /// <summary>The back action, and a cancelled dialog.</summary>
        public static void Back()
        {
            Request("ui.back", 0.8f);
        }

        private IEnumerator FadeRoutine(float seconds)
        {
            float from = _music.volume;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds));
                _music.volume = Mathf.Lerp(from, 0f, t);
                yield return null;
            }

            _music.Stop();
            _music.volume = 0f;
            if (_ambience != null) _ambience.Stop();
            _fade = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static AudioSource Source(Transform parent, string name, bool loop)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);

            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;      // the menu is not in a space; a panned menu is a mistake
            source.volume = 0f;
            return source;
        }
    }
}
