using Aether.Data.Levels;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// A resting place: touching it makes it the point the player returns to after death.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything about this checkpoint — where it stands, where the player comes back, and which id
    /// records it — comes from level data. Nothing is hard-coded here, so moving a checkpoint is an
    /// edit to a text file and cannot leave the respawn point behind.
    /// </para>
    /// <para>
    /// The respawn position is an explicit tile in the data rather than "wherever the player was
    /// standing". Respawning at the trigger's own position is how players end up inside geometry
    /// after the level shifts under them, and it makes "no softlock" impossible to guarantee.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CheckpointTrigger : MonoBehaviour
    {
        private LevelEntity _entity;
        private Vector2 _feet;

        /// <summary>Id recorded in the save file when this checkpoint is used.</summary>
        public string CheckpointId => _entity != null ? _entity.Id : null;

        /// <summary>Where the player is returned to, as the bottom of their feet.</summary>
        public Vector2 RespawnFeet => _feet;

        /// <summary>True once this checkpoint has been activated in this session.</summary>
        public bool Activated { get; private set; }

        /// <summary>
        /// Raised with true when the checkpoint takes, and false when it is re-armed for a retry.
        /// Presentation listens; nothing in the retry flow depends on anyone listening.
        /// </summary>
        public event System.Action<bool> ActivatedChanged;

        /// <summary>Called by the level builder. Everything needed comes from the entity.</summary>
        public void Configure(LevelEntity entity, Vector2 feet)
        {
            _entity = entity;
            _feet = feet;
            Activated = false;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Player.PlayerController player = other.GetComponent<Player.PlayerController>();
            if (player != null) TryInteract(player);
        }

        /// <summary>
        /// Activates from the keyboard/gamepad Interact command when the player is close enough.
        /// Touching the trigger still uses the same operation, so both paths persist identical state.
        /// </summary>
        public bool TryInteract(Player.PlayerController player)
        {
            if (_entity == null || Activated || player == null) return false;
            if (((Vector2)player.transform.position - (Vector2)transform.position).sqrMagnitude > 2.25f)
                return false;

            GameSession session = GameSession.Instance;
            if (session == null) return false;

            Activated = true;
            session.ActivateCheckpoint(_entity.Id, _feet);
            ActivatedChanged?.Invoke(true);
            return true;
        }

        /// <summary>
        /// Re-arms the trigger, used when returning to the level. Whether it *does* anything is
        /// decided by the session: activating an already-active checkpoint publishes the event again,
        /// which is how the presentation layer knows to play the "rested" beat.
        /// </summary>
        public void Rearm()
        {
            if (!Activated) return;
            Activated = false;
            ActivatedChanged?.Invoke(false);
        }
    }
}
