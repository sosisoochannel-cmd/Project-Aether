using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Asset-free combat presentation for the generated hero. It never owns combat rules; it only
    /// visualises attack starts and confirmed hits so the game reads immediately on a phone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatVisual : MonoBehaviour
    {
        private PlayerController _controller;
        private PlayerCombat _combat;
        private Transform _fxRoot;
        private SpriteRenderer _slash;
        private SpriteRenderer _impact;
        private float _slashTime;
        private float _impactTime;
        private Vector3 _slashBase;
        private Vector3 _impactBase;

        private const float SlashDuration = 0.13f;
        private const float ImpactDuration = 0.16f;

        public void Configure()
        {
            _controller = GetComponent<PlayerController>();
            _combat = GetComponent<PlayerCombat>();

            _fxRoot = new GameObject("CombatFX").transform;
            _fxRoot.SetParent(transform, false);

            _slash = CreateFX("AttackArc", PlaceholderVisuals.Diamond, 12);
            _impact = CreateFX("HitBurst", PlaceholderVisuals.Circle, 13);
            _slashBase = _slash.transform.localScale;
            _impactBase = _impact.transform.localScale;

            _slash.enabled = false;
            _impact.enabled = false;
        }

        private SpriteRenderer CreateFX(string name, Sprite sprite, int sorting)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_fxRoot, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = sorting;
            r.color = new Color(0.82f, 0.92f, 1f, 0.9f);
            return r;
        }

        private void OnEnable()
        {
            if (_combat == null) return;
            _combat.AttackStarted += OnAttackStarted;
            _combat.HitLanded += OnHitLanded;
        }

        private void OnDisable()
        {
            if (_combat == null) return;
            _combat.AttackStarted -= OnAttackStarted;
            _combat.HitLanded -= OnHitLanded;
        }

        private void OnAttackStarted(AttackDefinition attack)
        {
            if (_slash == null || _controller == null) return;

            int facing = _controller.FacingSign;
            _slash.transform.localPosition = new Vector3(0.62f * facing, 0.08f, -0.05f);
            _slash.transform.localRotation = Quaternion.Euler(0f, 0f, facing > 0 ? -28f : 28f);
            _slash.transform.localScale = new Vector3(_slashBase.x * 1.35f, _slashBase.y * 0.72f, 1f);
            _slash.color = new Color(0.84f, 0.94f, 1f, 0.92f);
            _slash.enabled = true;
            _slashTime = SlashDuration;
        }

        private void OnHitLanded(Collider2D other)
        {
            if (_impact == null || other == null) return;

            Vector2 from = transform.position;
            Vector2 point = other.ClosestPoint(from);
            _impact.transform.position = new Vector3(point.x, point.y, transform.position.z - 0.08f);
            _impact.transform.localScale = _impactBase * 0.7f;
            _impact.color = new Color(0.95f, 0.98f, 1f, 0.95f);
            _impact.enabled = true;
            _impactTime = ImpactDuration;
        }

        private void Update()
        {
            if (_slash != null && _slash.enabled)
            {
                _slashTime -= Time.deltaTime;
                float t = Mathf.Clamp01(_slashTime / SlashDuration);
                _slash.color = new Color(0.84f, 0.94f, 1f, t * 0.92f);
                _slash.transform.localScale = Vector3.Lerp(_slashBase * 0.8f, _slashBase * 1.35f, 1f - t);
                if (_slashTime <= 0f) _slash.enabled = false;
            }

            if (_impact != null && _impact.enabled)
            {
                _impactTime -= Time.deltaTime;
                float t = Mathf.Clamp01(_impactTime / ImpactDuration);
                _impact.color = new Color(0.95f, 0.98f, 1f, t * 0.95f);
                _impact.transform.localScale = _impactBase * (1.1f - t * 0.45f);
                if (_impactTime <= 0f) _impact.enabled = false;
            }
        }
    }
}
