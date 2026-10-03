using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// The physics layers gameplay is built on, resolved by name in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layers are a project setting, not code, so they cannot be referenced directly. Resolving them
    /// by name here means a typo produces one loud error naming the missing layer instead of a
    /// character that silently never touches the ground — which is exactly the failure that costs an
    /// afternoon.
    /// </para>
    /// <para>
    /// The split that matters is <b>Ground</b> versus <b>Player</b>. Every grounding probe must
    /// exclude the probing body's own layer; with one shared layer the player's foot probe finds the
    /// player and the character is grounded forever.
    /// </para>
    /// </remarks>
    public static class GameplayLayers
    {
        public const string GroundName = "Ground";
        public const string PlayerName = "Player";
        public const string EnemyName = "Enemy";
        public const string InteractableName = "Interactable";

        private static int _reportedMissing = -1;

        /// <summary>Everything solid the player and enemies stand on or bump into.</summary>
        public static LayerMask Ground => MaskOf(GroundName);

        /// <summary>The player's own layer.</summary>
        public static LayerMask Player => MaskOf(PlayerName);

        /// <summary>Hostile bodies. What player attacks are allowed to hit.</summary>
        public static LayerMask Enemy => MaskOf(EnemyName);

        /// <summary>Checkpoints, discoveries and the region exit.</summary>
        public static LayerMask Interactable => MaskOf(InteractableName);

        /// <summary>Layer index for a name, or -1 when the project does not define it.</summary>
        public static int IndexOf(string name)
        {
            int index = LayerMask.NameToLayer(name);
            if (index < 0)
            {
                // Report each missing layer once. This runs during setup, so it cannot spam a frame,
                // but a level built repeatedly in one session should not repeat the same error.
                int hash = name.GetHashCode();
                if (_reportedMissing != hash)
                {
                    _reportedMissing = hash;
                    Debug.LogError(
                        $"The project has no layer named '{name}'. Add it in Project Settings > Tags and " +
                        "Layers; gameplay physics will not work until it exists.");
                }
                return 0;
            }
            return index;
        }

        /// <summary>A mask containing one layer by name. Zero when the layer does not exist.</summary>
        public static LayerMask MaskOf(string name)
        {
            int index = IndexOf(name);
            return index <= 0 ? default : 1 << index;
        }

        /// <summary>Assigns a layer to an object and everything below it, without allocating.</summary>
        public static void Assign(GameObject root, string layerName)
        {
            int index = IndexOf(layerName);
            if (index <= 0) return;

            root.layer = index;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                children[i].gameObject.layer = index;
            }
        }
    }
}
