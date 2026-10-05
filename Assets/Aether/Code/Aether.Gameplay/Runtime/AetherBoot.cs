using Aether.Gameplay.Flow;
using Aether.Gameplay.Levels;
using UnityEngine;

namespace Aether.Gameplay
{
    /// <summary>
    /// Starts the game without scene wiring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buildable project contains a camera and nothing else, and the scenes in it were authored
    /// without an Editor to verify them in. Rather than depend on a component being placed in a
    /// scene by hand — a step nobody can check from here — the boot is a runtime hook: when play
    /// starts and no scene has claimed responsibility for the region, this starts it.
    /// </para>
    /// <para>
    /// The hook stands down the moment a scene contains its own <see cref="LevelBootstrap"/>, or a
    /// <see cref="SceneFlowOwner"/> — a scene that has its own plans for what happens after it loads
    /// (the studio intro, and later a main menu). That keeps the choice where it belongs: the default
    /// gets a fresh clone playing by pressing play, and any scene that wants explicit control simply
    /// places the component and this stays quiet.
    /// </para>
    /// <para>
    /// This is the seam that grows into real scene flow. It is deliberately not a loading system:
    /// there is one region, and inventing a state machine for the second one before it exists would
    /// decide how the game loads before the game knows.
    /// </para>
    /// </remarks>
    public static class AetherBoot
    {
        /// <summary>Turn off to take full manual control of the boot in tests and tools.</summary>
        public static bool AutoBootEnabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootIfUnclaimed()
        {
            if (!AutoBootEnabled) return;
            if (Object.FindAnyObjectByType<LevelBootstrap>() != null) return;
            if (Object.FindAnyObjectByType<SceneFlowOwner>() != null) return;

            var host = new GameObject("LevelBootstrap");
            host.AddComponent<LevelBootstrap>();
        }
    }
}
