using System.Collections.Generic;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// How the menu decides what is selected, and where the gamepad sticks and the arrow keys move it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> The project runs on the Input System alone, and the Input System's
    /// user interface module sends navigation events without any spatial knowledge: with nothing
    /// selecting an object, a stick does nothing, and with one selection it can only ever move by the
    /// <c>Navigation</c> links an editor's automatic mode would have solved. Those links are not
    /// authored here, and the automatic solve is an edit-time feature that does not exist at
    /// runtime.
    /// </para>
    /// <para>
    /// So selection is geometry. A screen hands this class its rows and the column their labels
    /// start at; up and down walk the active entries in reading order, left and right cross into a
    /// neighbouring column of controls on the same row. That gives keyboard, gamepad and TV-remote
    /// navigation for free and — more importantly — gives the settings screen a rule that works for
    /// a row whose label sits on the left and whose control sits on the right, on any screen shape.
    /// </para>
    /// <para>
    /// It is deliberately a plain class over a list, not a component, so it allocates nothing per
    /// frame and each screen owns exactly one. Move requests arrive as events from the input module,
    /// not from an <c>Update</c>.
    /// </para>
    /// <para>
    /// <b>It also tells the event system, and that is not decoration.</b> The Input System's user
    /// interface module routes <i>Submit</i> to whatever the event system has selected, and this
    /// menu never selects through the event system's own navigation — so without the line in
    /// <see cref="SelectIndex"/> a gamepad's south button and a keyboard's Enter would activate
    /// nothing at all, while taps kept working. Selecting through both keeps the two ways of driving
    /// the menu on one row.
    /// </para>
    /// </remarks>
    public sealed class MenuNav
    {
        /// <summary>A direction to move in, independent of any input package's enums.</summary>
        public enum Move
        {
            Up,
            Down,
            Left,
            Right,
        }

        private readonly List<Selectable> _entries = new List<Selectable>(16);
        private readonly List<Vector2> _points = new List<Vector2>(16);
        private int _index = -1;

        /// <summary>Invoked when the selection lands on a different entry. May be null.</summary>
        public System.Action<Selectable> SelectionChanged;

        /// <summary>Invoked when an entry is activated by the source's submit event. May be null.</summary>
        public System.Action<Selectable> Activated;

        /// <summary>How many entries the screen has offered so far.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>Whether anything at all is selectable. A screen with no rows must not eat input.</summary>
        public bool HasEntries
        {
            get { return _entries.Count > 0; }
        }

        /// <summary>The entry the selection is on, or null.</summary>
        public Selectable Current
        {
            get { return _index >= 0 && _index < _entries.Count ? _entries[_index] : null; }
        }

        /// <summary>
        /// Offers an entry to the navigation, in reading order: top to bottom, and left to right
        /// within a row. Remembers the position it was given, so callers add in the order they draw.
        /// </summary>
        /// <param name="selectable">The entry. A null, disabled or un-interactable selectable is skipped.</param>
        /// <param name="column">Which column of a row it belongs to: 0 is the panel's own column.</param>
        /// <param name="row">Which row of the panel it belongs to.</param>
        public void Add(Selectable selectable, int column = 0, int row = -1)
        {
            if (selectable == null) return;
            if (row < 0) row = _entries.Count;

            _entries.Add(selectable);
            _points.Add(new Vector2(column, row));

            // Everything the menu builds points at this class; an explicit link would be stale the
            // moment a row is rebuilt, which the settings screen does on every change.
            var navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>Forgets every entry. Used when a panel is rebuilt rather than merely shown.</summary>
        public void Clear()
        {
            _entries.Clear();
            _points.Clear();
            _index = -1;
        }

        /// <summary>Puts the selection on an entry, or on the first one when given null.</summary>
        public void Select(Selectable selectable, bool notify = true)
        {
            int index = selectable == null ? FirstIndex() : _entries.IndexOf(selectable);

            if (notify) SelectIndex(index);
            else _index = index;
        }

        /// <summary>Selects the first entry that can be used.</summary>
        public void SelectFirst(bool notify = true)
        {
            if (notify) SelectIndex(FirstIndex());
            else _index = FirstIndex();
        }

        /// <summary>Moves the selection one step in a direction, wrapping at the ends.</summary>
        public void Step(Move direction)
        {
            if (_entries.Count == 0) return;

            Selectable current = Current;
            if (current == null)
            {
                SelectFirst();
                return;
            }

            Selectable next = null;
            // In RTL locales, physical right means the previous reading-direction column and
            // physical left means the next one. Up/down remains unchanged.
            if (LanguageService.IsRightToLeft)
            {
                if (direction == Move.Right) direction = Move.Left;
                else if (direction == Move.Left) direction = Move.Right;
            }

            switch (direction)
            {
                case Move.Down:
                    next = Walk(current, 0, 1);
                    break;
                case Move.Up:
                    next = Walk(current, 0, -1);
                    break;
                case Move.Right:
                    next = Walk(current, 1, 0);
                    break;
                case Move.Left:
                    next = Walk(current, -1, 0);
                    break;
            }

            if (next != null) Select(next);
        }

        /// <summary>Activates an entry, exactly as a tap on it would.</summary>
        public void Activate(Selectable selectable)
        {
            if (selectable == null || !IsUsable(selectable)) return;

            _index = _entries.IndexOf(selectable);
            System.Action<Selectable> handler = Activated;
            if (handler != null) handler(selectable);
        }

        /// <summary>Activates the current entry, if there is one.</summary>
        public void Submit()
        {
            Activate(Current);
        }

        /// <summary>Drops entries that have gone away, and fixes the cursor up afterwards.</summary>
        /// <remarks>
        /// Rows are pooled and re-labelled rather than recreated, so the entries themselves are
        /// stable; this is a guard for the case where a panel is genuinely destroyed under the
        /// selection, which would otherwise leave the navigation pointing at a destroyed object.
        /// </remarks>
        public void Prune()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i] != null && IsUsable(_entries[i])) continue;
                _entries.RemoveAt(i);
                _points.RemoveAt(i);
                if (_index >= i) _index--;
            }

            if (_index >= _entries.Count) _index = _entries.Count - 1;
        }

        // -- internals -------------------------------------------------------------------------

        private static bool IsUsable(Selectable selectable)
        {
            return selectable != null
                   && selectable.isActiveAndEnabled
                   && selectable.interactable
                   && selectable.gameObject.activeInHierarchy;
        }

        private int FirstIndex()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (IsUsable(_entries[i])) return i;
            }

            return -1;
        }

        private void SelectIndex(int index)
        {
            if (index == _index)
            {
                // The selection is where it already was, so nothing changed and no handler runs —
                // but the event system may have lost it. Opening a dialog takes the selection onto
                // the dialog's own rows, and closing it switches those objects off, which clears the
                // event system's idea of what is selected. Re-asserting it here is what keeps a
                // gamepad able to press the row the screen is standing on afterwards.
                TellEventSystem(Current);
                return;
            }

            _index = index;
            Selectable current = Current;
            TellEventSystem(current);

            System.Action<Selectable> handler = SelectionChanged;
            if (handler != null) handler(current);
        }

        /// <summary>
        /// Hands the selection to the event system, so Submit reaches the same row.
        /// </summary>
        /// <remarks>
        /// Guarded against setting the object that is already set: the call is what fires
        /// <c>OnSelect</c> and <c>OnDeselect</c>, and firing those on every step for a row that did
        /// not change would redraw the whole screen for nothing.
        /// </remarks>
        private static void TellEventSystem(Selectable selectable)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            GameObject wanted = selectable == null ? null : selectable.gameObject;
            if (events.currentSelectedGameObject == wanted) return;

            events.SetSelectedGameObject(wanted);
        }

        /// <summary>
        /// Finds the next usable entry from a point, in a direction.
        /// </summary>
        /// <remarks>
        /// Vertical moves walk the flat list, because reading order already is the order a list
        /// should be read in and a row's several controls are adjacent in it. Horizontal moves look
        /// for the nearest entry with a different column number on the same row, which is what makes
        /// left and right step between a row's label side and its control side.
        /// </remarks>
        private Selectable Walk(Selectable from, int columnStep, int rowStep)
        {
            int origin = _entries.IndexOf(from);
            if (origin < 0) return null;

            Vector2 point = _points[origin];

            if (rowStep != 0)
            {
                for (int offset = 1; offset <= _entries.Count; offset++)
                {
                    int index = Wrap(origin + (offset * rowStep));
                    Vector2 candidate = _points[index];

                    // Only entries in a row *past* the current one count; that is how a row with
                    // several controls is stepped over as a unit and never gets stuck on itself.
                    bool beyond = rowStep > 0 ? candidate.y > point.y : candidate.y < point.y;
                    if (!beyond) continue;

                    if (IsUsable(_entries[index])) return _entries[index];
                }

                return null;
            }

            // Horizontal: nearest usable entry in the same row band, at any other column, on the
            // side asked for. Rows are compared by their recorded row number, not by pixels, so the
            // rule holds whether the screen is a phone or a desktop window.
            Selectable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == origin || !IsUsable(_entries[i])) continue;
                if (Mathf.Abs(_points[i].y - point.y) > 0.5f) continue;

                float delta = (_points[i].x - point.x) * columnStep;
                if (delta <= 0f) continue;

                if (delta < bestDistance)
                {
                    bestDistance = delta;
                    best = _entries[i];
                }
            }

            return best;
        }

        private int Wrap(int index)
        {
            int count = _entries.Count;
            if (count == 0) return 0;
            return ((index % count) + count) % count;
        }
    }
}
