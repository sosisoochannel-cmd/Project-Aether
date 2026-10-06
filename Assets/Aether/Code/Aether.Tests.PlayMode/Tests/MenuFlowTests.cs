using System.Collections;
using System.Collections.Generic;
using Aether.Core.Settings;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Menus.Screens;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Builds the real menu in the real engine and drives it, the way a player would.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tools/verify/mainmenu.py</c> proves the menu's arithmetic and its wiring by reading files:
    /// the composition fits, every screen is reachable, every label is a key. What it cannot prove is
    /// that the thing <i>runs</i> — that the canvas comes up, that one press opens one screen, that a
    /// locked row refuses to be pressed, that the event system speaks the Input System. Those are the
    /// failures a static gate cannot see, and they are what this class is for.
    /// </para>
    /// <para>
    /// <b>Nothing here is faked.</b> The menu is built by adding the shipped <see cref="MenuRoot"/> to
    /// a GameObject, exactly as the scene does; the screens are the shipped screens; the buttons are
    /// the shipped buttons. Where a test needs a value it asks the same systems the game asks.
    /// </para>
    /// <para>
    /// It also leaves the settings and the save file alone: the menu is shown with whatever is on
    /// disk, and the one test that needs "no stored run" asserts against
    /// <see cref="SaveHost.HasStoredProgress"/> rather than deleting a file it did not create.
    /// </para>
    /// </remarks>
    public sealed class MenuFlowTests
    {
        private readonly List<GameObject> _built = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // A deterministic starting point for the settings service, without touching the player's
            // file: the tests that read a value want the default, not whatever the machine saved.
            AetherSettings.ResetForTests();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _built.Count; i++)
            {
                if (_built[i] != null) Object.Destroy(_built[i]);
            }

            _built.Clear();

            // The transition is built to outlive a scene load, because in the game it must — and that
            // is exactly why a test may not leave one behind: a full-screen canvas in front of the
            // next test's camera is not a thing any test should have to know about. It is a
            // singleton that rebuilds itself on demand, so taking it away here costs nothing.
            MenuTransition[] transitions = Object.FindObjectsByType<MenuTransition>(FindObjectsSortMode.None);
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null) Object.Destroy(transitions[i].gameObject);
            }

            yield return null;
        }

        /// <summary>Builds the menu the way the scene does, and waits for it to come up.</summary>
        private IEnumerator BuildMenu()
        {
            var host = new GameObject("Menu Root (test)");
            _built.Add(host);
            host.AddComponent<MenuRoot>();

            // Awake runs on AddComponent; the first layout happens there too, and a frame lets the
            // canvas, the event system and the screens settle before anything is asserted.
            yield return null;
            yield return null;
        }

        private MenuRoot CurrentMenu()
        {
            for (int i = 0; i < _built.Count; i++)
            {
                if (_built[i] == null) continue;
                MenuRoot root = _built[i].GetComponent<MenuRoot>();
                if (root != null) return root;
            }

            return null;
        }

        private static IEnumerator Settle(MenuRoot root)
        {
            // The transition is a coroutine over unscaled time; a second of frames is far more than
            // a fade needs and is bounded, so a broken transition fails the test instead of hanging.
            float limit = Time.unscaledTime + 2f;
            while (root.System.Transitioning && Time.unscaledTime < limit) yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator The_menu_comes_up_on_its_first_screen()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            Assert.IsNotNull(root, "the menu root did not build");
            Assert.IsNotNull(root.System, "the menu built no screens");
            Assert.AreEqual(MenuScreenId.MainMenu, root.System.CurrentId,
                            "the menu did not open on the main menu");
            Assert.IsTrue(root.System.Current.Visible, "the main menu screen is not visible");

            // One canvas, and it is the menu's.
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Assert.IsTrue(canvases.Length >= 1, "no canvas was created");
            bool found = false;
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].gameObject == root.Ui.GameObject) found = true;
            }

            Assert.IsTrue(found, "the menu's canvas is not in the scene");
        }

        [UnityTest]
        public IEnumerator The_event_system_speaks_the_input_system()
        {
            yield return BuildMenu();

            EventSystem events = EventSystem.current;
            Assert.IsNotNull(events, "no EventSystem came up with the menu");

            // The project runs on the Input System alone, so the legacy module would throw the first
            // time it read a key. This is the difference between a menu that taps and one that does
            // not, and no static check can see it.
            Assert.IsNotNull(events.GetComponent<InputSystemUIInputModule>(),
                             "the EventSystem has no InputSystemUIInputModule");
            Assert.IsNull(events.GetComponent<StandaloneInputModule>(),
                          "the EventSystem still carries the legacy input module");
        }

        [UnityTest]
        public IEnumerator Every_entry_the_brief_lists_opens_its_screen()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            var destinations = new[]
            {
                MenuScreenId.Chapters, MenuScreenId.Characters, MenuScreenId.Collection,
                MenuScreenId.Achievements, MenuScreenId.Settings, MenuScreenId.Credits,
            };

            for (int i = 0; i < destinations.Length; i++)
            {
                MenuScreenId wanted = destinations[i];
                root.System.GoTo(wanted);
                yield return Settle(root);

                Assert.AreEqual(wanted, root.System.CurrentId,
                                $"pressing the {wanted} entry did not open that screen");
                Assert.IsTrue(root.System.Current.Visible, $"the {wanted} screen is not visible");
                Assert.AreEqual(1, VisibleScreens(root),
                                $"more than one screen is up while {wanted} is open");

                // Back does what back should, without the system back gesture being involved.
                bool handled = root.System.Back();
                Assert.IsTrue(handled, $"the {wanted} screen did not answer the back request");
                yield return Settle(root);

                Assert.AreEqual(MenuScreenId.MainMenu, root.System.CurrentId,
                                $"back from {wanted} did not return to the main menu");
            }
        }

        [UnityTest]
        public IEnumerator Credits_opened_from_settings_returns_to_settings()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            root.System.GoTo(MenuScreenId.Settings);
            yield return Settle(root);

            // Through the settings route the brief describes: ABOUT has a credits row, and the road
            // back from it is the road it was reached by.
            root.System.GoTo(MenuScreenId.Credits);
            yield return Settle(root);
            Assert.AreEqual(MenuScreenId.Credits, root.System.CurrentId);

            root.System.Back();
            yield return Settle(root);

            Assert.AreEqual(MenuScreenId.Settings, root.System.CurrentId,
                            "back from the credits screen did not return to where it was opened from");
        }

        [UnityTest]
        public IEnumerator A_press_that_arrives_twice_opens_one_screen()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            // Two requests in the same frame, which is what a fast double tap produces.
            root.System.GoTo(MenuScreenId.Settings);
            root.System.GoTo(MenuScreenId.Credits);
            yield return Settle(root);

            Assert.AreEqual(MenuScreenId.Settings, root.System.CurrentId,
                            "the second request in the same frame was served as well, so the menu "
                            + "opened two screens from one press");
            Assert.AreEqual(1, ScreenCount(root, MenuScreenId.Settings),
                            "the settings screen was built more than once");
        }

        [UnityTest]
        public IEnumerator A_row_refuses_a_second_activation_in_the_same_frame()
        {
            var host = new GameObject("Row host (test)", typeof(RectTransform));
            _built.Add(host);

            MenuButton row = MenuButton.Create("Row", host.transform, "TEST", MenuButton.Weight.Primary);
            yield return null;

            int activations = 0;
            row.Activated = () => activations++;

            row.Activate();
            row.Activate();
            Assert.AreEqual(1, activations, "one frame produced two activations from one row");

            yield return null;
            row.Activate();
            Assert.AreEqual(2, activations, "a later frame's press was refused as well");

            // A locked row is not pressable at all, on any route.
            row.SetLocked(true, "NOT YET");
            row.Activate();
            Assert.AreEqual(2, activations, "a locked row ran its action");

            // A fact drawn as a row is not pressable either: the navigation must not land on it.
            row.SetLocked(false);
            row.SetInformational(true);
            row.Activate();
            Assert.AreEqual(2, activations, "an informational row ran an action");
            Assert.IsFalse(row.Usable, "an informational row still reports itself as usable");
        }

        [UnityTest]
        public IEnumerator A_confirm_never_starts_on_the_answer_that_destroys_something()
        {
            var host = new GameObject("Dialog host (test)", typeof(RectTransform));
            _built.Add(host);

            var nav = new MenuNav();
            MenuConfirmPanel dialog = MenuConfirmPanel.Create("Confirm", host.transform, nav);
            yield return null;

            dialog.Open("QUESTION", "WHAT WILL HAPPEN", () => { });
            yield return null;

            Assert.IsTrue(dialog.IsOpen, "the dialog did not open");
            Assert.AreSame(dialog.CancelRow, nav.Current,
                           "the dialog opened with the destructive answer selected");

            // Left and right move between the two answers, and the safe one is where it started.
            dialog.OnMove(MenuNav.Move.Right);
            Assert.AreSame(dialog.ConfirmRow, nav.Current, "right did not move to the confirmation");
            dialog.OnMove(MenuNav.Move.Left);
            Assert.AreSame(dialog.CancelRow, nav.Current, "left did not move back to the refusal");

            bool ran = false;
            dialog.Open("QUESTION", "WHAT WILL HAPPEN", () => ran = true);
            dialog.Cancel();
            yield return null;

            Assert.IsFalse(ran, "cancelling the dialog ran its action anyway");
            Assert.IsFalse(dialog.IsOpen, "the dialog stayed open after being cancelled");
        }

        [UnityTest]
        public IEnumerator The_main_menu_locks_continue_when_there_is_nothing_to_continue()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();
            var screen = (MainMenuScreen)root.System.Current;

            // Compared against the label the row would be drawn with, so the check is about the
            // row and not about the order the rows happen to be built in.
            string wanted = MenuUi.Track(MenuStrings.Get("menu.continue"),
                                         MenuTheme.Metrics.PrimaryTracking);
            MenuButton continueRow = null;
            IList<MenuButton> rows = screen.Play.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Label.text == wanted) continueRow = rows[i];
            }

            Assert.IsNotNull(continueRow, "the main menu has no CONTINUE row");

            // The row says what the save layer says. The test asserts the two agree rather than
            // asserting a value, because the runner may or may not have a stored run.
            Assert.AreEqual(!SaveHost.HasStoredProgress, continueRow.Locked,
                            "CONTINUE is not locked in step with whether a run is stored");
            if (continueRow.Locked)
            {
                Assert.IsFalse(continueRow.Usable, "a locked CONTINUE row is still usable");
            }
        }

        [UnityTest]
        public IEnumerator The_settings_screen_lists_ten_categories_and_opens_each_one()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            root.System.GoTo(MenuScreenId.Settings);
            yield return Settle(root);

            var screen = (SettingsScreen)root.System.Current;
            SettingCategory[] categories = MenuSettingsPanel.Categories;
            Assert.AreEqual(10, categories.Length, "the settings screen does not list ten categories");
            Assert.AreEqual(10, SettingsScreen.CategoryCount);

            for (int i = 0; i < categories.Length; i++)
            {
                // Opening a category must not throw, and it must leave something selected: a screen
                // with nothing selected is a screen the keyboard and the gamepad cannot drive.
                screen.OpenCategory(categories[i]);
                yield return null;

                Assert.IsNotNull(screen.Rows, "the category panel is missing");
                Assert.IsNotNull(screen.ActiveNav, "the screen has no navigation to drive");
                Assert.IsNotNull(screen.ActiveNav.Current,
                               $"opening {categories[i]} left the navigation with nothing on it");
                Assert.IsTrue(MenuStrings.Has(MenuSettingsPanel.CategoryLabelKey(categories[i])),
                              $"no name for the {categories[i]} category");
                Assert.IsTrue(MenuStrings.Has(MenuSettingsPanel.CategoryNoteKey(categories[i])),
                              $"no note for the {categories[i]} category");
            }
        }

        [UnityTest]
        public IEnumerator Every_setting_row_has_a_label_and_a_destination()
        {
            SettingDefinition[] all = SettingsCatalog.All;
            Assert.GreaterOrEqual(all.Length, 20, "the catalogue has almost nothing in it");

            for (int i = 0; i < all.Length; i++)
            {
                SettingDefinition definition = all[i];
                Assert.IsTrue(MenuStrings.Has(definition.LabelKey),
                              $"{definition.Id} has no label in the table");
                if (!string.IsNullOrEmpty(definition.HelpKey))
                {
                    Assert.IsTrue(MenuStrings.Has(definition.HelpKey),
                                  $"{definition.Id} has no help text in the table");
                }

                Assert.IsFalse(string.IsNullOrEmpty(definition.Destination),
                               $"{definition.Id} does not say what reads it");

                // The row must round-trip through the settings service, or pressing it would change
                // a value that is not the one stored.
                GameSettings values = AetherSettings.Ensure().Values;
                float before = SettingsCatalog.Read(values, definition.Id);
                try
                {
                    float wanted = definition.Kind == SettingKind.Toggle
                        ? (before >= 0.5f ? 0f : 1f)
                        : definition.Sanitise(definition.Maximum);
                    SettingsCatalog.Write(values, definition.Id, wanted);

                    float after = SettingsCatalog.Read(values, definition.Id);
                    Assert.AreEqual(definition.Sanitise(wanted), after, 0.0001f,
                                    $"{definition.Id} does not store what it was given");
                }
                finally
                {
                    SettingsCatalog.Write(values, definition.Id, before);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator The_menu_survives_a_screen_of_rapid_input()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            // Hammering every direction and Submit for a second, with screen changes in the middle.
            // Nothing may throw, and only one screen may be up at the end.
            float until = Time.unscaledTime + 1f;
            int step = 0;
            while (Time.unscaledTime < until)
            {
                MenuNav.Move move = (MenuNav.Move)(step % 4);
                root.System.Move(move);
                root.System.Submit();

                if (step % 11 == 0)
                {
                    root.System.GoTo((MenuScreenId)(1 + (step % 6)));
                }

                step++;
                yield return null;
            }

            yield return Settle(root);
            Assert.AreEqual(1, VisibleScreens(root), "more than one screen is up after rapid input");
            Assert.IsTrue(root.System.Current.Visible, "no screen is up after rapid input");
        }

        [UnityTest]
        public IEnumerator The_canvas_scales_by_height_and_the_layout_fits_inside_it()
        {
            // A landscape window, the way the game will be played. A batch-mode editor may have no
            // window to resize, and the real box is measured below either way rather than assumed.
            try
            {
                Screen.SetResolution(1920, 1080, false);
            }
            catch (System.Exception)
            {
                // Nothing to do: the box the canvas actually got is what the assertions use.
            }

            yield return null;
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();
            yield return WaitForEntrance(root, 3f);

            // Height, not width: every phone the brief lists is wider than it is tall, and scaling by
            // height is what keeps the composition the same size on all of them. The reference is
            // 1920x1080 divided by the interface scale the player chose, so the numbers are compared
            // against what the menu itself says they should be rather than against a constant.
            CanvasScaler scaler = root.Ui.Scaler;
            float uiScale = MenuPreferences.ClampedUiScale;
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode,
                            "the canvas does not scale with the screen");
            Assert.AreEqual(1f, scaler.matchWidthOrHeight, 0.001f,
                            "the canvas does not match by height");
            Assert.AreEqual(1920f / uiScale, scaler.referenceResolution.x, 0.5f,
                            "the reference width does not follow the interface scale");
            Assert.AreEqual(1080f / uiScale, scaler.referenceResolution.y, 0.5f,
                            "the reference height does not follow the interface scale");

            // The safe area behaves: it is never larger than the screen, and when the device reports
            // none — which is what a runner does, and what a screen without cutouts does — the layout
            // fills the canvas exactly.
            Rect safe = Screen.safeArea;
            Assert.Greater(safe.width, 0f, "the runner reports an empty safe area");
            if (safe.width >= Screen.width - 0.5f && safe.height >= Screen.height - 0.5f)
            {
                Vector2 canvasBox = root.Ui.Root.rect.size;
                Vector2 safeBox = root.Ui.SafeRoot.rect.size;
                Assert.AreEqual(canvasBox.x, safeBox.x, 1.5f,
                                "the layout is inset although the screen has no unsafe area");
                Assert.AreEqual(canvasBox.y, safeBox.y, 1.5f,
                                "the layout is inset vertically although the screen has no unsafe area");
            }

            // Every row the player can press is inside the canvas at this resolution. This is the
            // check that catches a composition that fits 20:9 on paper and hangs off a 16:9 screen.
            var canvasCorners = new Vector3[4];
            root.Ui.Root.GetWorldCorners(canvasCorners);
            float left = canvasCorners[0].x, bottom = canvasCorners[0].y;
            float right = canvasCorners[2].x, top = canvasCorners[2].y;
            Assert.Greater(right - left, 0f, "the canvas has no area");

            MenuButton[] rows = root.System.Current.GetComponentsInChildren<MenuButton>(false);
            Assert.Greater(rows.Length, 0, "the main menu has no rows");

            if (right - left < top - bottom)
            {
                // The brief's screen shapes are all wider than they are tall. A portrait runner — an
                // editor window, not a phone — would be measuring a shape this menu does not claim to
                // support, and inventing a failure there would be dishonest rather than strict.
                Assert.Inconclusive($"the canvas is portrait on this machine "
                                    + $"({right - left:0} x {top - bottom:0} units); nothing about a "
                                    + "landscape composition can be measured on it");
            }

            var rowCorners = new Vector3[4];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Rect.GetWorldCorners(rowCorners);
                Assert.GreaterOrEqual(rowCorners[0].x, left - 1f, $"{rows[i].name} hangs off the left edge");
                Assert.LessOrEqual(rowCorners[2].x, right + 1f, $"{rows[i].name} hangs off the right edge");
                Assert.GreaterOrEqual(rowCorners[0].y, bottom - 1f, $"{rows[i].name} hangs off the bottom edge");
                Assert.LessOrEqual(rowCorners[2].y, top + 1f, $"{rows[i].name} hangs off the top edge");
            }
        }

        /// <summary>Waits for every block of the screen that is up to finish arriving.</summary>
        private static IEnumerator WaitForEntrance(MenuRoot root, float seconds)
        {
            float limit = Time.unscaledTime + seconds;
            while (Time.unscaledTime < limit)
            {
                MenuScreen screen = root.System.Current;
                if (screen == null) yield break;

                CanvasGroup[] groups = screen.GetComponentsInChildren<CanvasGroup>(false);
                bool settled = true;
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i].alpha < 0.999f)
                    {
                        settled = false;
                        break;
                    }
                }

                if (settled) break;
                yield return null;
            }

            yield return null;
        }

        private static int VisibleScreens(MenuRoot root)
        {
            var screens = root.GetComponentsInChildren<MenuScreen>(false);
            int visible = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].Visible) visible++;
            }

            return visible;
        }

        private static int ScreenCount(MenuRoot root, MenuScreenId id)
        {
            var screens = root.GetComponentsInChildren<MenuScreen>(true);
            int found = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].Id == id) found++;
            }

            return found;
        }
    }
}
