using System.Collections.Generic;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Identifiers for the protagonist's animation states.
    /// </summary>
    public enum PlayerAnimationId
    {
        Idle = 0,
        IdleAlt = 1,
        Walk = 2,
        Run = 3,
        RunStop = 4,
        JumpAnticipation = 5,
        JumpRise = 6,
        JumpApex = 7,
        JumpFall = 8,
        JumpLand = 9,
        Attack1 = 10,
        Attack2 = 11,
        Dodge = 12,
        Hurt = 13,
        Dead = 14
    }

    /// <summary>
    /// A sequence of animation sprite frames with playback timing.
    /// </summary>
    public sealed class PlayerAnimationClip
    {
        public PlayerAnimationId Id { get; }
        public string Name { get; }
        public Sprite[] Frames { get; }
        public float Fps { get; }
        public bool Loops { get; }

        public int FrameCount => Frames != null ? Frames.Length : 0;

        public PlayerAnimationClip(PlayerAnimationId id, string name, Sprite[] frames, float fps, bool loops)
        {
            Id = id;
            Name = name;
            Frames = frames;
            Fps = fps > 0f ? fps : 12f;
            Loops = loops;
        }

        public Sprite GetFrame(int index)
        {
            if (Frames == null || Frames.Length == 0) return null;
            if (index < 0) return Frames[0];
            if (index >= Frames.Length) return Loops ? Frames[index % Frames.Length] : Frames[Frames.Length - 1];
            return Frames[index];
        }
    }

    /// <summary>
    /// Slices and caches the protagonist's sprite sheets for runtime animation playback.
    /// Guaranteed zero per-frame garbage allocations.
    /// </summary>
    public static class PlayerAnimationCatalog
    {
        private const int Cols = 16;
        private const int Rows = 13;
        private const int TotalFrames = 204;
        // Keep the intended world-space frame size independent of the PNG's imported resolution.
        // Unity may import the source at 2048x1664 on Android even when the source was authored at
        // twice that size. Hard-coding 256px then asks Sprite.Create for rectangles outside the
        // actual texture and aborts the entire region bootstrap.
        private const float IntendedFrameWorldSize = 1.6f;
        private const string ResourcePath = "Characters/Player/Aether_Hero_SpriteSheet";

        private static readonly Dictionary<PlayerAnimationId, PlayerAnimationClip> _clips =
            new Dictionary<PlayerAnimationId, PlayerAnimationClip>();

        private static bool _initialized;

        /// <summary>
        /// Retrieves the clip for the specified animation ID.
        /// </summary>
        public static PlayerAnimationClip GetClip(PlayerAnimationId id)
        {
            EnsureInitialized();
            if (_clips.TryGetValue(id, out var clip)) return clip;
            return null;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            Sprite[] allFrames = null;
            Texture2D sheet = Resources.Load<Texture2D>(ResourcePath);

            if (sheet != null)
            {
                // Derive cell dimensions from the imported texture instead of assuming a fixed
                // resolution. Reject malformed layouts before Sprite.Create can throw mid-boot.
                bool validGrid = sheet.width >= Cols && sheet.height >= Rows
                    && sheet.width % Cols == 0 && sheet.height % Rows == 0;
                if (validGrid)
                {
                    int frameWidth = sheet.width / Cols;
                    int frameHeight = sheet.height / Rows;
                    float pixelsPerUnit = frameWidth / IntendedFrameWorldSize;

                    if (frameWidth == frameHeight && pixelsPerUnit > 0f)
                    {
                        var created = new List<Sprite>(TotalFrames);
                        try
                        {
                            allFrames = new Sprite[TotalFrames];
                            for (int i = 0; i < TotalFrames; i++)
                            {
                                int c = i % Cols;
                                int r = i / Cols;
                                // Row 0 in image files is at the top; Unity's texture origin is bottom-left.
                                int unityY = (Rows - 1 - r) * frameHeight;
                                int unityX = c * frameWidth;
                                var rect = new Rect(unityX, unityY, frameWidth, frameHeight);

                                // Defensive bounds check: never allow a bad imported texture to crash
                                // the level-start sequence.
                                if (rect.xMin < 0f || rect.yMin < 0f
                                    || rect.xMax > sheet.width || rect.yMax > sheet.height)
                                    throw new System.InvalidOperationException(
                                        $"Animation frame {i} rect {rect} exceeds imported sheet {sheet.width}x{sheet.height}.");

                                Sprite sprite = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
                                if (sprite == null)
                                    throw new System.InvalidOperationException($"Unity failed to create animation frame {i}.");
                                sprite.name = $"Hero_Frame_{i:000}";
                                sprite.hideFlags = HideFlags.HideAndDontSave;
                                allFrames[i] = sprite;
                                created.Add(sprite);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            for (int i = 0; i < created.Count; i++)
                            {
                                if (created[i] != null) Object.Destroy(created[i]);
                            }
                            allFrames = null;
                            Debug.LogError(
                                $"[player-animation] Sprite sheet '{ResourcePath}' ({sheet.width}x{sheet.height}) " +
                                $"could not be sliced safely; using the guaranteed fallback sprite. {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogError(
                            $"[player-animation] Invalid frame grid in '{ResourcePath}': " +
                            $"{sheet.width}x{sheet.height} produces {frameWidth}x{frameHeight} cells; " +
                            "expected square cells. Using the guaranteed fallback sprite.");
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[player-animation] Invalid sheet dimensions for '{ResourcePath}': " +
                        $"{sheet.width}x{sheet.height} cannot be divided into {Cols}x{Rows} cells. " +
                        "Using the guaranteed fallback sprite.");
                }
            }
            else
            {
                Debug.LogError(
                    $"[player-animation] Resource '{ResourcePath}' was not found. Using the guaranteed fallback sprite.");
            }

            RegisterClip(PlayerAnimationId.Idle, "Hero_Idle", allFrames, 0, 24, 12f, true);
            RegisterClip(PlayerAnimationId.IdleAlt, "Hero_IdleAlt", allFrames, 24, 24, 12f, false);
            RegisterClip(PlayerAnimationId.Walk, "Hero_Walk", allFrames, 48, 16, 16f, true);
            RegisterClip(PlayerAnimationId.Run, "Hero_Run", allFrames, 64, 20, 18f, true);
            RegisterClip(PlayerAnimationId.RunStop, "Hero_RunStop", allFrames, 84, 8, 20f, false);
            RegisterClip(PlayerAnimationId.JumpAnticipation, "Hero_JumpAnticipation", allFrames, 92, 6, 20f, false);
            RegisterClip(PlayerAnimationId.JumpRise, "Hero_JumpRise", allFrames, 98, 10, 20f, true);
            RegisterClip(PlayerAnimationId.JumpApex, "Hero_JumpApex", allFrames, 108, 8, 20f, false);
            RegisterClip(PlayerAnimationId.JumpFall, "Hero_JumpFall", allFrames, 116, 10, 20f, true);
            RegisterClip(PlayerAnimationId.JumpLand, "Hero_JumpLand", allFrames, 126, 8, 20f, false);
            RegisterClip(PlayerAnimationId.Attack1, "Hero_Attack1", allFrames, 134, 16, 20f, false);
            RegisterClip(PlayerAnimationId.Attack2, "Hero_Attack2", allFrames, 150, 16, 20f, false);
            RegisterClip(PlayerAnimationId.Dodge, "Hero_Dodge", allFrames, 166, 12, 20f, true);
            RegisterClip(PlayerAnimationId.Hurt, "Hero_Hurt", allFrames, 178, 10, 20f, false);
            RegisterClip(PlayerAnimationId.Dead, "Hero_Dead", allFrames, 188, 16, 20f, false);
        }

        private static void RegisterClip(PlayerAnimationId id, string name, Sprite[] allFrames,
                                         int start, int count, float fps, bool loops)
        {
            Sprite[] frames;
            if (allFrames != null && start + count <= allFrames.Length)
            {
                frames = new Sprite[count];
                for (int i = 0; i < count; i++)
                {
                    frames[i] = allFrames[start + i];
                }
            }
            else
            {
                // Fallback for isolated headless fixtures where Resources asset is not mounted.
                Sprite fallback = CharacterSpriteLibrary.Player;
                frames = new Sprite[] { fallback };
            }

            _clips[id] = new PlayerAnimationClip(id, name, frames, fps, loops);
        }
    }
}
