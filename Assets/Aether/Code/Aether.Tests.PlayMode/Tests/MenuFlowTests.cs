using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aether.Core.Settings;
using Aether.Gameplay.Flow;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Menus.Screens;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Storage;
using Aether.Gameplay.Sound;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
    /// It also isolates both persistence layers: settings use an in-memory store and save slots use
    /// a unique temporary folder that is removed after each test. The menu cannot migrate, overwrite
    /// or clean up a player's real settings or save files.
    /// </para>
    /// </remarks>
    public sealed class MenuFlowTests
    {
        private readonly List<GameObject> _built = new List<GameObject>();
        private string _saveFolder;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLaunch.Clear();
            _saveFolder = "Aether-MenuFlow-Test-" + Guid.NewGuid().ToString("N");
            SaveHost.ResetForTests(Aether.Gameplay.GameSession.Instance, _saveFolder);

            // The menu tests use isolated settings and save storage. A menu should never migrate,
            // overwrite or even need to inspect a player's real save while a test is running.
            AetherSettings.ResetForTests(new MemorySettingsStore());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _built.Count; i++)
            {
                if (_built[i] != null) UnityEngine.Object.Destroy(_built[i]);
            }

            _built.Clear();
            GameLaunch.Clear();

            // The transition is built to outlive a scene load, because in the game it must — and that
            // is exactly why a test may not leave one behind: a full-screen canvas in front of the
            // next test's camera is not a thing any test should have to know about. It is a
            // singleton that rebuilds itself on demand, so taking it away here costs nothing.
            MenuTransition[] transitions = UnityEngine.Object.FindObjectsByType<MenuTransition>(FindObjectsSortMode.None);
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null) UnityEngine.Object.Destroy(transitions[i].gameObject);
            }

            yield return null;

            // Tear down anything a menu action created and remove only this test's unique save folder.
            SaveHost.ResetForTests(Aether.Gameplay.GameSession.Instance, _saveFolder);
            for (int slot = 1; slot <= SaveSlots.Count; slot++) SaveSlots.For(slot).Clear();
            string saveRoot = Path.GetDirectoryName(SaveSlots.For(1).Location);
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            SaveHost.ResetForTests(null);
            AetherSettings.ResetForTests();
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
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Assert.IsTrue(canvases.Length >= 1, "no canvas was created");
            bool found = false;
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].gameObject == root.Ui.GameObject) found = true;
            }

            Assert.IsTrue(found, "the menu's canvas is not in the scene");
        }

        [UnityTest]
        public IEnumerator A_pause_settings_request_opens_settings_and_back_returns_to_main_menu()
        {
            GameLaunch.RequestOpenSettings();
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            Assert.AreEqual(MenuScreenId.Settings, root.System.CurrentId,
                            "the menu ignored the settings destination handed over from pause");
            Assert.AreEqual(1, VisibleScreens(root));

            Assert.IsTrue(root.System.Back(), "the settings screen did not handle Back");
            yield return Settle(root);
            Assert.AreEqual(MenuScreenId.MainMenu, root.System.CurrentId,
                            "Back from settings did not return to the main menu behind it");
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
                MenuScreenId.Achievements, MenuScreenId.SaveSlots, MenuScreenId.Settings,
                MenuScreenId.Credits,
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

            // This fixture owns a fresh isolated save folder, so Continue should be locked exactly
            // when its save layer reports that there is no stored run.
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
        public IEnumerator Settings_rows_are_inside_their_scroll_clip_and_navigation_reveals_them()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();
            root.System.GoTo(MenuScreenId.Settings);
            yield return Settle(root);

            SettingsScreen screen = (SettingsScreen)root.System.Current;
            ScrollRect categoryScroll = screen.Categories.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(categoryScroll, "the category list has no scroll view");
            MenuButton[] categories = screen.Categories.GetComponentsInChildren<MenuButton>(true);
            Assert.GreaterOrEqual(categories.Length, SettingCategoryCount,
                                  "the settings categories were not built");
            for (int i = 0; i < categories.Length; i++)
            {
                Assert.IsTrue(categories[i].transform.IsChildOf(categoryScroll.content),
                              "a settings category escaped the clipped scroll content");
            }

            screen.Layout(800f, 300f);
            screen.OpenCategory(SettingCategory.Audio);
            yield return null;
            Canvas.ForceUpdateCanvases();

            ScrollRect rowScroll = screen.Rows.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(rowScroll, "the settings rows have no scroll view");
            MenuButton[] rows = screen.Rows.GetComponentsInChildren<MenuButton>(true);
            Assert.Greater(rows.Length, 0, "the settings catalogue produced no rows");
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.IsTrue(rows[i].transform.IsChildOf(rowScroll.content),
                              "a settings row escaped the clipped scroll content");
            }

            Assert.Greater(rowScroll.content.rect.height, rowScroll.viewport.rect.height,
                           "the short test viewport should require scrolling");
            for (int i = 1; i < screen.ActiveNav.Count - 1; i++)
                screen.ActiveNav.Step(MenuNav.Move.Down);

            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.IsNotNull(screen.ActiveNav.Current, "navigation lost its selected settings row");
            Assert.Greater(rowScroll.content.anchoredPosition.y, 0f,
                           "keyboard/gamepad selection did not scroll the final settings row into view");
        }

        private const int SettingCategoryCount = 10;

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
        public IEnumerator The_menu_survives_a_screen_of_rapid_navigation()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            // Hammer every direction for a second, with screen changes in the middle. This stresses
            // navigation and transitions without submitting a real save, quit or scene-load action.
            float until = Time.unscaledTime + 1f;
            int step = 0;
            while (Time.unscaledTime < until)
            {
                MenuNav.Move move = (MenuNav.Move)(step % 4);
                root.System.Move(move);

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
        public IEnumerator The_canvas_scales_by_height_and_scroll_navigation_keeps_rows_reachable()
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

            // The viewport is what must fit the canvas. Content may be taller on a 4:3 or high-scale
            // screen, but it must remain clipped and every action must stay reachable through it.
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

            // What the box actually was, carried into the message of any failure below. A number that
            // says a row "hangs off the bottom" without the box it hung off cannot be acted on: the
            // runner's shape is not one of the shapes the brief names, and the first version of this
            // assertion said only which row failed. The line is logged as well as embedded, so a run
            // that fails here explains itself in the test log without a second push.
            string where = $"canvas {right - left:0.#}x{top - bottom:0.#} units, "
                         + $"content box {root.Ui.ContentRoot.rect.width:0.#}x{root.Ui.ContentRoot.rect.height:0.#}, "
                         + $"screen rect {root.System.Current.Rect.rect.width:0.#}x{root.System.Current.Rect.rect.height:0.#}, "
                         + $"screen {Screen.width}x{Screen.height}px, "
                         + $"safe area {safe.x:0},{safe.y:0} {safe.width:0}x{safe.height:0}, "
                         + $"interface scale {uiScale:0.###}, "
                         + $"reference {scaler.referenceResolution.x:0.#}x{scaler.referenceResolution.y:0.#}, "
                         + $"{rows.Length} rows";
            Debug.LogWarning($"[menu-layout] {where}");

            ScrollRect scroll = root.System.Current.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(scroll, "the main menu has no scroll view for short or narrow screens");

            var viewportCorners = new Vector3[4];
            scroll.viewport.GetWorldCorners(viewportCorners);
            Assert.GreaterOrEqual(viewportCorners[0].x, left - 1f,
                                  $"the menu viewport hangs off the left edge ({where})");
            Assert.LessOrEqual(viewportCorners[2].x, right + 1f,
                               $"the menu viewport hangs off the right edge ({where})");
            Assert.GreaterOrEqual(viewportCorners[0].y, bottom - 1f,
                                  $"the menu viewport hangs off the bottom edge ({where})");
            Assert.LessOrEqual(viewportCorners[2].y, top + 1f,
                               $"the menu viewport hangs off the top edge ({where})");

            for (int i = 0; i < rows.Length; i++)
            {
                Assert.IsTrue(rows[i].transform.IsChildOf(scroll.content),
                              $"row {i} '{rows[i].name}' is not inside the clipped scroll content ({where})");
            }

            MenuButton last = rows[rows.Length - 1];
            MenuNav nav = root.System.Current.ActiveNav;
            nav.Select(last);
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.AreSame(last, nav.Current, "the last main-menu action was not selectable");
            Bounds selected = RectTransformUtility.CalculateRelativeRectTransformBounds(
                scroll.viewport, last.Rect);
            Rect viewport = scroll.viewport.rect;
            Assert.GreaterOrEqual(selected.min.y, viewport.yMin - 1f,
                                  $"scroll selection left the last row below its viewport ({where})");
            Assert.LessOrEqual(selected.max.y, viewport.yMax + 1f,
                               $"scroll selection left the last row above its viewport ({where})");
        }

        [UnityTest]
        public IEnumerator The_selection_never_lands_on_a_row_that_cannot_be_used()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();
            yield return Settle(root);

            // A walk down the main menu. Every row the navigation stops on must be a row that can
            // actually be pressed: the locked destinations are skipped, not stepped over on the way
            // to somewhere else, and a read-only fact is never a stop. This is the property that
            // makes a locked row an honest thing to show rather than a trap.
            int visited = 0;
            for (int i = 0; i < 14; i++)
            {
                MenuNav nav = root.System.Current.ActiveNav;
                Assert.IsNotNull(nav, "the screen has no navigation");
                Assert.IsNotNull(nav.Current, "the navigation has nothing selected");

                MenuButton row = nav.Current as MenuButton;
                Assert.IsNotNull(row, "the selection is not on a row");
                Assert.IsTrue(row.Usable, $"the selection landed on '{row.name}', which cannot be used");

                visited++;
                root.System.Move(MenuNav.Move.Down);
                yield return null;
            }

            Assert.Greater(visited, 10, "the walk stopped early");
        }

        [UnityTest]
        public IEnumerator Every_audio_bus_is_one_gain_path_and_master_scales_all_of_them()
        {
            // The brief asks for MASTER / MUSIC / SFX / UI / VOICE / AMBIENCE with smooth fades and
            // a future mixer behind them. What that means in code is one formula, and this is it:
            // a bus's gain is its own level times master, master's own gain is its level, and
            // silencing master silences everything. Every cue in the menu reads through here.
            Assert.AreEqual(6, AudioBuses.All.Length, "the audio layer does not list six buses");
            Assert.AreEqual(AudioBusId.Master, AudioBuses.All[0],
                            "master is not the first bus, so the settings order is not the enum's");

            // `var`, deliberately: AudioSettings exists in both namespaces this file imports -
            // Aether.Core.Settings and UnityEngine - so naming the type is ambiguous, and the
            // editor said so. The value is the settings group either way.
            var audio = AetherSettings.Ensure().Values.Audio;
            float master = audio.Master;
            float music = audio.Music;
            try
            {
                audio.Master = 0.5f;
                audio.Music = 0.4f;

                Assert.AreEqual(0.5f, AudioBuses.Gain(AudioBusId.Master), 0.0001f,
                                "master's gain is not its own level");
                Assert.AreEqual(0.2f, AudioBuses.Gain(AudioBusId.Music), 0.0001f,
                                "a bus's gain is not its level times master");
                Assert.IsFalse(AudioBuses.IsSilent(AudioBusId.Music),
                               "a bus at a fifth of full scale reports itself silent");

                audio.Master = 0f;
                for (int i = 0; i < AudioBuses.All.Length; i++)
                {
                    Assert.IsTrue(AudioBuses.IsSilent(AudioBuses.All[i]),
                                  $"master at zero left {AudioBuses.All[i]} audible");
                }

                // The readout a settings row shows is the same number, as a percentage.
                audio.Master = 1f;
                audio.Music = 0.5f;
                Assert.AreEqual("50%", AudioBuses.Describe(AudioBusId.Music),
                                "the bus readout does not follow the gain");
            }
            finally
            {
                audio.Master = master;
                audio.Music = music;
            }

            yield return null;
        }


        [UnityTest]
        public IEnumerator The_new_game_row_opens_the_slot_list()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();
            var menu = (MainMenuScreen)root.System.Current;

            // By label, not by index: the check is about the row the player reads.
            string wanted = MenuUi.Track(MenuStrings.Get("menu.newGame"),
                                         MenuTheme.Metrics.PrimaryTracking);
            MenuButton newGame = null;
            IList<MenuButton> rows = menu.Play.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Label.text == wanted) newGame = rows[i];
            }

            Assert.IsNotNull(newGame, "the main menu has no NEW GAME row");

            newGame.Activate();
            yield return Settle(root);

            // NEW GAME asks where the run should go rather than choosing for the player, and the
            // screen it asks on is the one that names the run a replacement would destroy.
            Assert.AreEqual(MenuScreenId.SaveSlots, root.System.CurrentId,
                            "NEW GAME did not open the slot list");
            Assert.IsTrue(root.System.Current is SaveSlotScreen,
                          "the save-slots id did not resolve to the save-slot screen");
        }

        [UnityTest]
        public IEnumerator The_slot_screen_shows_a_row_for_every_slot()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            root.System.GoTo(MenuScreenId.SaveSlots);
            yield return Settle(root);

            var screen = (SaveSlotScreen)root.System.Current;
            Assert.AreEqual(SaveSlots.Count, screen.PrimaryRows.Length,
                            "the screen does not show one row per slot");
            Assert.AreEqual(SaveSlots.Count, screen.NewRunRows.Length,
                            "the screen does not offer an explicit new-run action in every slot");
            Assert.AreEqual(SaveSlots.Count, screen.DeleteRows.Length,
                            "the screen does not offer a way to free every slot");

            for (int i = 0; i < screen.PrimaryRows.Length; i++)
            {
                MenuButton row = screen.PrimaryRows[i];
                SaveSlotInfo info = SaveSlots.Describe(i + 1);

                // The label is a sentence from the string table, whether the slot is empty, stored or
                // damaged — never the key it was looked up by, which is what a missing string shows.
                Assert.IsFalse(string.IsNullOrEmpty(row.Label.text),
                               $"slot {i + 1} has no label at all");
                Assert.IsFalse(row.Label.text.StartsWith("slots."),
                               $"slot {i + 1} shows the key '{row.Label.text}' instead of a label");

                // A slot whose file cannot be read is the one case the row refuses to open, and the
                // test asserts the row and the store agree rather than asserting either alone.
                bool shouldBeLocked = info.Exists && !info.Playable;
                Assert.AreEqual(shouldBeLocked, row.Locked,
                                $"slot {i + 1} is {(shouldBeLocked ? "locked" : "open")} while the "
                                + "store says the opposite");
                if (row.Locked) Assert.IsFalse(row.Usable, "a locked slot row is still usable");
            }
        }

        [UnityTest]
        public IEnumerator Every_catalogue_screen_draws_the_catalogue_behind_it()
        {
            yield return BuildMenu();
            MenuRoot root = CurrentMenu();

            root.System.GoTo(MenuScreenId.Characters);
            yield return Settle(root);

            var characters = (CharactersScreen)root.System.Current;
            Assert.AreEqual(CharacterCatalog.All.Length, characters.Rows.Length,
                            "the characters screen does not have a row per character");
            for (int i = 0; i < characters.Rows.Length; i++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(characters.Rows[i].Label.text),
                               $"character {i} has no name");
            }

            root.System.GoTo(MenuScreenId.Achievements);
            yield return Settle(root);

            var achievements = (AchievementsScreen)root.System.Current;
            Assert.AreEqual(AchievementCatalog.Count, achievements.Rows.Length,
                            "the achievements screen does not have a row per achievement");
            for (int i = 0; i < achievements.Rows.Length; i++)
            {
                MenuButton row = achievements.Rows[i];
                Assert.IsFalse(string.IsNullOrEmpty(row.Label.text),
                               $"achievement {i} has no name");

                // Locked or earned, never blank: a row that says nothing is a row a player cannot
                // tell from a broken screen.
                Assert.IsFalse(string.IsNullOrEmpty(row.Meta.text),
                               $"achievement {i} says nothing about its state");
            }

            root.System.GoTo(MenuScreenId.Collection);
            yield return Settle(root);

            var collection = (CollectionScreen)root.System.Current;
            Assert.AreEqual(CollectionCatalog.All.Length, collection.CategoryCount,
                            "the collection screen does not list every category");
            for (int c = 0; c < collection.CategoryCount; c++)
            {
                MenuButton[] rows = collection.RowsOf(c);
                Assert.AreEqual(CollectionCatalog.All[c].Entries.Length, rows.Length,
                                $"collection category {c} does not show every entry");
            }
        }

        private sealed class MemorySettingsStore : ISettingsStore
        {
            public GameSettings Stored;

            public bool TryLoad(out GameSettings settings)
            {
                settings = Stored;
                return settings != null;
            }

            public bool Save(GameSettings settings)
            {
                if (settings == null) return false;
                Stored = new GameSettings();
                Stored.CopyFrom(settings);
                return true;
            }

            public bool Clear()
            {
                Stored = null;
                return true;
            }

            public bool Exists => Stored != null;
            public string Location => "isolated menu test store";
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