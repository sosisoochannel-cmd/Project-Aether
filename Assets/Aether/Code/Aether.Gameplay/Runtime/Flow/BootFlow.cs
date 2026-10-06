using Aether.Gameplay.Levels;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// The scene that decides what the app is doing: the main menu, or the region the player asked
    /// for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Boot used to be the region itself — the fallback boot started a level in whatever scene was
    /// loaded. With a menu in front of the game there are two answers to "what happens now", and
    /// they cannot both live in the scene the menu loads to reach the region. So boot became a
    /// question instead of an answer, and the question is asked in exactly one place.
    /// </para>
    /// <para>
    /// It costs nothing: the hand-over is one <c>LoadScene</c> in the path that needs it, no frame
    /// is drawn, and the region path is the same code path it always was.
    /// </para>
    /// <para>
    /// Being a <see cref="SceneFlowOwner"/> is what keeps the automatic boot out of this scene: the
    /// fallback exists to make a fresh clone playable by pressing play, and this scene has plans of
    /// its own.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BootFlow : SceneFlowOwner
    {
        private void Start()
        {
            // Settings first: they decide the frame cap, the quality tier and the screen timeout,
            // and both destinations want them applied before they draw anything.
            AetherSettings.Ensure();

            if (GameLaunch.TakePlayRequest())
            {
                // The player asked to play. This scene has always been able to boot a region; it
                // still is, and the menu simply chooses when it does.
                SaveHost.Ensure();
                if (FindAnyObjectByType<LevelBootstrap>() == null)
                {
                    gameObject.AddComponent<LevelBootstrap>();
                }

                return;
            }

            SceneManager.LoadScene(Scenes.MainMenu);
        }
    }
}
