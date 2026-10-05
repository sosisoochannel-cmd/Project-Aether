using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Aether.Gameplay.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Plays the studio intro in the real engine and measures the pixels it actually produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tools/verify/intro.py</c> proves the numbers — the lockup fits inside the safe area, keeps
    /// the artwork's aspect ratio and stays centred — but it proves them about arithmetic, not about
    /// what the renderer draws. This test closes that gap: it loads the shipped scene, lets the
    /// shipped component run, captures the frames the engine put on screen, and asserts the same
    /// properties from the pixels. The bug that made the mark fill the screen was invisible to the
    /// arithmetic and unmissable here.
    /// </para>
    /// <para>
    /// The frames it captures are written to <c>IntroRender/</c> in the project root, which is what
    /// the CI job uploads, so a run leaves behind a real screenshot of the intro beside the proof.
    /// </para>
    /// </remarks>
    public sealed class StudioIntroRenderTests
    {
        private const string OutputFolder = "IntroRender";

        /// <summary>Width over height of the lockup the artwork draws, measured from its ink.</summary>
        private const float LockupAspect = 800f / 814f;

        /// <summary>When to look, in seconds after the intro scene came up.</summary>
        private static readonly float[] CaptureTimes = { 0.75f, 1.35f, 1.95f, 2.45f };

        /// <summary>A pixel at or above this luminance is part of the mark.</summary>
        private const float InkLuminance = 0.35f;

        /// <summary>A pixel at or below this luminance is the black the intro is drawn on.</summary>
        private const float BlackLuminance = 0.03f;

        [UnityTest]
        public IEnumerator The_intro_draws_the_lockup_where_it_belongs()
        {
            // A landscape window, so the portrait half of the layout is not what gets photographed.
            // The Editor may ignore this; the frame's real size is reported either way.
            Screen.SetResolution(1920, 1080, false);
            yield return null;

            yield return SceneManager.LoadSceneAsync("StudioIntro", LoadSceneMode.Single);
            float start = Time.unscaledTime;

            StudioIntroSequence intro = Object.FindFirstObjectByType<StudioIntroSequence>();
            Assert.That(intro, Is.Not.Null,
                        "the intro scene has no StudioIntroSequence, so it can only show black");

            var frames = new List<Frame>();
            for (int i = 0; i < CaptureTimes.Length; i++)
            {
                while (Time.unscaledTime - start < CaptureTimes[i]) yield return null;
                yield return new WaitForEndOfFrame();
                Frame frame = Frame.Capture(CaptureTimes[i]);
                frames.Add(frame);
                Debug.Log($"[intro-render] {frame.Describe()}");
            }

            // This test is about what the intro draws, not about what Boot does afterwards: taking
            // the component out stops the hand-over and leaves the measurement above standing.
            Object.Destroy(intro);
            yield return null;

            WriteFrames(frames);

            Frame hold = frames[2];
            Assert.That(hold.Width, Is.GreaterThan(hold.Height),
                        $"the capture is {hold.Width}x{hold.Height}, not landscape; this test needs a "
                        + "landscape window or the frames are not the ones a phone would show");

            // Something is drawn, and it is a mark on a black screen rather than a picture of one.
            Assert.Greater(hold.InkFraction, 0.002f,
                           "the mark covers almost nothing of the frame; either it is not drawn or it "
                           + "is far smaller than the layout asks for");
            Assert.Less(hold.InkFraction, 0.25f,
                        $"the mark covers {hold.InkFraction:P1} of the frame; this is the shape of the "
                        + "bug where the sprite's own pixels-per-unit was left out of its scale and "
                        + "the lockup was drawn many times too large");
            Assert.Greater(hold.BlackFraction, 0.90f,
                           $"only {hold.BlackFraction:P1} of the frame is black; the intro is drawn on "
                           + "black, so something else is filling the screen");

            // Centred on the frame it has: a lockup pushed off-centre is what the old placement did
            // as soon as the safe area was not the whole screen.
            Assert.That(Mathf.Abs(hold.CentreX - 0.5f), Is.LessThan(0.03f),
                        $"the mark's centre is at {hold.CentreX:P1} across, not 50%");
            Assert.That(Mathf.Abs(hold.CentreY - 0.5f), Is.LessThan(0.03f),
                        $"the mark's centre is at {hold.CentreY:P1} down, not 50%");

            // Inside the frame on every side: a crop is the other half of the old bug.
            Assert.That(hold.Left, Is.GreaterThan(0.10f), "the mark runs off the left edge");
            Assert.That(hold.Right, Is.GreaterThan(0.10f), "the mark runs off the right edge");
            Assert.That(hold.Bottom, Is.GreaterThan(0.10f), "the mark runs off the bottom edge");
            Assert.That(hold.Top, Is.GreaterThan(0.10f), "the mark runs off the top edge");

            // The artwork's own proportions: the mark is never stretched to fit.
            Assert.That(hold.InkAspect, Is.EqualTo(LockupAspect).Within(0.08f),
                        $"the drawn lockup is {hold.InkAspect:0.000} wide over tall, the artwork is "
                        + $"{LockupAspect:0.000}; the fit is not preserving its aspect ratio");

            // And it animates: the mark arrives, holds, and leaves, rather than cutting in and out.
            Assert.Less(frames[0].InkFraction, hold.InkFraction,
                        "the mark is as present at 0.75s as at the hold, so there is no reveal");
            Assert.Less(frames[3].InkFraction, hold.InkFraction,
                        "the mark is as present at 2.45s as at the hold, so there is no exit");
        }

        private static void WriteFrames(List<Frame> frames)
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputFolder));
            Directory.CreateDirectory(folder);
            var summary = new StringBuilder();
            foreach (Frame frame in frames)
            {
                File.WriteAllBytes(Path.Combine(folder, $"intro-{frame.Seconds:0.00}s.png"), frame.Png);
                summary.AppendLine(frame.Describe());
            }

            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString());
        }

        /// <summary>What one captured frame contained, in fractions of the frame.</summary>
        private readonly struct Frame
        {
            public readonly float Seconds;
            public readonly int Width;
            public readonly int Height;
            public readonly float InkFraction;
            public readonly float BlackFraction;
            public readonly float CentreX;
            public readonly float CentreY;
            public readonly float Left;
            public readonly float Right;
            public readonly float Bottom;
            public readonly float Top;
            public readonly float InkAspect;
            public readonly byte[] Png;

            private Frame(float seconds, int width, int height, float inkFraction, float blackFraction,
                          float centreX, float centreY, float left, float right, float bottom,
                          float top, float inkAspect, byte[] png)
            {
                Seconds = seconds;
                Width = width;
                Height = height;
                InkFraction = inkFraction;
                BlackFraction = blackFraction;
                CentreX = centreX;
                CentreY = centreY;
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
                InkAspect = inkAspect;
                Png = png;
            }

            public static Frame Capture(float time)
            {
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int width = shot.width;
                int height = shot.height;
                Color32[] pixels = shot.GetPixels32();
                byte[] png = ImageConversion.EncodeToPNG(shot);
                Object.Destroy(shot);

                int ink = 0;
                int black = 0;
                int minX = width;
                int minY = height;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 pixel = pixels[row + x];
                        float luminance = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b) / 255f;
                        if (luminance >= InkLuminance)
                        {
                            ink++;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                        else if (luminance <= BlackLuminance)
                        {
                            black++;
                        }
                    }
                }

                float total = width * height;
                bool found = maxX >= minX && maxY >= minY;
                float inkWidth = found ? maxX - minX + 1 : 0f;
                float inkHeight = found ? maxY - minY + 1 : 0f;
                return new Frame(
                    time, width, height,
                    ink / total, black / total,
                    found ? ((minX + maxX) * 0.5f) / width : 0.5f,
                    found ? ((minY + maxY) * 0.5f) / height : 0.5f,
                    found ? minX / (float)width : 0f,
                    found ? (width - 1 - maxX) / (float)width : 0f,
                    found ? minY / (float)height : 0f,
                    found ? (height - 1 - maxY) / (float)height : 0f,
                    found && inkHeight > 0f ? inkWidth / inkHeight : 0f,
                    png);
            }

            public string Describe()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "t={0:0.00}s {1}x{2} ink={3:P2} black={4:P2} centre=({5:0.000},{6:0.000}) "
                    + "margins L{7:0.000} R{8:0.000} B{9:0.000} T{10:0.000} aspect={11:0.000}",
                    Seconds, Width, Height, InkFraction, BlackFraction, CentreX, CentreY,
                    Left, Right, Bottom, Top, InkAspect);
            }
        }
    }
}
