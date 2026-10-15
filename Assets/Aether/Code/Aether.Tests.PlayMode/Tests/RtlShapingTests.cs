using System.Collections;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Guards the hand-written Persian shaping the shipped interface depends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menus use Unity's legacy <c>Text</c> component, which runs neither the Unicode
    /// bidirectional algorithm nor the font's own OpenType shaping, so <see cref="RtlText"/>
    /// substitutes Arabic presentation forms itself. Nothing else stands between a correctly
    /// translated Persian sheet and text that is wrong in a way that still looks like writing.
    /// </para>
    /// <para>
    /// The expectations below are presentation-form code points rather than pixels, because the
    /// failure mode being guarded is a wrong choice of form: a lam that keeps its connector next to
    /// an alef reads as plausible text until someone who reads Persian looks at it.
    /// </para>
    /// <para>
    /// Persian is already the shipped default language, so these tests only need to state which
    /// language they are inspecting rather than change any setting the game keeps.
    /// </para>
    /// </remarks>
    public sealed class RtlShapingTests
    {
        /// <summary>Lam-alef: the pair written as one glyph, never as two letters.</summary>
        private const char LamAlefIsolated = '\uFEFB';
        private const char LamAlefFinal = '\uFEFC';

        /// <summary>Lam-alef-madda, lam-alef-hamza above, lam-alef-hamza below.</summary>
        private const char LamAlefMaddaIsolated = '\uFEF5';
        private const char LamAlefMaddaFinal = '\uFEF6';
        private const char LamAlefHamzaAboveIsolated = '\uFEF7';
        private const char LamAlefHamzaAboveFinal = '\uFEF8';
        private const char LamAlefHamzaBelowIsolated = '\uFEF9';
        private const char LamAlefHamzaBelowFinal = '\uFEFA';

        private const char ZeroWidthNonJoiner = '\u200C';

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LanguageService.Reload();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LanguageService.Set("en");
            yield return null;
        }

        /// <summary>
        /// Lam followed by alef is one ligature glyph, in the form the surrounding letters require.
        /// </summary>
        [Test]
        public void LamAlefIsWrittenAsOneLigature()
        {
            // The two words the shipped Persian copy is built around: "سلامتی" ("safety", from
            // pause.restart.body) and "علامتی" ("a marker", from character.precursor.body).
            Assert.AreEqual("\uFBFD\uFE98\uFEE3" + LamAlefFinal + "\uFEB3", RtlText.Visualize("سلامتی"),
                "سلامتی must be drawn with the joined lam-alef ligature, not a lam next to an alef.");
            Assert.AreEqual("\uFBFD\uFE98\uFEE3" + LamAlefFinal + "\uFECB", RtlText.Visualize("علامتی"),
                "علامتی must be drawn with the joined lam-alef ligature, not a lam next to an alef.");

            // A lam with nothing to its right that can join takes the isolated ligature instead.
            Assert.AreEqual(LamAlefIsolated, RtlText.Visualize("لا"),
                "A lam-alef pair with no joining letter before it must use the isolated ligature.");
            Assert.AreEqual(LamAlefFinal + "\uFE91", RtlText.Visualize("بلا"),
                "A lam-alef pair reached from a joining letter must use the final ligature.");

            // The ligature replaces both letters, so no separate lam or alef form may survive beside it.
            string joined = RtlText.Visualize("سلامتی");
            Assert.IsFalse(joined.Contains('\uFEDF') || joined.Contains('\uFEDE'),
                "The lam of سلامتی must be consumed by the ligature, not emitted as a separate letter.");
            Assert.IsFalse(joined.Contains('\uFE8E'),
                "The alef of سلامتی must be consumed by the ligature, not emitted as a separate letter.");
        }

        /// <summary>
        /// The alef-hamza and alef-madda ligature families are covered too, in both their forms.
        /// </summary>
        [Test]
        public void LamAlefLigatureFamiliesCoverEveryAlefVariant()
        {
            Assert.AreEqual(LamAlefMaddaIsolated, RtlText.Visualize("لآ"),
                "Lam followed by alef-madda needs its own ligature glyph.");
            Assert.AreEqual(LamAlefMaddaFinal + "\uFE91", RtlText.Visualize("بلآ"),
                "Lam-alef-madda must take its final form after a joining letter.");
            Assert.AreEqual(LamAlefHamzaAboveFinal + "\uFED7", RtlText.Visualize("قلأ"),
                "Lam followed by alef-hamza-above needs its own ligature glyph.");
            Assert.AreEqual(LamAlefHamzaBelowFinal + "\uFED7", RtlText.Visualize("قلإ"),
                "Lam followed by alef-hamza-below needs its own ligature glyph.");
            Assert.AreEqual(LamAlefHamzaAboveIsolated, RtlText.Visualize("لأ"),
                "An unjoined lam-alef-hamza pair must use the isolated ligature.");

            // A combining mark written between the two letters stays with the pair, so it cannot be
            // left between two glyphs where it would attach to the wrong one.
            Assert.AreEqual(LamAlefIsolated + "\u064E", RtlText.Visualize("لَا"),
                "A mark between lam and alef must stay attached to the ligature.");
        }

        /// <summary>
        /// Latin words and numbers are left exactly as written, including inside Persian sentences.
        /// </summary>
        [Test]
        public void LatinAndNumberRunsAreNotReshaped()
        {
            Assert.AreEqual("AETHER", RtlText.Visualize("AETHER"),
                "A string with no right-to-left letters must come back untouched.");
            Assert.AreEqual("AETHER 120", RtlText.Visualize("AETHER 120"),
                "Digits and Latin words must keep their order and their characters.");
            Assert.AreEqual("not-found: 1 key(s)", RtlText.Visualize("not-found: 1 key(s)"),
                "Punctuation in a Latin-only string must survive untouched.");

            // In a sentence, the Latin phrase keeps its own order and sits where its logical position
            // puts it, which for right-to-left text is to the left of the Persian words. "دوربین"
            // ("camera") is shaped as usual behind it.
            Assert.AreEqual("AETHER \uFEE6\uFBFF\uFE91\uFEAD\uFEED\uFEA9",
                RtlText.Visualize("دوربین AETHER"),
                "A Latin phrase inside a Persian sentence must keep its spelling and order.");
        }

        /// <summary>
        /// Arabic joining, and the Persian non-joiner that deliberately breaks it, are unchanged.
        /// </summary>
        [Test]
        public void ArabicJoiningAndTheZeroWidthNonJoinerAreUnchanged()
        {
            Assert.AreEqual("\uFEAA\uFEE4\uFEA4\uFEE3", RtlText.Visualize("محمد"),
                "Ordinary Arabic joining must be unaffected by the lam-alef ligature work.");

            // میشود: the non-joiner ends the yeh and stops the sheen from reaching back to it.
            string withNonJoiner = RtlText.Visualize("می" + ZeroWidthNonJoiner + "شود");
            Assert.AreEqual("\uFEA9\uFEEE\uFEB7" + ZeroWidthNonJoiner + "\uFBFD\uFEE3", withNonJoiner,
                "The non-joiner must keep the two halves of the word apart.");
            Assert.IsTrue(withNonJoiner.Contains(ZeroWidthNonJoiner),
                "The non-joiner must stay in the prepared text.");
        }

        /// <summary>
        /// Both shipped strings that contain a lam-alef reach the ligature, in a font that has it.
        /// </summary>
        [UnityTest]
        public IEnumerator ShippedPersianCopyWithLamAlefReachesTheLigature()
        {
            Assert.IsTrue(LanguageService.Set("fa"),
                "Persian must stay selectable; it is the shipped default language.");
            yield return null;

            var bodies = new[]
            {
                ("pause.restart.body", "سلامتی"),
                ("character.precursor.body", "علامتی"),
            };

            for (int i = 0; i < bodies.Length; i++)
            {
                string key = bodies[i].Item1;
                string word = bodies[i].Item2;
                string copy = MenuStrings.Get(key);
                Assert.IsTrue(copy.Contains(word),
                    key + " must still contain " + word + " for this test to mean anything.");

                string prepared = MenuUi.PrepareText(copy);
                Assert.IsTrue(prepared.Contains(LamAlefFinal),
                    key + " must be prepared with the lam-alef ligature of " + word + ".");
            }

            // The bundled font has to own the glyph the prepared text now asks for, or the ligature
            // would render as a placeholder box.
            Font font = MenuArt.Font;
            Assert.IsNotNull(font, "The menus must resolve a font for right-to-left text.");
            Assert.IsTrue(font.HasCharacter(LamAlefFinal),
                "The shipped font must contain the lam-alef ligature, or prepared Persian text " +
                "renders as a missing glyph.");
            Assert.IsTrue(font.HasCharacter(LamAlefIsolated),
                "The shipped font must contain the isolated lam-alef ligature as well.");
        }
    }
}
