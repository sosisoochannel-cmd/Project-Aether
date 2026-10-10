using System.Reflection;
using Aether.Gameplay.Menus;
using NUnit.Framework;

namespace Aether.Tests
{
    /// <summary>Regression tests for the legacy Text RTL preparation fallback.</summary>
    public sealed class RtlTextTests
    {
        private static string Visualize(string input)
        {
            // RtlText is intentionally an implementation detail; reflection lets tests exercise
            // the exact production path without widening the runtime API just for the test suite.
            var type = typeof(MenuUi).Assembly.GetType("Aether.Gameplay.Menus.RtlText");
            Assert.That(type, Is.Not.Null, "The production RTL preparation helper must exist.");
            MethodInfo method = type.GetMethod("Visualize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The production RTL entry point must exist.");
            return (string)method.Invoke(null, new object[] { input });
        }

        [Test]
        public void ArabicRightJoiningLettersConnectToTheirPreviousLetter()
        {
            // "سلام": alef must use its final form, and lam must use its medial form.
            Assert.That(Visualize("سلام"), Is.EqualTo("\uFEE1\uFE8E\uFEE0\uFEB3"));
        }

        [Test]
        public void PersianYehUsesItsActualMedialPresentationForm()
        {
            // U+FBFF is ARABIC LETTER FARSI YEH MEDIAL FORM; U+FEFF is a zero-width BOM.
            Assert.That(Visualize("بیب"), Is.EqualTo("\uFE90\uFBFF\uFE91"));
        }

        [Test]
        public void ZeroWidthNonJoinerBreaksJoining()
        {
            Assert.That(Visualize("ب\u200Cت"), Is.EqualTo("\uFE95\u200C\uFE8F"));
        }

        [Test]
        public void PlaceholderStaysReadableBesideArabicText()
        {
            Assert.That(Visualize("تم حفظ {0}"),
                Is.EqualTo("{0} \uFEC6\uFED4\uFEA3 \uFEE2\uFE97"));
        }

        [Test]
        public void ArabicCombiningMarksStayWithTheirBaseAfterReversal()
        {
            Assert.That(Visualize("بَت"), Is.EqualTo("\uFE96\uFE91َ"));
        }

        [Test]
        public void PureLeftToRightTextIsPreservedExactly()
        {
            const string input = "PROJECT AETHER — 120 FPS";
            Assert.That(Visualize(input), Is.EqualTo(input));
        }
    }
}
