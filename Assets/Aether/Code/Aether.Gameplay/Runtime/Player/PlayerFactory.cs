using Aether.Data.Config;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Builds the player object in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no player prefab. The body's size comes from <see cref="PlayerTuningData"/>, which is
    /// the same source the traversal solver reads, so the character the level was designed around and
    /// the character that actually runs it cannot disagree. A prefab would be a second place for that
    /// number to live.
    /// </para>
    /// <para>
    /// The object is assembled <b>inactive</b> and switched on at the end. Unity runs
    /// <c>Awake</c> the moment a component is added to an active object, and the controller reads its
    /// tuning in <c>Awake</c>; building inactive means every component is fully configured before any
    /// of them starts, so no startup order can leave the player half-wired.
    /// </para>
    /// </remarks>
    public static class PlayerFactory
    {
        private static readonly Color BodyColour = new Color(0.88f, 0.91f, 0.98f, 1f);
        private const int SortingOrder = 6;

        /// <summary>Creates the player, standing at <paramref name="centre"/>.</summary>
        public static PlayerController Create(Vector2 centre, PlayerTuningData tuning,
                                              AttackDefinition firstAttack, Transform parent)
        {
            var host = new GameObject("Player");
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            host.transform.position = centre;
            GameplayLayers.Assign(host, GameplayLayers.PlayerName);

            var body = host.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            float width = tuning != null ? tuning.BodyWidth : 0.7f;
            float height = tuning != null ? tuning.BodyHeight : 1.4f;

            var collider = host.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(width, height);
            collider.offset = Vector2.zero;

            // The visual is a child object rather than a scaled renderer on the body: scaling the
            // body's own transform would scale its collider with it, and the collider is the thing
            // the level was designed around.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(host.transform, false);
            visual.transform.localScale = new Vector3(width, height, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Triangle;
            renderer.color = BodyColour;
            renderer.sortingOrder = SortingOrder;

            // Layered silhouette: the playable character is no longer a featureless rectangle. The
            // body, head, core and eyes remain generated sprites so the game stays asset-light, but
            // their proportions create a readable hero silhouette at phone scale.
            var head = new GameObject("Head");
            head.transform.SetParent(host.transform, false);
            head.transform.localPosition = new Vector3(0f, height * 0.34f, 0f);
            head.transform.localScale = Vector3.one * Mathf.Min(width * 0.72f, height * 0.30f);
            var headRenderer = head.AddComponent<SpriteRenderer>();
            headRenderer.sprite = PlaceholderVisuals.Circle;
            headRenderer.color = new Color(0.96f, 0.97f, 1f, 1f);
            headRenderer.sortingOrder = SortingOrder + 2;

            var core = new GameObject("AetherCore");
            core.transform.SetParent(host.transform, false);
            core.transform.localPosition = new Vector3(0f, height * 0.04f, -0.01f);
            core.transform.localScale = Vector3.one * Mathf.Min(width * 0.30f, height * 0.18f);
            var coreRenderer = core.AddComponent<SpriteRenderer>();
            coreRenderer.sprite = PlaceholderVisuals.Diamond;
            coreRenderer.color = new Color(0.76f, 0.86f, 1f, 1f);
            coreRenderer.sortingOrder = SortingOrder + 1;

            Transform eyeL = CreateEye(host.transform, -width * 0.16f, height * 0.39f, width * 0.08f);
            Transform eyeR = CreateEye(host.transform, width * 0.16f, height * 0.39f, width * 0.08f);
            var heroMotion = host.AddComponent<PlayerHeroVisual>();
            heroMotion.Configure(visual.transform, head.transform, core.transform, eyeL, eyeR);

            // Order matters only in that the controller's Awake looks its siblings up, and every
            // one of them already exists by the time it is added.
            var motor = host.AddComponent<PlayerMotor>();
            var combat = host.AddComponent<PlayerCombat>();
            var health = host.AddComponent<PlayerHealth>();
            var deviceInput = host.AddComponent<PlayerInputReader>();
            var router = host.AddComponent<GameplayInputRouter>();
            var controller = host.AddComponent<PlayerController>();
            var feedback = host.AddComponent<PlayerFeedback>();
            feedback.Configure(renderer);

            router.AddSource(deviceInput);
            motor.ConfigureGrounding(GameplayLayers.Ground);
            combat.Configure(firstAttack, GameplayLayers.Enemy);

            // Configure before activation: the controller caches its tuning in Awake and reports a
            // missing asset there, so the value has to be in place first.
            controller.Configure(tuning);
            host.SetActive(true);
            return controller;
        }

        private static Transform CreateEye(Transform parent, float x, float y, float size)
        {
            var eye = new GameObject("Eye");
            eye.transform.SetParent(parent, false);
            eye.transform.localPosition = new Vector3(x, y, -0.02f);
            eye.transform.localScale = new Vector3(size, size * 0.55f, 1f);
            var r = eye.AddComponent<SpriteRenderer>();
            r.sprite = PlaceholderVisuals.Square;
            r.color = new Color(0.08f, 0.10f, 0.14f, 1f);
            r.sortingOrder = SortingOrder + 3;
            return eye.transform;
        }
    }
}
