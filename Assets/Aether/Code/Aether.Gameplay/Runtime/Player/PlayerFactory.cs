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
            renderer.sprite = PlaceholderVisuals.Square;
            renderer.color = BodyColour;
            renderer.sortingOrder = SortingOrder;

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
    }
}
