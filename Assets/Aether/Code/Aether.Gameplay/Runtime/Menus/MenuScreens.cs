using Aether.Gameplay.Menus.Screens;
using UnityEngine;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Every screen the menu can show, named.
    /// </summary>
    /// <remarks>
    /// The set is closed on purpose: an enum of screens is a list a tool can check, and the menu's
    /// entry points have to agree with it, which is what stops a row from existing that opens
    /// nothing. Adding a screen is a member here, a case in <see cref="MenuScreens.Create"/>, and a
    /// row somewhere that asks for it.
    /// </remarks>
    public enum MenuScreenId
    {
        /// <summary>The first screen: brand, primary choices, the rest of the destinations.</summary>
        MainMenu = 0,

        /// <summary>Settings, category by category.</summary>
        Settings = 1,

        /// <summary>Credits and legal.</summary>
        Credits = 2,

        /// <summary>The regions and their chapters. One is playable.</summary>
        Chapters = 3,

        /// <summary>Who the player has met. Not in the build yet.</summary>
        Characters = 4,

        /// <summary>What the player has found. Not in the build yet.</summary>
        Collection = 5,

        /// <summary>What the player has achieved.</summary>
        Achievements = 6,

        /// <summary>Where a new run is put, and what each stored run contains.</summary>
        SaveSlots = 7,
    }

    /// <summary>
    /// Makes screens, and says what each one is called.
    /// </summary>
    /// <remarks>
    /// Screens are built once and kept by the host, so this is a factory rather than a manager: it
    /// decides which type implements which id, and nothing else about them.
    /// </remarks>
    public static class MenuScreens
    {
        /// <summary>Builds the screen for an id, under the host's content root.</summary>
        public static MenuScreen Create(MenuScreenId id, Transform parent, MenuSystem host)
        {
            switch (id)
            {
                case MenuScreenId.Settings:
                    return MenuScreen.Create<SettingsScreen>(id, parent, host);
                case MenuScreenId.Credits:
                    return MenuScreen.Create<CreditsScreen>(id, parent, host);
                case MenuScreenId.Chapters:
                    return MenuScreen.Create<ChaptersScreen>(id, parent, host);
                case MenuScreenId.Characters:
                    return MenuScreen.Create<CharactersScreen>(id, parent, host);
                case MenuScreenId.Collection:
                    return MenuScreen.Create<CollectionScreen>(id, parent, host);
                case MenuScreenId.Achievements:
                    return MenuScreen.Create<AchievementsScreen>(id, parent, host);
                case MenuScreenId.SaveSlots:
                    return MenuScreen.Create<SaveSlotScreen>(id, parent, host);
                default:
                    return MenuScreen.Create<MainMenuScreen>(id, parent, host);
            }
        }

        /// <summary>The localisation key for a screen's title.</summary>
        public static string TitleKey(MenuScreenId id)
        {
            switch (id)
            {
                case MenuScreenId.Settings: return "menu.settings";
                case MenuScreenId.Credits: return "credits.title";
                case MenuScreenId.Chapters: return "chapters.title";
                case MenuScreenId.Characters: return "menu.characters";
                case MenuScreenId.Collection: return "menu.collection";
                case MenuScreenId.Achievements: return "menu.achievements";
                case MenuScreenId.SaveSlots: return "slots.title";
                default: return "menu.title";
            }
        }

        /// <summary>
        /// The localisation key for the paragraph a screen that is not built yet shows.
        /// </summary>
        /// <remarks>
        /// A screen that has nothing behind it says so in one sentence, in the players' language,
        /// from the string table. It is not a placeholder: the entry point is real, and this is what
        /// it honestly has to say until the system it opens exists.
        /// </remarks>
        public static string BodyKey(MenuScreenId id)
        {
            switch (id)
            {
                case MenuScreenId.Characters: return "locked.characters.body";
                case MenuScreenId.Collection: return "locked.collection.body";
                case MenuScreenId.Achievements: return "locked.achievements.body";
                case MenuScreenId.Chapters: return "chapters.locked";
                default: return null;
            }
        }
    }
}
