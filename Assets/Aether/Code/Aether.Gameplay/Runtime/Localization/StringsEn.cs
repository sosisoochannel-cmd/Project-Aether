using Aether.Gameplay.Menus;

namespace Aether.Gameplay.Localization
{
    /// <summary>
    /// The English table, as the localization service sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// English is the odd one out, and deliberately so. Every other language is written as a sheet in
    /// <c>tools/localization</c> and generated into a C# table; English is written directly in
    /// <c>MenuStrings.cs</c>, because that is the file people edit when they add a string and the file
    /// the verifier reads when it counts what a translation is missing.
    /// </para>
    /// <para>
    /// This wrapper is the only seam between the two, and it copies nothing: it hands the one English
    /// dictionary to the service under the name the service expects. A second copy of the English
    /// table would be a second thing to keep in step, and something that exists twice will eventually
    /// disagree with itself.
    /// </para>
    /// <para>
    /// <c>tools/verify/localization.py</c> checks that this file still names
    /// <c>MenuStrings.Raw</c>, so the wiring cannot be quietly undone.
    /// </para>
    /// </remarks>
    internal static class StringsEn
    {
        internal static readonly Strings Table = Strings.From(MenuStrings.Raw);
    }
}
