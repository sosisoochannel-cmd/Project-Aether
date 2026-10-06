using Aether.Gameplay.Flow;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The two things a menu can do that leave it: start the game, and close it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are one-way doors, and both are the kind of thing that ends up written three times — once
    /// in the main menu, once in the settings screen's quit row, once somewhere a month later. They
    /// live here so that \"leaving the menu\" is one piece of code with one order of operations: the
    /// things that must be written down are written down, the sound is faded rather than cut, and the
    /// scene change goes through the transition system like every other change of screen.
    /// </para>
    /// <para>
    /// <b>The region is started by boot, not by the menu.</b> A scene load destroys the object that
    /// asked for it, so a menu cannot start a region and then watch it happen. The request is written
    /// to <see cref="GameLaunch"/>, boot reads it once, and the menu's only job is to have asked.
    /// </para>
    /// </remarks>
    public static class MenuFlow
    {
        /// <summary>Saves, fades the menu's music, and hands over to the region.</summary>
        public static void PlayRegion()
        {
            // Progress first: the menu is the last moment before a load in which a write is free
            // (the player has already stopped pressing things) and the first moment after which the
            // object doing the writing is gone.
            SaveHost.SaveNow();

            MenuAudio audio = MenuAudio.Instance;
            if (audio != null)
            {
                // As long as the transition takes, and no longer: the veil reaches full cover at the
                // same moment the music reaches silence, so the scene change is never heard as a cut.
                audio.FadeOut(MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
            }

            GameLaunch.RequestPlay();
            MenuTransition.Instance.GoToScene(Scenes.Boot);
        }

        /// <summary>
        /// Closes the game, having written down what is worth keeping.
        /// </summary>
        /// <remarks>
        /// Only ever reached from a confirmed action. On a phone this is a request to the platform
        /// rather than a guarantee — Android is free to keep the process — and the write before it is
        /// what makes that harmless.
        /// </remarks>
        public static void Quit()
        {
            SaveHost.SaveNow();
            AetherSettings.Flush();
            Application.Quit();
        }
    }
}
