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
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class DiscoveryTrigger : MonoBehaviour
    {
        private LevelEntity _entity;

        /// <summary>Stable entity id used by the collection catalogue.</summary>
        public string Id => _entity != null ? _entity.Id : null;

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
                if (session == null || _entity == null) return false;

                // Either half of an older save can survive independently. Treat either as found,
                // then let LevelBootstrap.RecordFinding repair the missing half before hiding this
                // trigger so a collection entry can never become permanently unreachable.
                bool flagRecorded = !string.IsNullOrEmpty(_entity.Flag)
                    && session.World.IsSet(_entity.Flag);
                bool collectionRecorded = !string.IsNullOrEmpty(_entity.Id)
                    && session.Collection != null
                    && session.Collection.Has(_entity.Id);
                return flagRecorded || collectionRecorded;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_entity == null || Found) return;
            if (string.IsNullOrEmpty(_entity.Flag)) return;
            if (other.GetComponent<Player.PlayerController>() == null) return;

            GameSession session = GameSession.Instance;
            if (session == null) return;

            Found = true;

            // Recorded as a find rather than only as a world flag: the flag is what the level and the
            // save agree on, and the collection entry is what the collection screen lists. Recording
            // both here is what keeps the counter in the strip and the list in the menu from ever
            // disagreeing about how many secrets the region holds.
            session.RecordFinding(_entity.Flag, _entity.Id);

            // The find is hidden once taken, so a player who comes back does not read a second,
            // emptier secret sitting where the first one was.
            gameObject.SetActive(false);
        }
    }
}
