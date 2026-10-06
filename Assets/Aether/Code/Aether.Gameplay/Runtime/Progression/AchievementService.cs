using Aether.Core.Events;
using Aether.Core.Progression;
using Aether.Gameplay.Levels;
using UnityEngine;

namespace Aether.Gameplay.Progression.Achievements
{
    /// <summary>
    /// Watches a running region and records what the player has achieved in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is event-driven. A checkpoint being lit, a find being recorded, a region's
    /// exit being reached and a death are all events the session already publishes, and the service
    /// subscribes to them when a level is built and unsubscribes when it is destroyed. Nothing is
    /// polled, nothing runs per frame, and no achievement can fire without something having
    /// actually happened in the game.
    /// </para>
    /// <para>
    /// <b>The region's own contents are the yardstick.</b> "Every find in the region" is measured
    /// against the discoveries the level actually built, not against a number written down here —
    /// so adding a second secret to the Greenway changes the requirement automatically, and a
    /// player cannot be left one short of a total the game no longer uses.
    /// </para>
    /// <para>
    /// <b>Achievements are recorded in the run.</b> They are part of the save file, so continuing a
    /// run continues its achievements, and a player who deletes a slot has deleted those too. That
    /// is the honest model for a game with three slots: nothing is global about a run's record.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AchievementService : MonoBehaviour
    {
        private static AchievementService _instance;

        private GameSession _session;
        private BuiltLevel _level;
        private int _regionFindings;

        /// <summary>The service watching the region that is running, or null.</summary>
        public static AchievementService Instance
        {
            get { return _instance; }
        }

        /// <summary>Raised for each achievement the moment it is unlocked.</summary>
        /// <remarks>
        /// The gameplay layer listens for this to show the notification. It is deliberately static:
        /// the notification belongs to the interface, not to the region, and the two have no other
        /// reason to know each other.
        /// </remarks>
        public static event System.Action<AchievementDefinition> Unlocked;

        /// <summary>
        /// Starts watching a region, or re-attaches to a new one.
        /// </summary>
        /// <param name="session">The run being played.</param>
        /// <param name="level">The built region, so its contents can be counted.</param>
        public static AchievementService Attach(GameSession session, BuiltLevel level)
        {
            if (session == null) return null;

            if (_instance == null)
            {
                var host = new GameObject("Achievements");
                _instance = host.AddComponent<AchievementService>();
            }

            _instance.Watch(session, level);
            return _instance;
        }

        /// <summary>Stops watching. Called when a region is torn down.</summary>
        public static void Detach()
        {
            if (_instance != null) _instance.Stop();
        }

        private void Watch(GameSession session, BuiltLevel level)
        {
            Stop();

            _session = session;
            _level = level;
            _regionFindings = level != null && level.Discoveries != null ? level.Discoveries.Count : 0;

            session.Events.Subscribe<CheckpointActivatedEvent>(OnCheckpoint);
            session.Events.Subscribe<WorldFlagSetEvent>(OnWorldFlag);
            session.Events.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            session.Events.Subscribe<RegionEnteredEvent>(OnRegionEntered);
            session.Events.Subscribe<AbilityUnlockedEvent>(OnAbility);

            // The region is already standing when this is attached, so the entry achievement is
            // settled here: a run that was resumed at a checkpoint has, by definition, entered.
            JudgeEntry();
        }

        private void Stop()
        {
            if (_session == null) return;

            _session.Events.Unsubscribe<CheckpointActivatedEvent>(OnCheckpoint);
            _session.Events.Unsubscribe<WorldFlagSetEvent>(OnWorldFlag);
            _session.Events.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
            _session.Events.Unsubscribe<RegionEnteredEvent>(OnRegionEntered);
            _session.Events.Unsubscribe<AbilityUnlockedEvent>(OnAbility);
            _session = null;
            _level = null;
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            Stop();
            if (_instance == this) _instance = null;
        }

        private void OnCheckpoint(CheckpointActivatedEvent checkpoint)
        {
            Award(AchievementTrigger.CheckpointLit);
        }

        private void OnRegionEntered(RegionEnteredEvent region)
        {
            JudgeEntry();
        }

        private void OnAbility(AbilityUnlockedEvent ability)
        {
            // No achievement is tied to an ability yet: Rootbind is granted in a region that is not
            // in this build. The subscription stays because the trigger that will use it exists, and
            // a handler that does nothing is cheaper to read than an absent one.
        }

        private void OnPlayerDied(PlayerDiedEvent died)
        {
            // Deaths are counted by the session. Nothing is awarded for dying, and nothing is
            // revoked either: "unbroken" is judged at the exit, when the run is over.
        }

        private void OnWorldFlag(WorldFlagSetEvent flag)
        {
            if (string.IsNullOrEmpty(flag.FlagId)) return;

            if (flag.FlagId.StartsWith("secret."))
            {
                JudgeFindings();
                return;
            }

            if (flag.FlagId.StartsWith("region.completed."))
            {
                JudgeCompletion(flag.FlagId);
            }
        }

        /// <summary>Awards the entry achievement the first time a region is standing with a run.</summary>
        private void JudgeEntry()
        {
            if (_session == null) return;

            Award(AchievementTrigger.FirstRegionEntered);
        }

        /// <summary>Awards the findings achievement when every find in the region has been found.</summary>
        private void JudgeFindings()
        {
            if (_session == null || _level == null) return;

            int found = 0;
            for (int i = 0; i < _level.Discoveries.Count; i++)
            {
                DiscoveryTrigger discovery = _level.Discoveries[i];
                if (discovery == null) continue;
                if (_session.Collection.Has(discovery.FlagId) || _session.World.IsSet(discovery.FlagId)) found++;
            }

            if (_regionFindings > 0 && found >= _regionFindings) Award(AchievementTrigger.AllFindingsInRegion);
        }

        /// <summary>
        /// Awards the two achievements a finished region can carry, with the run's own record as
        /// evidence.
        /// </summary>
        private void JudgeCompletion(string completionFlag)
        {
            if (_session == null) return;

            Award(AchievementTrigger.RegionExitReached);
            JudgeFindings();

            int deaths = _session.Save.Meta != null ? _session.Save.Meta.Deaths : 0;
            if (deaths == 0) Award(AchievementTrigger.RegionExitWithoutDying);
        }

        /// <summary>
        /// Unlocks every achievement with a trigger, once, and announces it.
        /// </summary>
        /// <remarks>
        /// Unlocking is idempotent: an achievement that is already unlocked is left exactly as it
        /// was, timestamp and all. A player who reaches the exit twice does not get two
        /// notifications, and the date shown for it stays the day they first earned it.
        /// </remarks>
        private void Award(AchievementTrigger trigger)
        {
            if (_session == null) return;

            AchievementDefinition[] all = AchievementCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                AchievementDefinition definition = all[i];
                if (definition.Trigger != trigger) continue;

                AchievementState state = _session.Achievements;
                if (state == null) continue;
                if (state.IsUnlocked(definition.Id)) continue;

                AchievementRecord record = state.Ensure(definition.Id);
                if (record == null) continue;

                record.Progress = definition.Target;
                record.UnlockedUtcTicks = System.DateTime.UtcNow.Ticks;
                _session.RequestSave();

                System.Action<AchievementDefinition> handler = Unlocked;
                if (handler != null) handler(definition);
            }
        }
    }
}
