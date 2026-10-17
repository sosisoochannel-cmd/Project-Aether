using System.Reflection;
using NUnit.Framework;

namespace Aether.Tests
{
    /// <summary>Regression tests for mixed Persian, Latin, numeric and placeholder text.</summary>
    public sealed class RtlTextTests
    {
        private static string Visualize(string input)
        {
            var type = typeof(Aether.Gameplay.Menus.MenuUi).Assembly.GetType(
                "Aether.Gameplay.Menus.RtlText", true);
            var method = type.GetMethod("Visualize", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "the RTL preparation entry point must exist");
            return (string)method.Invoke(null, new object[] { input });
        }

        [Test]
        public void PersianSentence_PreservesPlaceholderToken()
        {
            string actual = Visualize("متن {0} ادامه");

            Assert.That(actual, Does.Contain("{0}"));
            Assert.That(actual.IndexOf("{0}"), Is.EqualTo(actual.LastIndexOf("{0}")));
        }

        [Test]
        public void PersianSentence_KeepsLatinWordReadableAndInOrder()
        {
            string actual = Visualize("متن AETHER");

            Assert.That(actual, Does.StartWith("AETHER "));
        }

        [Test]
        public void PersianSentence_KeepsVersionAndLatinAbbreviationInOrder()
        {
            string actual = Visualize("متن 120 FPS");

            Assert.That(actual, Does.StartWith("120 FPS "));
        }

        [Test]
        public void MixedScriptWord_DoesNotReverseLatinCharacters()
        {
            string actual = Visualize("متنAETHER");

            Assert.That(actual, Does.StartWith("AETHER"));
        }
    }
}
