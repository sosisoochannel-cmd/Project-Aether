using System.Collections.Generic;
using Aether.Gameplay.Cameras;
using Aether.Gameplay.Enemies;
using Aether.Gameplay.Player;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Runs a built level: where the player comes back to, what happens when they die, and what a
    /// retry resets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Death is not an event the player has to recover from by hand. Dying returns them to the last
    /// checkpoint with full health, and puts every enemy in the region back where the level placed
    /// them, so a retry is the encounter as authored rather than a battlefield of half-killed
    /// enemies. That is what makes the second attempt a lesson instead of a formality.
    /// </para>
    /// <para>
    /// The respawn position always comes from a checkpoint in level data, or from the level's own
    /// start. There is no code path that returns the player to "wherever they were", which is how
    /// respawn-into-geometry bugs happen.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LevelDirector : MonoBehaviour
    {
        /// <summary>How long the death beat lasts before control returns.</summary>
        private const float RespawnDelay = 0.9f;
        private const float FallDeathMargin = 4f;

        private readonly List<EnemyController> _enemies = new List<EnemyController>();
        private readonly List<Vector2> _enemyFeet = new List<Vector2>();

        private GameSession _session;
        private BuiltLevel _built;
        private PlayerController _player;
        private CameraFollow2D _camera;
        private float _respawnAt = float.NegativeInfinity;

        /// <summary>The level currently running.</summary>
        public BuiltLevel Level => _built;

        /// <summary>The player.</summary>
        public PlayerController Player => _player;

        /// <summary>True while the player is dead and waiting to be returned.</summary>
        public bool IsRespawning => _respawnAt > float.NegativeInfinity;

        /// <summary>Where the player is returned to, as the bottom of their feet.</summary>
        public Vector2 RespawnFeet { get; private set; }

        /// <summary>Wires the director to a built level and the player now running in it.</summary>
        /// <param name="camera">
        /// The follow camera, so a respawn can move the view immediately. Optional: a test or a
        /// headless run has no camera and everything else still works.
        /// </param>
        public void Initialize(GameSession session, BuiltLevel built, PlayerController player,
                               CameraFollow2D camera = null)
        {
            // Initialize can be called again by a test harness or a future region hand-off. Remove
            // the old callback before replacing the player so a former player cannot drive this
            // director's respawn timer after it is no longer the active character.
            if (_player != null && _player.Health != null)
                _player.Health.Died -= OnPlayerDied;

            _session = session;
            _built = built;
            _player = player;
            _camera = camera;

            _enemies.Clear();
            _enemyFeet.Clear();
            for (int i = 0; i < built.Enemies.Count; i++)
            {
                EnemyController enemy = built.Enemies[i].Controller;
                _enemies.Add(enemy);
                _enemyFeet.Add(built.Enemies[i].Feet);

                // The factory cannot know which player this level will spawn. Wire the target here,
                // once both sides exist; without this, every enemy's perception sees a null player
                // and the entire region silently becomes a combat-free walk.
                if (enemy != null && _player != null)
                    enemy.Initialize(enemy.Definition, _player.transform);
            }

            RespawnFeet = ResolveRespawnFeet();

            if (_player != null && _player.Health != null)
            {
                _player.Health.Died -= OnPlayerDied;
                _player.Health.Died += OnPlayerDied;
            }
        }

        private void OnDestroy()
        {
            if (_player != null && _player.Health != null) _player.Health.Died -= OnPlayerDied;
        }

        private void Update()
        {
            // The authored terrain contains open pits all the way through the map. Without a
            // lower-world guard, a player who falls through one can keep falling forever because
            // there is no collider below the level to produce a normal death event.
            if (Time.timeScale > 0f && _player != null && _built != null && _built.Data != null
                && _player.Health != null && _player.Health.IsAlive
                && _player.Motor.Position.y < _built.Data.WorldBounds.yMin - FallDeathMargin)
            {
                _player.Health.ForceDeath();
            }

            if (_respawnAt <= float.NegativeInfinity || Time.time < _respawnAt) return;

            _respawnAt = float.NegativeInfinity;
            PerformRespawn();
        }

        private void OnPlayerDied()
        {
            if (_player == null) return;

            // The run counts its own deaths: the achievement for never falling, the pause menu's
            // summary and the save file's meta line all read this number, and a death that is not
            // counted here is a death the game forgets.
            if (_session != null) _session.RecordDeath();

            // Control is removed immediately: an input that arrives during the death beat must not
            // queue a jump that fires the instant the player is back on their feet.
            _player.InputEnabled = false;
            _respawnAt = Time.time + RespawnDelay;
        }

        /// <summary>
        /// Begins the region again from its own start, with every encounter restored.
        /// </summary>
        /// <remarks>
        /// The pause menu's restart. It reuses the respawn path deliberately — the same enemy reset,
        /// the same checkpoint rearm, the same camera snap — and differs in one thing: where the
        /// player lands. A respawn returns them to the checkpoint they lit; a restart returns them to
        /// where the region begins, which is the only reading of "restart" that is not a no-op.
        /// </remarks>
        public void RestartRegion()
        {
            if (_player == null || _built == null) return;

            RespawnFeet = _built.PlayerStartFeet;

            // Restart means returning to the region's true beginning. Keeping the old checkpoint
            // id would make a later death jump back to that checkpoint, contradicting the restart.
            if (_session != null && !string.IsNullOrEmpty(_session.World.ActiveCheckpointId))
            {
                _session.World.ActiveCheckpointId = string.Empty;
                _session.RequestSave();
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null) _enemies[i].ResetForSpawn(_enemyFeet[i]);
            }

            for (int i = 0; i < _built.Checkpoints.Count; i++) _built.Checkpoints[i].Rearm();

            // Already recorded discoveries stay collected across both restart and death respawn.
            HideRecordedDiscoveries();

            _respawnAt = float.NegativeInfinity;
            float halfHeight = _player.Tuning != null ? _player.Tuning.BodyHeight * 0.5f : 0.7f;
            _player.ResetForRespawn(RespawnFeet + new Vector2(0f, halfHeight));
            _player.InputEnabled = true;

            if (_camera != null) _camera.SnapToTarget();
        }

        private void HideRecordedDiscoveries()
        {
            if (_built == null) return;
            for (int i = 0; i < _built.Discoveries.Count; i++)
            {
                DiscoveryTrigger discovery = _built.Discoveries[i];
                if (discovery != null && discovery.AlreadyRecorded)
                    discovery.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Returns the player to the last checkpoint and restores the encounters. Public so a test or
        /// a debug command can drive the same path the death flow uses.
        /// </summary>
        public void PerformRespawn()
        {
            if (_player == null || _built == null) return;

            // This method is public for debug/test use too, so make repeated calls idempotent.
            _respawnAt = float.NegativeInfinity;
            RespawnFeet = ResolveRespawnFeet();

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null) _enemies[i].ResetForSpawn(_enemyFeet[i]);
            }

            // Keep the checkpoint we are actually respawning at visibly active. Other checkpoints
            // are re-armed because the run's single saved checkpoint id is the source of truth.
            string activeCheckpointId = _session != null ? _session.World.ActiveCheckpointId : string.Empty;
            for (int i = 0; i < _built.Checkpoints.Count; i++)
            {
                CheckpointTrigger checkpoint = _built.Checkpoints[i];
                if (checkpoint == null) continue;

                if (!string.IsNullOrEmpty(activeCheckpointId) &&
                    checkpoint.CheckpointId == activeCheckpointId)
                    checkpoint.RestoreActivated();
                else
                    checkpoint.Rearm();
            }
            HideRecordedDiscoveries();

            float halfHeight = _player.Tuning != null ? _player.Tuning.BodyHeight * 0.5f : 0.7f;
            _player.ResetForRespawn(RespawnFeet + new Vector2(0f, halfHeight));
            _player.InputEnabled = true;

            // The view moves with the player, not after them. Without this the camera glides across
            // the whole region after every death, which reads as the game losing its place.
            if (_camera != null) _camera.SnapToTarget();
        }

        /// <summary>The checkpoint the session says is active, or the level's start.</summary>
        private Vector2 ResolveRespawnFeet()
        {
            if (_built == null) return Vector2.zero;

            string activeId = _session != null ? _session.World.ActiveCheckpointId : null;
            if (!string.IsNullOrEmpty(activeId))
            {
                for (int i = 0; i < _built.Checkpoints.Count; i++)
                {
                    CheckpointTrigger checkpoint = _built.Checkpoints[i];
                    if (checkpoint != null && checkpoint.CheckpointId == activeId) return checkpoint.RespawnFeet;
                }

                Debug.LogWarning(
                    $"The session records checkpoint '{activeId}' but this level has no such " +
                    "checkpoint. Falling back to the level start and clearing the stale checkpoint id.");
                if (_session != null)
                {
                    _session.World.ActiveCheckpointId = string.Empty;
                    _session.RequestSave();
                }
            }

            return _built.PlayerStartFeet;
        }
    }
}
