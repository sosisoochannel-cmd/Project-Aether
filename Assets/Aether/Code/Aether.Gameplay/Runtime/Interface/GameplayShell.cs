using Aether.Core.Events;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Player;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// Everything the region says to the player, and everything the player can say back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is the region's session, not a scene.</b> Gameplay happens in the boot scene, and what
    /// makes it a session rather than a level is this component: it opens the region with an
    /// objective, keeps the clock, writes the run down as it goes, answers the pause key, and ends
    /// the region when the exit is reached. The alternative — a scene per region — would mean
    /// duplicating all of it for the second region, which is the rewrite the brief asks not to need.
    /// </para>
    /// <para>
    /// <b>The clock stops when the region does.</b> Play time is accumulated in unscaled seconds and
    /// handed to the session once a second; pausing stops the accumulation, which is why the number
    /// on the pause screen is time played rather than time elapsed since the app opened.
    /// </para>
    /// <para>
    /// <b>Autosave is the setting, honoured.</b> The save layer already refuses writes when the
    /// autosave setting is off; this asks on a cadence and at the moments that matter — a pause, a
    /// death, a checkpoint, a completion — so a crash costs at most the last half minute rather than
    /// the run.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameplayShell : MonoBehaviour
    {
        /// <summary>How often the run is written down while the player is playing.</summary>
        private const float AutosaveSeconds = 30f;

        /// <summary>How often the clock is handed to the session.</summary>
        private const float ClockSeconds = 1f;

        private GameSession _session;
        private BuiltLevel _level;
        private LevelDirector _director;
        private PlayerController _player;
        private TouchControlsView _touchControls;
        private ChapterDefinition _chapter;

        private GameplayHud _hud;
        private GameplayPause _pause;
        private RegionCompletePanel _complete;

        private float _clock;
        private float _autosave;
        private float _saveFailureAnnounceAfter = float.NegativeInfinity;
        private bool _completed;
        private bool _revealed;
        private bool _leaving;

        /// <summary>The HUD, for a test to read.</summary>
        public GameplayHud Hud
        {
            get { return _hud; }
        }

        /// <summary>The pause overlay, for a test to drive.</summary>
        public GameplayPause Pause
        {
            get { return _pause; }
        }

        /// <summary>The chapter this run is playing.</summary>
        public ChapterDefinition Chapter
        {
            get { return _chapter; }
        }

        /// <summary>Whether the region has been completed in this session.</summary>
        public bool Completed
        {
            get { return _completed; }
        }

        /// <summary>
        /// Opens a region: the interface, the achievement service and the clock.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="LevelBootstrap"/> once the level, the player and the director exist.
        /// Everything it builds it builds once; everything it needs it was handed.
        /// </remarks>
        public static GameplayShell Attach(GameSession session, BuiltLevel level, LevelDirector director,
                                           PlayerController player, TouchControlsView touchControls)
        {
            var host = new GameObject("Gameplay Shell");
            GameplayShell shell = host.AddComponent<GameplayShell>();
            shell.Build(session, level, director, player, touchControls);
            return shell;
        }

        private void Build(GameSession session, BuiltLevel level, LevelDirector director,
                           PlayerController player, TouchControlsView touchControls)
        {
            _session = session;
            _session.SaveFailed += OnSaveFailed;
            _level = level;
            _director = director;
            _player = player;
            _touchControls = touchControls;

            _chapter = ResolveChapter();
            _completed = IsCompleted();

            int discoveries = CountDiscoveries();
            _hud = GameplayHud.Create(session, discoveries);
            _hud.SetObjective(_chapter);
            _hud.PauseRequested += OnPauseRequested;
            _hud.transform.SetParent(transform, false);

            _pause = GameplayPause.Create(session, director, touchControls, _hud);
            _pause.transform.SetParent(transform, false);

            _complete = RegionCompletePanel.Create(session, OnKeepExploring, OnLeaveAfterCompletion);
            _complete.transform.SetParent(transform, false);

            // The run knows which region it is in, and the entry is what the first achievement and
            // the chapter's "current" state read. Asking once, here, is what makes them true.
            _session.NoteRegionEntered(_chapter.Id, _chapter.ObjectiveId);

            AchievementService.Attach(_session, _level);

            _session.Events.Subscribe<WorldFlagSetEvent>(OnWorldFlag);
            _session.Events.Subscribe<CharacterMetEvent>(OnCharacterMet);

            if (_completed) ShowCompletion();
        }

        /// <summary>
        /// Lifts the curtain once the region is worth looking at, as the call after this one.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Attach"/> so that the boot can build everything and then reveal,
        /// and so that a test can attach a shell without a curtain ever being on screen.
        /// </remarks>
        public void Reveal()
        {
            if (_revealed) return;
            _revealed = true;
            GameplayCurtain.Reveal();
        }

        private void OnApplicationPause(bool paused)
        {
            // A suspension is another automatic save point, but still obeys the player's choice:
            // SaveSlotStore ignores this request when autosave is disabled.
            if (paused && _session != null) _session.RequestSave();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Events.Unsubscribe<WorldFlagSetEvent>(OnWorldFlag);
                _session.Events.Unsubscribe<CharacterMetEvent>(OnCharacterMet);
                _session.SaveFailed -= OnSaveFailed;
            }

            AchievementService.Detach();

            // A region that is torn down while the world is frozen would leave the next scene
            // frozen with it: the time scale is global, and nothing else in the game sets it.
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void Update()
        {
            ReadPauseKey();

            // The clock and the autosave both stop with the region: a paused run is not growing, and
            // a write during a pause is a write the player asked to consider first.
            if (_pause != null && _pause.IsOpen) return;

            _clock += Time.unscaledDeltaTime;
            if (_clock >= ClockSeconds)
            {
                // Handed over in one lump: the session's meta is a float, and touching it every frame
                // would be sixty writes a second to a number nobody reads that often.
                _session.AddPlayTime(_clock);
                _clock = 0f;
            }

            _autosave += Time.unscaledDeltaTime;
            if (_autosave >= AutosaveSeconds)
            {
                _autosave = 0f;
                _session.RequestSave();
            }
        }

        /// <summary>One key, one reader, one meaning per state.</summary>
        private void ReadPauseKey()
        {
            if (_leaving) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            if (_pause == null) return;

            if (_pause.Confirm != null && _pause.Confirm.IsOpen)
            {
                // A question is being asked: back answers it the safe way, which is what the panel
                // already does for its own cancel row.
                _pause.Confirm.Cancel();
                return;
            }

            if (_pause.IsOpen) _pause.Close();
            else if (!_completed) _pause.Open();
        }

        private void OnPauseRequested()
        {
            if (_pause == null || _leaving || _completed) return;
            _pause.Open();
        }

        private void OnWorldFlag(WorldFlagSetEvent raised)
        {
            if (_completed) return;
            if (raised.FlagId != ChapterCatalog.CompletionFlagOf(_chapter.Id)) return;

            ShowCompletion();
        }

        private void OnCharacterMet(CharacterMetEvent raised)
        {
            if (_hud == null) return;

            CharacterDefinition character = CharacterCatalog.Find(raised.CharacterId);
            if (character == null) return;

            _hud.Announce("characters.met.title", MenuStrings.Get(character.TitleKey));
        }

        private void OnSaveFailed(System.Exception _)
        {
            if (_hud == null || Time.unscaledTime < _saveFailureAnnounceAfter) return;

            _saveFailureAnnounceAfter = Time.unscaledTime + 8f;
            _hud.Announce("save.failed.title", MenuStrings.Get("save.failed.body"));
        }

        /// <summary>
        /// The end of the region: the run is written, and the player is told what they did.
        /// </summary>
        /// <remarks>
        /// This is the honest version of "to be continued". Region two does not exist, so the screen
        /// says what is true — the region is finished, the next one is not in this build — and offers
        /// the two things that do exist: keep walking around, or go back to the menu with the run
        /// saved. Nothing here pretends a second region is loading.
        /// </remarks>
        private void ShowCompletion()
        {
            if (_completed) return;

            _completed = true;

            // The clock is closed out before the summary is built, so the number on the screen is
            // the number that gets written.
            _session.AddPlayTime(_clock);
            _clock = 0f;
            bool saved = SaveHost.SaveNow();

            if (_pause != null && _pause.IsOpen) _pause.Close();

            _complete.Open();
            if (!saved) _complete.ShowSaveFailure();
        }

        private void OnKeepExploring()
        {
            // Deliberately nothing but closing the panel: the region is still there, the exit is
            // still behind the player, and walking back into it does not re-trigger anything.
            _hud.SetVisible(true);
            _hud.Focus();
        }

        private void OnLeaveAfterCompletion()
        {
            if (!SaveHost.SaveNow())
            {
                _complete.ShowSaveFailure();
                return;
            }

            _leaving = true;
            GameplayCurtain.Hold("loading.title", "loading.leaving");
            GameplayCurtain.LeaveToMenu();
        }

        private ChapterDefinition ResolveChapter()
        {
            string id = _session != null && _session.Save.Meta != null ? _session.Save.Meta.ChapterId : null;
            ChapterDefinition chapter = ChapterCatalog.Find(id);
            return chapter != null && chapter.Playable ? chapter : ChapterCatalog.First;
        }

        private bool IsCompleted()
        {
            return _session != null && _session.World.IsSet(ChapterCatalog.GreenwayCompletionFlag);
        }

        private int CountDiscoveries()
        {
            if (_level == null) return 0;

            int count = 0;
            for (int i = 0; i < _level.Discoveries.Count; i++)
            {
                DiscoveryTrigger discovery = _level.Discoveries[i];
                if (discovery != null && !string.IsNullOrEmpty(discovery.FlagId)) count++;
            }

            return count;
        }
    }
}
