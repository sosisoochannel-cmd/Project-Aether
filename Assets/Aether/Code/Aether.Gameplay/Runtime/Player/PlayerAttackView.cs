using UnityEngine;
using Aether.Data.Config;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Draws a brief, directional slash only during the attack's real active window.
    /// The AttackRunner remains the sole authority on when the hitbox is live.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttackView : MonoBehaviour
    {
        private const float SlashLifetime = 0.14f;

        private PlayerCombat _combat;
        private PlayerMotor _motor;
        private SpriteRenderer _renderer;
        private float _remaining;

        /// <summary>Wires the view before the player object is activated.</summary>
        public void Configure(PlayerCombat combat, PlayerMotor motor, SpriteRenderer renderer)
        {
            _combat = combat;
            _motor = motor;
            _renderer = renderer;
            if (_renderer != null)
            {
                _renderer.sprite = Aether.Gameplay.Support.CharacterSpriteLibrary.Slash;
                _renderer.sortingOrder = 10;
                _renderer.enabled = false;
            }
        }

        private void Awake()
        {
            if (_combat == null) _combat = GetComponent<PlayerCombat>();
            if (_motor == null) _motor = GetComponent<PlayerMotor>();
            if (_renderer == null)
            {
                Transform slash = transform.Find("AttackSlash");
                if (slash != null) _renderer = slash.GetComponent<SpriteRenderer>();
            }
        }

        private void OnEnable()
        {
            if (_combat == null) _combat = GetComponent<PlayerCombat>();
            if (_combat != null) _combat.HitboxActivated += OnHitboxActivated;
        }

        private void OnDisable()
        {
            if (_combat != null) _combat.HitboxActivated -= OnHitboxActivated;
            Hide();
        }

        private void OnHitboxActivated(AttackDefinition attack)
        {
            if (_renderer == null || _motor == null) return;

            int facing = _motor.FacingSign;
            _renderer.flipX = facing < 0;
            _renderer.transform.localPosition = new Vector3(0.64f * facing, 0.10f, 0f);
            _renderer.color = Color.white;
            _renderer.enabled = true;
            _remaining = attack != null && attack.Active > 0f ? attack.Active : SlashLifetime;
        }

        private void Update()
        {
            if (_remaining <= 0f) return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Hide();
                return;
            }

            Color colour = _renderer.color;
            colour.a = Mathf.Clamp01(_remaining / SlashLifetime);
            _renderer.color = colour;
        }

        private void Hide()
        {
            _remaining = 0f;
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}