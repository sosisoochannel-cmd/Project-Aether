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
        private const float InteractDistance = 1.5f;

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
            _session = session;
            _built = built;
            _player = player;
            _camera = camera;

            _enemies.Clear();
            _enemyFeet.Clear();
            for (int i = 0; i < built.Enemies.Count; i++)
            {
                _enemies.Add(built.Enemies[i].Controller);
                _enemyFeet.Add(built.Enemies[i].Feet);
            }

            RespawnFeet = ResolveRespawnFeet();

            if (_player != null)
            {
                _player.Health.Died += OnPlayerDied;
            }
        }

        private void OnDestroy()
        {
            if (_player != null && _player.Health != null) _player.Health.Died -= OnPlayerDied;
        }

        private void Update()
        {
            if (_respawnAt > float.NegativeInfinity && Time.time >= _respawnAt)
            {
                _respawnAt = float.NegativeInfinity;
                PerformRespawn();
            }

            if (_player == null || !_player.InputEnabled || _player.Input == null) return;
            if (!_player.Input.InteractPressed) return;

            // Consume once at the gameplay boundary. Interact is now a real command for keyboard
            // and gamepad players: it can activate the nearest checkpoint, finding or exit without
            // creating a second input path for the mobile touch overlay.
            _player.Input.ConsumeInteract();
            TryInteract();
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
        /// Uses Interact on the closest nearby level object. Trigger entry remains the primary
        /// touch-friendly path; this command is the deliberate path for keyboard and gamepad.
        /// </summary>
        private void TryInteract()
        {
            if (_built == null || _player == null) return;

            Vector2 playerPosition = _player.transform.position;
            float best = InteractDistance * InteractDistance;
            CheckpointTrigger checkpoint = null;
            DiscoveryTrigger discovery = null;
            LevelExitTrigger exit = null;

            for (int i = 0; i < _built.Checkpoints.Count; i++)
            {
                CheckpointTrigger candidate = _built.Checkpoints[i];
                if (candidate == null || candidate.Activated) continue;
                float distance = ((Vector2)candidate.transform.position - playerPosition).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    checkpoint = candidate;
                    discovery = null;
                    exit = null;
                }
            }

            for (int i = 0; i < _built.Discoveries.Count; i++)
            {
                DiscoveryTrigger candidate = _built.Discoveries[i];
                if (candidate == null || candidate.Found || !candidate.gameObject.activeInHierarchy) continue;
                float distance = ((Vector2)candidate.transform.position - playerPosition).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    checkpoint = null;
                    discovery = candidate;
                    exit = null;
                }
            }

            if (_built.Exit != null && !_built.Exit.Reached)
            {
                float distance = ((Vector2)_built.Exit.transform.position - playerPosition).sqrMagnitude;
                if (distance < best)
                {
                    checkpoint = null;
                    discovery = null;
                    exit = _built.Exit;
                }
            }

            if (checkpoint != null) checkpoint.TryInteract(_player);
            else if (discovery != null) discovery.TryInteract(_player);
            else if (exit != null) exit.TryInteract(_player);
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

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null) _enemies[i].ResetForSpawn(_enemyFeet[i]);
            }

            for (int i = 0; i < _built.Checkpoints.Count; i++) _built.Checkpoints[i].Rearm();

            // A find the player already took stays taken: it is recorded in the save file, and a
            // restart that let them collect it twice would make the collection's count a lie.
            for (int i = 0; i < _built.Discoveries.Count; i++)
            {
                DiscoveryTrigger discovery = _built.Discoveries[i];
                if (discovery != null && discovery.AlreadyRecorded) discovery.gameObject.SetActive(false);
            }

            _respawnAt = float.NegativeInfinity;
            float halfHeight = _player.Tuning != null ? _player.Tuning.BodyHeight * 0.5f : 0.7f;
            _player.ResetForRespawn(RespawnFeet + new Vector2(0f, halfHeight));
            _player.InputEnabled = true;

            if (_camera != null) _camera.SnapToTarget();
        }

        /// <summary>
        /// Returns the player to the last checkpoint and restores the encounters. Public so a test or
        /// a debug command can drive the same path the death flow uses.
        /// </summary>
        public void PerformRespawn()
        {
            if (_player == null || _built == null) return;

            RespawnFeet = ResolveRespawnFeet();

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null) _enemies[i].ResetForSpawn(_enemyFeet[i]);
            }

            for (int i = 0; i < _built.Checkpoints.Count; i++) _built.Checkpoints[i].Rearm();

            float halfHeight = _player.Tuning != null ? _player.Tuning.BodyHeight * 0.5f : 0.7f;
            _player.ResetForRespawn(RespawnFeet + new Vector2(0f, halfHeight));

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
                    "checkpoint. Falling back to the level start, which is safe but wrong: the level " +
                    "and the save file have drifted apart.");
            }

            return _built.PlayerStartFeet;
        }
    }
}
