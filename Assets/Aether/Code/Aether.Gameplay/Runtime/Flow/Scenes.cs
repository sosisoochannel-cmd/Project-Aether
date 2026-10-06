namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// The scenes the game can be in, by the name the build knows them by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scene names are strings to Unity, which means a typo is a runtime exception on a device
    /// rather than a compile error. Naming them once, here, means the project has three spellings of
    /// its own flow instead of a dozen, and it gives <c>tools/verify/verify.py</c> something exact
    /// to check against the build order: every constant below has to be an enabled scene, or the
    /// build fails before it is installed on anything.
    /// </para>
    /// <para>
    /// The order is the flow: the studio intro shows first, hands over to boot, and boot either
    /// opens the main menu or starts the region the menu asked for.
    /// </para>
    /// </remarks>
    public static class Scenes
    {
        /// <summary>The studio ident. Scene 0 in the build, so it is what a cold launch shows.</summary>
        public const string StudioIntro = "StudioIntro";

        /// <summary>Resolves what the app should be doing and loads it.</summary>
        public const string Boot = "Boot";

        /// <summary>Where the player chooses what to play.</summary>
        public const string MainMenu = "MainMenu";
    }
}
