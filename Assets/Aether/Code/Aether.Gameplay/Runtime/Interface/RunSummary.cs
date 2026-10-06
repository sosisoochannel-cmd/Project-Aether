using Aether.Gameplay.Progression;
using UnityEngine;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// The numbers a run is described by: how long it has been played, how many times it has ended,
    /// and how much of the region it has found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three screens show these — the strip while playing, the pause menu, and the summary at the end
    /// — and they must agree, to the digit. They agree because there is one reader: this class. The
    /// alternative, which was the first draft, was each screen counting findings for itself, and the
    /// second draft of any counter is where two screens start disagreeing after a change.
    /// </para>
    /// <para>
    /// <b>Where each number comes from.</b> Time and deaths are the session's own record, written into
    /// the save file. Findings are counted from the collection catalogue's findings category — the
    /// region's real secrets — against the session's raised world flags, so a secret added to the
    /// level changes both the count and the total in the same commit.
    /// </para>
    /// </remarks>
    public static class RunSummary
    {
        /// <summary>Seconds this run has been played, with paused time excluded.</summary>
        public static float PlaySeconds(GameSession session)
        {
            if (session == null || session.Save.Meta == null) return 0f;
            return session.Save.Meta.PlaySeconds;
        }

        /// <summary>How many times the player has fallen in this run.</summary>
        public static int Deaths(GameSession session)
        {
            if (session == null || session.Save.Meta == null) return 0;
            return session.Save.Meta.Deaths;
        }

        /// <summary>How many of the region's secrets this run has found.</summary>
        public static int Findings(GameSession session)
        {
            if (session == null) return 0;

            CollectionCategory findings = CollectionCatalog.Find(CollectionCatalog.FindingsId);
            if (findings == null) return 0;

            int found = 0;
            for (int i = 0; i < findings.Entries.Length; i++)
            {
                CollectionEntry entry = findings.Entries[i];
                if (entry.NotInBuild) continue;
                if (session.World.IsSet(entry.Id)) found++;
            }

            return found;
        }

        /// <summary>How many secrets the region holds, in total, as the catalogue defines them.</summary>
        public static int FindingsTotal()
        {
            CollectionCategory findings = CollectionCatalog.Find(CollectionCatalog.FindingsId);
            if (findings == null) return 0;

            int total = 0;
            for (int i = 0; i < findings.Entries.Length; i++)
            {
                if (!findings.Entries[i].NotInBuild) total++;
            }

            return total;
        }

        /// <summary>A playtime a person reads at a glance: minutes and seconds, then hours.</summary>
        public static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;

            return hours > 0
                ? hours + "h " + minutes.ToString("00") + "m"
                : minutes + "m " + (total % 60).ToString("00") + "s";
        }
    }
}
