using System.Collections.Generic;
using Aether.Gameplay.Support;
using NUnit.Framework;
using UnityEngine;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Guards against accidentally replacing the illustration and environment passes with blank or stretched
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

        [Test]
        public void EnvironmentArtworkAndParallaxLayersAreDistinct()
        {
            Sprite canopyBack = EnvironmentSpriteLibrary.CanopyBack;
            Sprite canopyFront = EnvironmentSpriteLibrary.CanopyFront;
            Sprite story = EnvironmentSpriteLibrary.StoryMarker;
            Sprite discovery = EnvironmentSpriteLibrary.Discovery;
            Sprite checkpoint = EnvironmentSpriteLibrary.Checkpoint;
            Sprite exit = EnvironmentSpriteLibrary.Exit;
            Sprite sky = EnvironmentSpriteLibrary.SkyBackdrop;
            Sprite mountains = EnvironmentSpriteLibrary.MountainRidge;

            Assert.IsNotNull(canopyBack);
            Assert.IsNotNull(canopyFront);
            Assert.IsNotNull(story);
            Assert.IsNotNull(discovery);
            Assert.IsNotNull(checkpoint);
            Assert.IsNotNull(exit);
            Assert.IsNotNull(sky);
            Assert.IsNotNull(mountains);

            Assert.AreEqual(TextureWrapMode.Repeat, canopyBack.texture.wrapMode);
            Assert.AreEqual(TextureWrapMode.Repeat, canopyFront.texture.wrapMode);
            Assert.Greater(UniqueColours(canopyBack), 8);
            Assert.Greater(UniqueColours(canopyFront), 8);

            int visibleCanopyFront = VisiblePixels(canopyFront);
            Assert.Greater(visibleCanopyFront, 300, "Canopy front foliage must contain visible tendrils.");
            Assert.Less(visibleCanopyFront, canopyFront.texture.width * canopyFront.texture.height * 0.90f,
                "Canopy front foliage must remain a translucent cutout.");

            Assert.Greater(VisiblePixels(story), 200, "Story marker must have an inscribed silhouette.");
            Assert.Greater(VisiblePixels(discovery), 150, "Discovery crystal must be visible.");
            Assert.Greater(VisiblePixels(checkpoint), 500, "Checkpoint dais must have a complete runic ring.");
            Assert.Greater(VisiblePixels(exit), 500, "Exit gate must have pillars and portal threshold.");

            Assert.Greater(UniqueColours(story), 8);
            Assert.Greater(UniqueColours(discovery), 8);
            Assert.Greater(UniqueColours(checkpoint), 8);
            Assert.Greater(UniqueColours(exit), 8);
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
