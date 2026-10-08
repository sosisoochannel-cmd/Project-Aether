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
    [RequireComponent(typeof(Collider2D))]
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

        private static string GetNextRegionId(string exitId)
        {
            if (string.IsNullOrEmpty(exitId)) return null;
            if (exitId.StartsWith("exit.greenway.")) return "region2.glassroot_caverns";
            if (exitId.StartsWith("exit.glassroot_caverns.")) return "region3.ashen_ridge";
            return null;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_entity == null || Reached) return;
            if (other.GetComponent<Player.PlayerController>() == null) return;

            GameSession session = GameSession.Instance;
            if (session == null) return;

            Reached = true;
            session.SetWorldFlag(CompletionFlag);

            // Region completion also unlocks the authored next region. The actual scene loader can
            // consume this stable flag later; progression is therefore saved even when a build uses
            // a menu/transition screen between regions.
            string nextRegion = GetNextRegionId(_entity.Id);
            if (!string.IsNullOrEmpty(nextRegion))
            {
                session.SetWorldFlag("region.unlocked." + nextRegion);
            }

            Debug.Log(
                $"Region complete: '{CompletionFlag}' recorded." +
                (string.IsNullOrEmpty(nextRegion) ? string.Empty : $" Next region '{nextRegion}' unlocked."),
                this);
        }
    }
}
