using UnityEngine;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// The on-screen controls, expressed as gameplay input.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class knows nothing about rendering and nothing about touch hardware: the view decides
    /// what a finger did and calls these methods, and gameplay reads the same members it reads from
    /// a keyboard. Either side can be replaced — a different control layout, or a replay that drives
    /// the same methods — without touching the other.
    /// </para>
    /// <para>
    /// Presses are latched, exactly like the device source, so a tap that begins and ends between two
    /// physics steps is still acted on exactly once.
    /// </para>
    /// </remarks>
    public sealed class TouchInputSource : IGameplayInput
    {
        private Vector2 _move;
        private bool _jumpHeld;
        private bool _jumpLatch;
        private bool _attackLatch;
        private bool _dodgeLatch;
        private bool _enabled = true;

        /// <inheritdoc />
        public Vector2 Move => _move;

        /// <inheritdoc />
        public bool JumpHeld => _jumpHeld && _enabled;

        /// <inheritdoc />
        public bool JumpPressed => _enabled && _jumpLatch;

        /// <inheritdoc />
        public bool AttackPressed => _enabled && _attackLatch;

        /// <inheritdoc />
        public bool DodgePressed => _enabled && _dodgeLatch;

        /// <inheritdoc />
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value) Reset();
            }
        }

        /// <summary>Analogue stick value. Magnitude is clamped to one by the view.</summary>
        public void SetMove(Vector2 move)
        {
            _move = _enabled ? Vector2.ClampMagnitude(move, 1f) : Vector2.zero;
        }

        /// <summary>Finger down on jump.</summary>
        public void PressJump()
        {
            if (!_enabled) return;
            _jumpHeld = true;
            _jumpLatch = true;
        }

        /// <summary>Finger lifted from jump.</summary>
        public void ReleaseJump() => _jumpHeld = false;

        /// <summary>Finger down on attack.</summary>
        public void PressAttack()
        {
            if (_enabled) _attackLatch = true;
        }

        /// <summary>Finger down on dodge.</summary>
        public void PressDodge()
        {
            if (_enabled) _dodgeLatch = true;
        }

        /// <summary>Drops every held and latched value. Called when the overlay is hidden.</summary>
        public void Reset()
        {
            _move = Vector2.zero;
            _jumpHeld = false;
            _jumpLatch = false;
            _attackLatch = false;
            _dodgeLatch = false;
        }

        /// <inheritdoc />
        public void ConsumeJump() => _jumpLatch = false;

        /// <inheritdoc />
        public void ConsumeAttack() => _attackLatch = false;

        /// <inheritdoc />
        public void ConsumeDodge() => _dodgeLatch = false;

    }
}
