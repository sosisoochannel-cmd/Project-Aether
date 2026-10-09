using Aether.Data.Levels;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// The end of the region. Records that the player got here and stops.
    /// </summary>
    /// <remarks>
    /// What comes after The Greenway — the Whispering Woods, a transition, a title card — is a later
    /// milestone, so this deliberately does nothing more than mark the region complete in the session.
    /// Writing a placeholder "to be continued" screen now would be building presentation for content
    /// that does not exist, and it would have to be deleted.
    /// </remarks>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class LevelExitTrigger : MonoBehaviour
    {
        private LevelEntity _entity;

        /// <summary>Flag raised in the save file when the region is completed.</summary>
        public string CompletionFlag { get; private set; }

        /// <summary>True once the player has reached the exit in this session.</summary>
        public bool Reached { get; private set; }

        /// <summary>Called by the level builder.</summary>
        public void Configure(LevelEntity entity)
        {
            _entity = entity;
            CompletionFlag = entity != null ? entity.Id.Replace("exit.", "region.completed.") : null;
            Reached = false;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_entity == null || Reached) return;
            if (other.GetComponent<Player.PlayerController>() == null) return;

            GameSession session = GameSession.Instance;
            if (session == null) return;

            Reached = true;
            session.SetWorldFlag(CompletionFlag);
            Debug.Log(
                $"The Greenway is complete. '{CompletionFlag}' recorded; region 2 is not part of this milestone.",
                this);
        }
    }
}
