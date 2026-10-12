using System.Collections.Generic;
using Aether.Gameplay.Support;
using NUnit.Framework;
using UnityEngine;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Guards against accidentally replacing the first illustration pass with blank or stretched
    /// debug sprites. These checks validate generated pixels, not a claim about final art direction.
    /// </summary>
    public sealed class CharacterVisualTests
    {
        [Test]
        public void RangerAndForestStalkerHaveDistinctLayeredArtwork()
        {
            Sprite ranger = CharacterSpriteLibrary.Player;
            Sprite stalker = CharacterSpriteLibrary.ForestStalker;

            Assert.IsNotNull(ranger);
            Assert.IsNotNull(stalker);
            Assert.AreEqual(96f, ranger.rect.width);
            Assert.AreEqual(96f, ranger.rect.height);
            Assert.AreNotSame(ranger.texture, stalker.texture);

            Assert.Greater(VisiblePixels(ranger), 500,
                "The ranger must contain a visible silhouette, not a transparent or missing sprite.");
            Assert.Greater(VisiblePixels(stalker), 500,
                "The Forest Stalker must contain a visible silhouette, not a transparent or missing sprite.");
            Assert.Greater(UniqueColours(ranger), 8,
                "The ranger should retain its armour, visor and highlight layers.");
            Assert.Greater(UniqueColours(stalker), 8,
                "The Forest Stalker should retain its bark, moss and eye details.");
        }

        [Test]
        public void SlashIsAnAlphaCutoutAndTerrainTexturesHaveSurfaceVariation()
        {
            Sprite slash = CharacterSpriteLibrary.Slash;
            Assert.IsNotNull(slash);
            int visibleSlashPixels = VisiblePixels(slash);
            Assert.Greater(visibleSlashPixels, 80, "The slash must contain a visible arc.");
            Assert.Less(visibleSlashPixels, slash.texture.width * slash.texture.height * 0.70f,
                "The slash must remain a cutout, not a full-screen rectangle.");

            Sprite ground = EnvironmentSpriteLibrary.Ground;
            Sprite rock = EnvironmentSpriteLibrary.DeepRock;
            Sprite platform = EnvironmentSpriteLibrary.Platform;
            Assert.AreEqual(TextureWrapMode.Repeat, ground.texture.wrapMode);
            Assert.AreEqual(TextureWrapMode.Repeat, rock.texture.wrapMode);
            Assert.AreEqual(TextureWrapMode.Repeat, platform.texture.wrapMode);
            Assert.Greater(UniqueColours(ground), 8, "Ground texture lost its soil variation.");
            Assert.Greater(UniqueColours(rock), 8, "Rock texture lost its fissures and shading.");
            Assert.Greater(UniqueColours(platform), 4, "Platform texture lost its surface edge.");
        }

        private static int VisiblePixels(Sprite sprite)
        {
            Color32[] pixels = sprite.texture.GetPixels32();
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 8) count++;
            return count;
        }

        private static int UniqueColours(Sprite sprite)
        {
            Color32[] pixels = sprite.texture.GetPixels32();
            var colours = new HashSet<Color32>();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 8) colours.Add(pixels[i]);
                if (colours.Count > 64) return colours.Count;
            }
            return colours.Count;
        }
    }
}