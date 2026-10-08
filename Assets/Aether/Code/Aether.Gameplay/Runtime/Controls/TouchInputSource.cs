using UnityEngine;

namespace Aether.Gameplay.Controls
{
    /// <summary>Touch gameplay commands. Movement is intentionally digital: dedicated left/right controls replace the old joystick.</summary>
    public sealed class TouchInputSource : IGameplayInput
    {
        private bool _leftHeld;
        private bool _rightHeld;
        private bool _jumpHeld;
        private bool _jumpLatch;
        private bool _attackLatch;
        private bool _dodgeLatch;
        private bool _enabled = true;

        public Vector2 Move => !_enabled ? Vector2.zero :
            new Vector2((_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f), 0f);
        public bool JumpHeld => _enabled && _jumpHeld;
        public bool JumpPressed => _enabled && _jumpLatch;
        public bool AttackPressed => _enabled && _attackLatch;
        public bool DodgePressed => _enabled && _dodgeLatch;

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; if (!value) Reset(); }
        }

        public void PressLeft() { if (_enabled) _leftHeld = true; }
        public void ReleaseLeft() => _leftHeld = false;
        public void PressRight() { if (_enabled) _rightHeld = true; }
        public void ReleaseRight() => _rightHeld = false;

        public void PressJump() { if (!_enabled) return; _jumpHeld = true; _jumpLatch = true; }
        public void ReleaseJump() => _jumpHeld = false;
        public void PressAttack() { if (_enabled) _attackLatch = true; }
        public void PressDodge() { if (_enabled) _dodgeLatch = true; }

        public void Reset()
        {
            _leftHeld = _rightHeld = false;
            _jumpHeld = false;
            _jumpLatch = _attackLatch = _dodgeLatch = false;
        }

        public void ConsumeJump() => _jumpLatch = false;
        public void ConsumeAttack() => _attackLatch = false;
        public void ConsumeDodge() => _dodgeLatch = false;
    }
}
