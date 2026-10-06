using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Cameras
{
    /// <summary>
    /// A lightweight 2D follow camera with velocity look-ahead and optional world bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written in-project rather than pulled from Cinemachine. A metroidvania camera needs to follow,
    /// look ahead, respect region bounds, and snap on room transitions — that is a small, well
    /// understood amount of behaviour, and owning it avoids a heavyweight dependency, gives direct
    /// control over the mobile cost, and keeps camera behaviour reviewable in a diff.
    /// </para>
    /// <para>
    /// Following happens in <c>LateUpdate</c>, after gameplay has moved the target, so the camera
    /// never renders a frame using a stale target position.
    /// </para>
    /// <para>
    /// The camera reads the target's <see cref="Rigidbody2D"/> when present because
    /// <c>Transform.position</c> on an interpolated body lags behind the physics step, and following
    /// the interpolated position makes the whole frame judder.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What the camera follows. Usually the player's root transform.")]
        [SerializeField]
        private Transform _target;

        [Tooltip("Framing offset from the target, in world units. Raise it to show more of what is ahead of the player.")]
        [SerializeField]
        private Vector2 _offset = new Vector2(0f, 0.9f);

        [Header("Follow")]
        [Tooltip("Approximate time to catch up with the target. Larger is softer and more cinematic; smaller is tighter.")]
        [SerializeField, Range(0.02f, 1f)]
        private float _smoothTime = 0.16f;

        [Tooltip("Maximum follow speed. Caps the camera during long falls and Rootbind launches.")]
        [SerializeField, Min(0f)]
        private float _maxSpeed = 40f;

        [Header("Look ahead")]
        [Tooltip("How far the camera leads the target when moving, in world units.")]
        [SerializeField]
        private Vector2 _lookAhead = new Vector2(1.7f, 0.8f);

        [Tooltip("Rate at which look-ahead catches up. Lower values stop the camera swinging during direction changes.")]
        [SerializeField, Range(0.02f, 1f)]
        private float _lookAheadSmoothTime = 0.35f;

        [Tooltip("Threshold below which target speed is treated as standing still, in units/second.")]
        [SerializeField, Min(0f)]
        private float _lookAheadDeadZone = 0.4f;

        [Header("Bounds")]
        [Tooltip("Clamp the camera so it never shows outside the region's playable rectangle.")]
        [SerializeField]
        private bool _useBounds;

        [Tooltip("Minimum corner of the camera's allowed centre, in world units.")]
        [SerializeField]
        private Vector2 _boundsMin = new Vector2(-20f, -20f);

        [Tooltip("Maximum corner of the camera's allowed centre, in world units.")]
        [SerializeField]
        private Vector2 _boundsMax = new Vector2(20f, 20f);

        /// <summary>
        /// Takes the player's camera preferences: how softly the camera catches up, and how far it
        /// looks ahead.
        /// </summary>
        /// <remarks>
        /// Subscribed while the camera is alive rather than read every frame. The two values are
        /// smoothing constants — a frame that used the old value while the slider moved would be a
        /// frame nobody could see — so the work belongs on the change, not in <c>LateUpdate</c>.
        /// </remarks>
        public void ApplySettings()
        {
            CameraSettings wanted = AetherSettings.Ensure().Values.Camera;
            _smoothTime = wanted.FollowSmoothing;
            _lookAhead = new Vector2(wanted.LookAhead, _lookAhead.y);
        }

        private void OnEnable()
        {
            AetherSettings.Ensure().Changed += OnSettingsChanged;
            ApplySettings();
        }

        private void OnDisable()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(string id)
        {
            ApplySettings();
        }

        /// <summary>
        /// Points the camera at a target and confines it to the level's rectangle. Objects built at
        /// runtime have no inspector, and a camera that never leaves the level's bounds is what keeps
        /// the player from seeing the edge of the world.
        /// </summary>
        public void Configure(Transform target, Vector2 boundsMin, Vector2 boundsMax)
        {
            _target = target;
            _boundsMin = boundsMin;
            _boundsMax = boundsMax;
            _useBounds = true;
        }

        private Camera _camera;
        private Rigidbody2D _targetBody;
        private Vector3 _followVelocity;
        private Vector2 _currentLookAhead;
        private Vector2 _lookAheadVelocity;

        /// <summary>The transform being followed.</summary>
        public Transform Target => _target;

        /// <summary>The camera component. Exposed so gameplay can query the visible area.</summary>
        public Camera Camera => _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            _followVelocity = Vector3.zero;
            _lookAheadVelocity = Vector2.zero;
            _currentLookAhead = Vector2.zero;
        }

        /// <summary>
        /// Points the camera at a new target and snaps to it immediately.
        /// </summary>
        /// <remarks>
        /// Snapping is required on respawn and on region transitions. Smoothing into place would show
        /// the player the inside of the level between the old and new positions.
        /// </remarks>
        public void SetTarget(Transform target, bool snap)
        {
            _target = target;
            _targetBody = target != null ? target.GetComponent<Rigidbody2D>() : null;

            if (snap) SnapToTarget();
        }

        /// <summary>Moves the camera onto the target with no interpolation.</summary>
        public void SnapToTarget()
        {
            if (_target == null) return;

            Vector3 desired = ComputeTargetPosition(Vector2.zero);
            transform.position = new Vector3(desired.x, desired.y, transform.position.z);

            _followVelocity = Vector3.zero;
            _currentLookAhead = Vector2.zero;
            _lookAheadVelocity = Vector2.zero;
        }

        /// <summary>Sets the rectangle the camera centre is confined to.</summary>
        public void SetBounds(Vector2 min, Vector2 max)
        {
            _boundsMin = min;
            _boundsMax = max;
            _useBounds = true;
        }

        /// <summary>Turns off bounds clamping, for use in unbounded areas such as the boss arena approach.</summary>
        public void ClearBounds()
        {
            _useBounds = false;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Vector2 targetVelocity = ReadTargetVelocity();

            // Look-ahead is smoothed separately from the follow so a direction change does not whip
            // the frame across the screen; the dead zone keeps it still while the player idles.
            Vector2 desiredLookAhead = Mathf.Abs(targetVelocity.x) > _lookAheadDeadZone
                ? new Vector2(Mathf.Sign(targetVelocity.x) * _lookAhead.x, _lookAhead.y)
                : new Vector2(0f, _lookAhead.y);

            _currentLookAhead = Vector2.SmoothDamp(
                _currentLookAhead,
                desiredLookAhead,
                ref _lookAheadVelocity,
                _lookAheadSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            Vector3 desiredPosition = ComputeTargetPosition(_currentLookAhead);
            desiredPosition.z = transform.position.z;

            Vector3 next = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref _followVelocity,
                _smoothTime,
                _maxSpeed,
                Time.unscaledDeltaTime);

            transform.position = next;
        }

        private Vector2 ReadTargetVelocity()
        {
            if (_targetBody != null) return _targetBody.linearVelocity;

            // Fall back to the body lookup once, then remember the result so a body added later is
            // picked up without paying for GetComponent every frame.
            _targetBody = _target.GetComponent<Rigidbody2D>();
            return _targetBody != null ? _targetBody.linearVelocity : Vector2.zero;
        }

        private Vector3 ComputeTargetPosition(Vector2 lookAhead)
        {
            Vector2 focus = (Vector2)_target.position + _offset + lookAhead;

            if (_useBounds)
            {
                float halfHeight = _camera.orthographicSize;
                float halfWidth = halfHeight * _camera.aspect;

                // Clamp the centre so the viewport stays inside the bounds. When the bounds are
                // narrower than the viewport, centre the camera in them rather than snapping to an
                // arbitrary edge.
                float minX = _boundsMin.x + halfWidth;
                float maxX = _boundsMax.x - halfWidth;
                float minY = _boundsMin.y + halfHeight;
                float maxY = _boundsMax.y - halfHeight;

                focus.x = minX > maxX ? (_boundsMin.x + _boundsMax.x) * 0.5f : Mathf.Clamp(focus.x, minX, maxX);
                focus.y = minY > maxY ? (_boundsMin.y + _boundsMax.y) * 0.5f : Mathf.Clamp(focus.y, minY, maxY);
            }

            return new Vector3(focus.x, focus.y, transform.position.z);
        }
    }
}
