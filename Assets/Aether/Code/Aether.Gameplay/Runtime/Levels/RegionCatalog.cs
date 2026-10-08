using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Stable ids for playable regions. UI and progression should reference these ids rather than
    /// copying Resources paths throughout the project.
    /// </summary>
    public static class RegionCatalog
    {
        public const string Greenway = "region1.greenway";
        public const string WhisperingWoods = "region2.whispering_woods";

        public static string PathFor(string regionId)
        {
            switch (regionId)
            {
                case Greenway:
                    return "Levels/region1.greenway.level";
                case WhisperingWoods:
                    return "Levels/region2.whispering_woods.level";
                default:
                    return null;
            }
        }

        public static bool Exists(string regionId)
        {
            string path = PathFor(regionId);
            return !string.IsNullOrEmpty(path) && Resources.Load<TextAsset>(path) != null;
        }
    }
}
