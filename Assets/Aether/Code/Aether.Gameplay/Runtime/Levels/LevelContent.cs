using System.Collections.Generic;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// The tuning assets a level needs, loaded from one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These live under <c>Resources/Content</c> so that the game boots with no scene wiring at all:
    /// a level is a text file plus a handful of tuning assets, and both are found by name. That is
    /// the whole loading architecture for this milestone, and it is deliberately the smallest thing
    /// that works — one small text file and four small assets do not justify Addressables, and
    /// pretending otherwise would be architecture built for a problem that does not exist yet.
    /// </para>
    /// <para>
    /// A missing asset is reported by name and by path, because "the player does not move" is a much
    /// worse bug report than "PlayerTuning was not found".
    /// </para>
    /// </remarks>
    public sealed class LevelContent
    {
        private readonly Dictionary<string, EnemyDefinition> _enemies =
            new Dictionary<string, EnemyDefinition>(System.StringComparer.Ordinal);

        private LevelContent()
        {
        }

        /// <summary>Movement and combat feel for the player.</summary>
        public PlayerTuningData PlayerTuning { get; private set; }

        /// <summary>First attack of the player's chain; follow-ups hang off it.</summary>
        public AttackDefinition PlayerFirstAttack { get; private set; }

        /// <summary>Every enemy archetype the level may spawn, keyed by its type id.</summary>
        public IReadOnlyDictionary<string, EnemyDefinition> Enemies => _enemies;

        /// <summary>Looks up an enemy archetype by the id used in level data.</summary>
        public bool TryGetEnemy(string typeId, out EnemyDefinition definition)
        {
            if (string.IsNullOrEmpty(typeId))
            {
                definition = null;
                return false;
            }
            return _enemies.TryGetValue(typeId, out definition);
        }

        /// <summary>Loads the standard content set. Individual misses are logged and left null.</summary>
        public static LevelContent Load()
        {
            var content = new LevelContent
            {
                PlayerTuning = Load<PlayerTuningData>("Content/PlayerTuning"),
                PlayerFirstAttack = Load<AttackDefinition>("Content/Attack.Strike"),
            };

            EnemyDefinition[] enemies = Resources.LoadAll<EnemyDefinition>("Content/Enemies");
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyDefinition definition = enemies[i];
                if (definition == null) continue;

                if (string.IsNullOrEmpty(definition.TypeId))
                {
                    throw new System.InvalidOperationException(
                        $"Enemy definition '{definition.name}' has no type id. " +
                        "Set a unique type id in the inspector before the level can start.");
                }

                if (content._enemies.ContainsKey(definition.TypeId))
                {
                    throw new System.InvalidOperationException(
                        $"Two enemy definitions claim the type id '{definition.TypeId}'. " +
                        "The level cannot start until every enemy archetype id is unique.");
                }

                content._enemies.Add(definition.TypeId, definition);
            }

            return content;
        }

        private static T Load<T>(string path) where T : Object
        {
            T asset = Resources.Load<T>(path);
            if (asset == null)
            {
                Debug.LogError(
                    $"Content asset 'Resources/{path}' was not found. The level cannot be built " +
                    "without it. Re-run Aether > Bake Region 1 (Greenway) to recreate missing " +
                    "content, or restore the asset from version control.");
            }
            return asset;
        }
    }
}
