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
    /// The object is assembled <b>inactive</b> and switched on at the end. Adding a component to an
    /// active object runs its <c>Awake</c> immediately, and the controller validates its tuning in
    /// <c>Awake</c>; building inactive means every component is fully configured before any of them
    /// starts, so no startup order can leave the player half-wired.
    /// </para>
    /// <para>
    /// The player parts are each added exactly once, and <see cref="RequireSingle{T}"/> verifies that
    /// after the fact — because <c>[RequireComponent]</c> on a part can add a component as a side
    /// effect of adding another one, which is how a second, untuned controller used to appear.
    /// </para>
    /// </remarks>
    public static class PlayerFactory
    {
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
            body.position = centre;
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
            renderer.sprite = CharacterSpriteLibrary.Player;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingOrder;

            // The controller is added LAST of the player parts, and this order is load-bearing.
            // Unity satisfies [RequireComponent] by adding the missing component to the object, so a
            // part that required the controller back would have the controller created — and Awoken —
            // early, before Configure(tuning) below could run, leaving a second controller that
            // reports "no tuning asset". The controller's own requirements are unaffected: adding the
            // motor, the health and the combat here satisfies them with exactly the components this
            // method creates, and the object is switched on only once they all exist.
            var motor = host.AddComponent<PlayerMotor>();
            var health = host.AddComponent<PlayerHealth>();
            var combat = host.AddComponent<PlayerCombat>();
            var deviceInput = host.AddComponent<PlayerInputReader>();
            var router = host.AddComponent<GameplayInputRouter>();
            var controller = host.AddComponent<PlayerController>();
            var feedback = host.AddComponent<PlayerFeedback>();
            feedback.Configure(renderer);

            var animator = host.AddComponent<PlayerAnimator>();
            animator.Configure(controller, motor, combat, health, renderer);

            var slashObject = new GameObject("AttackSlash");
            slashObject.transform.SetParent(host.transform, false);
            var slashRenderer = slashObject.AddComponent<SpriteRenderer>();
            var attackView = host.AddComponent<PlayerAttackView>();
            attackView.Configure(combat, motor, slashRenderer);

            router.AddSource(deviceInput);
            motor.ConfigureGrounding(GameplayLayers.Ground);
            combat.Configure(firstAttack, GameplayLayers.Enemy);

            // Configure before activation: the controller validates its tuning in Awake and switches
            // itself off without one, so the value has to be in place first.
            controller.Configure(tuning);

            // The components above are added, not searched for, so each of these is a statement about
            // what AddComponent actually did. Together they assert the invariant the player depends
            // on: exactly one of each part, and the controller returned here is the one on the object.
            RequireSingle<PlayerController>(host, controller);
            RequireSingle<PlayerHealth>(host);
            RequireSingle<PlayerCombat>(host);
            RequireSingle<PlayerMotor>(host);
            RequireSingle<PlayerAnimator>(host);

            host.SetActive(true);

            // A null tuning is reported by the controller itself. Reaching this with a tuning in hand
            // means the value did not get where it was going, which only the factory can see.
            if (tuning != null && !controller.IsInitialized)
            {
                Debug.LogError(
                    $"{nameof(PlayerFactory)} configured the player with " +
                    $"{nameof(PlayerTuningData)} '{tuning.name}', but the controller on '{host.name}' " +
                    "is not initialized with it. The player will not move.",
                    controller);
            }

            return controller;
        }

        /// <summary>
        /// Fails loudly if a player part is missing or duplicated, instead of leaving a half-wired
        /// player in the scene.
        /// </summary>
        private static void RequireSingle<T>(GameObject host, T expected = null) where T : Component
        {
            var found = host.GetComponents<T>();
            if (found.Length == 1 && (expected == null || ReferenceEquals(found[0], expected))) return;

            Debug.LogError(
                $"{nameof(PlayerFactory)} expected exactly one {typeof(T).Name} on '{host.name}' but " +
                $"found {found.Length}."
                + (expected != null && found.Length == 1
                    ? " The component it built a reference to is not the one on the object."
                    : string.Empty),
                host);
        }
    }
}
