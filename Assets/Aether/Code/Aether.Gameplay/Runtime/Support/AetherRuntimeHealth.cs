using Aether.Data.Config;
using Aether.Gameplay.Localization;
using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Small development-build smoke gate for the things that static analysis cannot prove:
    /// Resources-backed content, localization tables, and the current menu presentation seam.
    /// It never changes gameplay and never runs in a release player.
    /// </summary>
    public static class AetherRuntimeHealth
    {
        private static bool _ran;

        public static void Run()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_ran) return;
            _ran = true;

            int failures = 0;
            failures += Require<TextAsset>("Levels/region1.greenway.level", "Greenway level data");
            failures += Require<EnemyDefinition>("Content/Enemies/ForestStalker", "Forest Stalker archetype");
            failures += Require<EnemyDefinition>("Content/Enemies/ThornCrawler", "Thorn Crawler archetype");
            failures += Require<EnemyDefinition>("Content/Enemies/CanopyWatcher", "Canopy Watcher archetype");
            failures += Require<AttackDefinition>("Content/Attack.Stalker", "Stalker attack");
            failures += Require<AttackDefinition>("Content/Attack.Strike", "Player strike");
            failures += Require<AttackDefinition>("Content/Attack.StrikeFollowUp", "Player strike follow-up");

            int offered = LanguageService.OfferedCount;
            for (int i = 0; i < LanguageService.OfferedCodes().Count; i++)
            {
                string code = LanguageService.OfferedCodes()[i];
                int present;
                int total;
                LanguageService.Coverage(code, out present, out total);
                if (present != total || total == 0)
                {
                    failures++;
                    Debug.LogError("[aether-health] Incomplete offered language " + code +
                                   ": " + present + "/" + total + " strings.");
                }
            }

            if (offered < 3)
            {
                failures++;
                Debug.LogError("[aether-health] Expected the shipped English, Spanish and Persian languages.");
            }

            Sprite forest = Resources.Load<Sprite>("Menu/MainMenuForest");
            if (forest == null)
            {
                failures++;
                Debug.LogError("[aether-health] Main menu forest artwork is missing.");
            }

            if (failures == 0)
                Debug.Log("[aether-health] Runtime content smoke gate: PASS (" +
                          offered + " offered languages, core level/combat resources present).");
            else
                Debug.LogError("[aether-health] Runtime content smoke gate: FAIL (" +
                               failures + " issue(s)).");
#endif
        }

        private static int Require<T>(string path, string label) where T : Object
        {
            if (Resources.Load<T>(path) != null) return 0;
            Debug.LogError("[aether-health] Missing " + label + " at Resources/" + path);
            return 1;
        }
    }
}
