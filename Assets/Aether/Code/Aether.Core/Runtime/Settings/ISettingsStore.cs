namespace Aether.Core.Settings
{
    /// <summary>
    /// Where settings are kept between runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mirror of <c>ISaveStore</c>, and for the same reason: the service that owns the values
    /// must not know whether they end up in a JSON file, in <c>PlayerPrefs</c>, or in a platform
    /// cloud store. Nothing outside the store touches a path or a key.
    /// </para>
    /// <para>
    /// Implementations must tolerate junk. A hand-edited file, a file from a newer build, a
    /// half-written file after a crash — <see cref="TryLoad"/> returns false for all of them, and
    /// the game starts on defaults rather than refusing to start.
    /// </para>
    /// </remarks>
    public interface ISettingsStore
    {
        /// <summary>Loads settings. Returns false when there are none, or they cannot be read.</summary>
        bool TryLoad(out GameSettings settings);

        /// <summary>Writes settings without mutating them. False means the write failed.</summary>
        bool Save(GameSettings settings);

        /// <summary>Deletes stored settings, so the next load is defaults. False means failure.</summary>
        bool Clear();

        /// <summary>True when a stored copy exists right now.</summary>
        bool Exists { get; }

        /// <summary>Where the values live, for a diagnostics line or a support conversation.</summary>
        string Location { get; }
    }
}
