using Aether.Data.Levels;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// A find. Touching it records a world flag so the discovery survives death and reload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first secret in The Greenway is the first thing in the game that says "the world has more
    /// in it than the path" — and the whole point is that it is found, not announced. So this
    /// component records the flag and stops there: no popup, no dialogue, no cutscene. The clue
    /// itself is a thing placed in the level, and what the player makes of it is their own.
    /// </para>
    /// <para>
    /// Nothing here plays a sound either. Sound is a seam with a director behind it, and wiring a cue
    /// to a secret before the cue list exists would be inventing content this milestone does not
    /// need.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Collider2D))]
    public sealed class DiscoveryTrigger : MonoBehaviour
    {
        private LevelEntity _entity;

        /// <summary>Flag raised in the save file when this is found.</summary>
        public string FlagId => _entity != null ? _entity.Flag : null;

        /// <summary>What kind of find this is, from level data.</summary>
        public string MarkerKind => _entity != null ? _entity.MarkerKind : null;

        /// <summary>True once this discovery has been recorded in this session.</summary>
        public bool Found { get; private set; }

        /// <summary>Called by the level builder.</summary>
        public void Configure(LevelEntity entity)
        {
            _entity = entity;
            Found = false;
        }

        /// <summary>True when this discovery was already found in a previous session.</summary>
        public bool AlreadyRecorded
        {
            get
            {
                GameSession session = GameSession.Instance;
                return session != null && _entity != null && !string.IsNullOrEmpty(_entity.Flag)
                       && session.World.IsSet(_entity.Flag);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Player.PlayerController player = other.GetComponent<Player.PlayerController>();
            if (player != null) TryInteract(player);
        }

        /// <summary>
        /// Records the finding from the Interact command when the player is beside it. Automatic
        /// trigger entry remains available for touch-first play and calls this same path.
        /// </summary>
        public bool TryInteract(Player.PlayerController player)
        {
            if (_entity == null || Found || player == null) return false;
            if (string.IsNullOrEmpty(_entity.Flag)) return false;
            if (((Vector2)player.transform.position - (Vector2)transform.position).sqrMagnitude > 2.25f)
                return false;

            GameSession session = GameSession.Instance;
            if (session == null) return false;

            Found = true;

            // Recorded as a find rather than only as a world flag: the flag is what the level and the
            // save agree on, and the collection entry is what the collection screen lists. Recording
            // both here is what keeps the counter in the strip and the list in the menu from ever
            // disagreeing about how many secrets the region holds.
            session.RecordFinding(_entity.Flag, _entity.Id);

            // The find is hidden once taken, so a player who comes back does not read a second,
            // emptier secret sitting where the first one was.
            gameObject.SetActive(false);
            return true;
        }
    }
}
