using System;
using System.Collections.Generic;

namespace Aether.Core.Settings
{
    /// <summary>
    /// Owns the player's settings: the values, the rules, and when they are written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is exactly one of these in a running game, and every write goes through it.</b>
    /// Nothing else in the project touches a setting, a settings file or a settings key. That rule
    /// is what makes "reset this category", "are these defaults" and "write before the app
    /// suspends" answerable at all; scattered <c>PlayerPrefs</c> calls make all three impossible and
    /// are the reason the interface is shaped this way.
    /// </para>
    /// <para>
    /// <b>Writes are coalesced, not immediate.</b> A slider dragged across the screen produces a
    /// change event per frame and zero writes: <see cref="Set"/> marks the values dirty and
    /// <see cref="Flush"/> is what touches the disk. The interface calls <see cref="Flush"/> when a
    /// drag ends, when a screen closes and when the app is about to lose focus — the three moments a
    /// write is both meaningful and cheap. Writing on every frame of a drag is a real cause of
    /// hitching on a phone, and a settings file is not worth a dropped frame.
    /// </para>
    /// <para>
    /// <b>Reads never allocate and never touch the store.</b> The whole object graph is in memory;
    /// gameplay reads a float.
    /// </para>
    /// </remarks>
    public sealed class SettingsService
    {
        private readonly List<string> _dirtyIds = new List<string>();
        private bool _loaded;
        private bool _dirty;

        public SettingsService() : this(new GameSettings())
        {
        }

        public SettingsService(GameSettings settings)
        {
            Values = settings ?? new GameSettings();
        }

        /// <summary>The live values. Read them directly; write them through <see cref="Set"/>.</summary>
        public GameSettings Values { get; }

        /// <summary>Where these are persisted. Null means "this run only".</summary>
        public ISettingsStore Store { get; set; }

        /// <summary>
        /// Raised after any value changes, with the id that changed, or an empty string for a bulk
        /// change such as a category reset.
        /// </summary>
        /// <remarks>
        /// Subscribers apply the change to whatever they own. The event is deliberately not
        /// per-setting: there are a couple of dozen settings and a handful of listeners, and a
        /// listener that is told to re-read the five values it cares about is simpler — and less
        /// likely to be wired up wrongly — than five events.
        /// </remarks>
        public event Action<string> Changed;

        /// <summary>True when values have been changed since the last write.</summary>
        public bool IsDirty => _dirty;

        /// <summary>True when a stored copy exists.</summary>
        public bool HasStoredCopy => Store != null && Store.Exists;

        /// <summary>Where the values are kept, or a plain statement that they are not.</summary>
        public string Location => Store != null ? Store.Location : "this session only";

        /// <summary>
        /// Reads the stored values, or moves to defaults when there are none.
        /// </summary>
        /// <remarks>
        /// Safe to call more than once; the second call does nothing, so a scene that boots twice
        /// cannot reload settings out from under a screen that is already open.
        /// </remarks>
        public void Load()
        {
            if (_loaded) return;
            _loaded = true;

            if (Store != null && Store.TryLoad(out GameSettings stored) && stored != null)
            {
                Values.CopyFrom(stored);
            }
            else
            {
                Values.Reset();
            }

            // A file is untrusted input. Everything that reaches gameplay has been checked against
            // the range its own definition declares before a single system reads it.
            Values.Clamp();
            Raise(null);
        }

        /// <summary>The current value of a setting.</summary>
        public float Get(string id)
        {
            return SettingsCatalog.Read(Values, id);
        }

        /// <summary>
        /// Stores a value and tells the listeners.
        /// </summary>
        /// <remarks>
        /// Setting a value to what it already is does nothing at all: no event, no dirty flag. That
        /// is not just an optimisation — a slider that is nudged back to where it started must not
        /// mark the settings dirty and cause a write on the next flush.
        /// </remarks>
        public void Set(string id, float value)
        {
            SettingDefinition definition = SettingsCatalog.Find(id);
            if (definition == null) return;

            float clean = definition.Sanitise(value);
            if (Math.Abs(Get(id) - clean) < 0.0001f) return;

            SettingsCatalog.Write(Values, id, clean);
            Values.Clamp();

            if (!_dirtyIds.Contains(id)) _dirtyIds.Add(id);
            _dirty = true;
            Raise(id);
        }

        /// <summary>One category back to its defaults.</summary>
        public void ResetCategory(SettingCategory category)
        {
            List<SettingDefinition> rows = SettingsCatalog.InCategory(category);
            bool touched = false;

            for (int i = 0; i < rows.Count; i++)
            {
                SettingDefinition row = rows[i];
                var defaults = new GameSettings();
                defaults.Reset();
                float wanted = SettingsCatalog.Read(defaults, row.Id);
                if (Math.Abs(Get(row.Id) - wanted) < 0.0001f) continue;

                SettingsCatalog.Write(Values, row.Id, wanted);
                touched = true;
            }

            if (!touched) return;

            Values.Clamp();
            _dirty = true;
            Raise(null);
        }

        /// <summary>Every setting back to the value a fresh install has.</summary>
        public void ResetAll()
        {
            Values.Reset();
            Values.Clamp();
            _dirty = true;
            Raise(null);
        }

        /// <summary>True when this setting still holds its default value.</summary>
        public bool IsDefault(string id)
        {
            var defaults = new GameSettings();
            defaults.Reset();
            return Math.Abs(Get(id) - SettingsCatalog.Read(defaults, id)) < 0.0001f;
        }

        /// <summary>
        /// Writes the values if anything changed since the last write.
        /// </summary>
        /// <remarks>
        /// Never throws: a settings file that cannot be written must not take the game down. A
        /// failed write leaves the dirty flag set, so the next flush tries again.
        /// </remarks>
        public bool Flush()
        {
            if (!_dirty) return true;

            // No store means this service is intentionally session-only. There is nothing to write,
            // so a lifecycle flush must not be reported as a save failure. Keep the dirty state until
            // a store is attached, in case a caller installs persistence later in the same session.
            if (Store == null) return true;

            try
            {
                if (!Store.Save(Values)) return false;

                _dirty = false;
                _dirtyIds.Clear();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Deletes the stored copy and returns to defaults.</summary>
        public bool ClearStored()
        {
            if (Store != null)
            {
                try
                {
                    if (!Store.Clear()) return false;
                }
                catch (Exception)
                {
                    // Same rule as Flush: storage problems are reported by the store, not thrown here.
                    return false;
                }
            }

            Values.Reset();
            Values.Clamp();
            _dirty = false;
            _dirtyIds.Clear();
            Raise(null);
            return true;
        }

        private void Raise(string id)
        {
            Changed?.Invoke(id ?? string.Empty);
        }
    }
}
