using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Shows whether a checkpoint has taken.
    /// </summary>
    /// <remarks>
    /// A checkpoint the player cannot see working is a checkpoint they will not trust, and the whole
    /// point of this one is that dying stops being frightening. The trigger announces when it takes
    /// and when a retry re-arms it; this colours the ring.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CheckpointView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private CheckpointTrigger _trigger;

        /// <summary>Supplies the visual. Called by the level builder.</summary>
        public void Configure(SpriteRenderer renderer, CheckpointTrigger trigger)
        {
            if (_trigger != null) _trigger.ActivatedChanged -= OnActivatedChanged;

            _renderer = renderer;
            _trigger = trigger;

            // AddComponent invokes OnEnable before the builder can call Configure on an active
            // GameObject. Subscribe here too, and reflect the current state instead of always
            // painting a restored checkpoint as idle.
            if (isActiveAndEnabled && _trigger != null)
                _trigger.ActivatedChanged += OnActivatedChanged;

            OnActivatedChanged(_trigger != null && _trigger.Activated);
        }

        private void OnEnable()
        {
            if (_trigger != null)
            {
                _trigger.ActivatedChanged -= OnActivatedChanged;
                _trigger.ActivatedChanged += OnActivatedChanged;
                OnActivatedChanged(_trigger.Activated);
            }
        }

        private void OnDisable()
        {
            if (_trigger != null) _trigger.ActivatedChanged -= OnActivatedChanged;
        }

        private void OnActivatedChanged(bool activated)
        {
            if (_renderer != null)
            {
                _renderer.color = activated ? LevelPalette.CheckpointActive : LevelPalette.CheckpointIdle;
            }
        }
    }
}
