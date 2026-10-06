using System.Collections;
using Aether.Gameplay.Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// Brings one block of a screen in: a short rise, a fade, and nothing that has to be watched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entrance is the menu's first impression and the first thing that can go wrong: it is easy
    /// to write an entrance the player has to wait for, and a menu that makes anyone wait is a menu
    /// with a bug. So the whole thing is under half a second, blocks are staggered by
    /// <see cref="MenuTheme.Motion.EntranceStagger"/>, and the input is live throughout — a row is
    /// usable the moment it exists, and the entrance is decoration on top of a working screen rather
    /// than a gate in front of one.
    /// </para>
    /// <para>
    /// It runs as one coroutine per block, on the block's own component, and finishes. Reduced motion
    /// removes the movement and shortens the fade; a low graphics tier does the same, because the
    /// player who asked for fewer effects meant this too.
    /// </para>
    /// </remarks>
    public sealed class MenuReveal : MonoBehaviour
    {
        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _restingPosition;
        private Coroutine _running;

        /// <summary>Attaches a reveal to a block, remembering where the block belongs.</summary>
        public static MenuReveal Attach(RectTransform block)
        {
            if (block == null) return null;

            MenuReveal reveal = block.GetComponent<MenuReveal>();
            if (reveal == null) reveal = block.gameObject.AddComponent<MenuReveal>();

            reveal._rect = block;
            reveal._group = MenuUi.Group(block);
            reveal._restingPosition = block.anchoredPosition;
            return reveal;
        }

        /// <summary>Plays the entrance, or snaps to the end of it when motion is off.</summary>
        /// <param name="delay">Seconds to wait before this block starts.</param>
        /// <param name="instant">True for a screen that is being rebuilt rather than entered.</param>
        public void Play(float delay, bool instant)
        {
            if (_rect == null) return;

            _restingPosition = _rect.anchoredPosition;
            Stop();

            bool still = instant || MenuPreferences.ReducedMotion || MenuPreferences.Tier ==
                         Aether.Core.Settings.GraphicsTier.Low;

            if (still)
            {
                Settle();
                return;
            }

            _running = StartCoroutine(Run(delay));
        }

        /// <summary>Keeps the block where it is. Used when a panel is hidden rather than entered.</summary>
        public void Settle()
        {
            if (_rect == null) return;

            _rect.anchoredPosition = _restingPosition;
            _rect.localScale = Vector3.one;
            if (_group != null) _group.alpha = 1f;
        }

        private void Stop()
        {
            if (_running == null) return;
            StopCoroutine(_running);
            _running = null;
        }

        private IEnumerator Run(float delay)
        {
            float duration = MenuTheme.Motion.Entrance(MenuPreferences.ReducedMotion);
            float rise = MenuTheme.Motion.EntranceRise;

            // Raycasts stay on for the whole entrance. A row that is still fading in is a row the
            // player can already press, which is the difference between an entrance and a delay.
            if (_group != null) _group.alpha = 0f;

            _rect.anchoredPosition = _restingPosition + new Vector2(0f, -rise);
            _rect.localScale = new Vector3(0.985f, 0.985f, 1f);

            float waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Ease out: quick to arrive, slow to settle. The last few percent are what read as
                // "polished" rather than "animated".
                float eased = 1f - ((1f - t) * (1f - t));

                _rect.anchoredPosition = Vector2.Lerp(_restingPosition + new Vector2(0f, -rise),
                                                     _restingPosition, eased);
                _rect.localScale = Vector3.Lerp(new Vector3(0.985f, 0.985f, 1f), Vector3.one, eased);
                if (_group != null) _group.alpha = eased;
                yield return null;
            }

            Settle();
            _running = null;
        }
    }
}
