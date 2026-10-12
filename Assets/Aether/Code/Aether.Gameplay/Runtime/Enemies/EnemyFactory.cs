using Aether.Data.Config;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Enemies
{
    /// <summary>
    /// Builds an enemy object from its archetype definition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that makes a Forest Stalker behave like a Forest Stalker — health, reach, the
    /// wind-up before it commits, the pause that gives the player their answer — lives in the
    /// definition asset. This factory only gives it a body. That split is why teaching a new
    /// archetype is an authoring task and not a code change.
    /// </para>
    /// <para>
    /// The body is assembled inactive and switched on once configured, for the same reason as the
    /// player: no component should ever run in a half-built state.
    /// </para>
    /// </remarks>
    public static class EnemyFactory
    {
        // Gameplay dimensions stay separate from the illustrated silhouette.
        private const float BodyWidth = 0.9f;
        private const float BodyHeight = 1.1f;
        private const int SortingOrder = 5;

        /// <summary>Creates an enemy standing at <paramref name="feet"/>.</summary>
        public static EnemyController Create(EnemyDefinition definition, Vector2 feet, Transform parent,
                                             int patrolTiles)
        {
            Color colour = LevelPalette.EnemyIdle;
            var host = new GameObject($"Enemy_{definition.TypeId}");
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            GameplayLayers.Assign(host, GameplayLayers.EnemyName);

            var body = host.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = host.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(BodyWidth, BodyHeight);
            collider.offset = Vector2.zero;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(host.transform, false);
            visual.transform.localScale = new Vector3(BodyWidth, BodyHeight, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = CharacterSpriteLibrary.ForestStalker;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingOrder;

            var motor = host.AddComponent<EnemyMotor2D>();
            var health = host.AddComponent<EnemyHealth>();
            var controller = host.AddComponent<EnemyController>();

            // Presentation only, and added last so the controller's Awake has already wired the
            // state machine and health events it listens to.
            var telegraphObject = new GameObject("AttackTelegraph");
            telegraphObject.transform.SetParent(host.transform, false);
            telegraphObject.transform.localScale = new Vector3(BodyWidth * 1.28f, BodyHeight * 1.28f, 1f);
            var telegraphRenderer = telegraphObject.AddComponent<SpriteRenderer>();
            telegraphRenderer.sprite = PlaceholderVisuals.Ring;
            telegraphRenderer.color = colour;
            telegraphRenderer.sortingOrder = SortingOrder - 1;

            var telegraph = host.AddComponent<EnemyTelegraph>();
            telegraph.Configure(telegraphRenderer, controller);

            motor.ConfigureSolidLayers(GameplayLayers.Ground);
            controller.ConfigureLayers(GameplayLayers.Ground, GameplayLayers.Player);
            controller.ConfigurePatrol(patrolTiles);

            host.SetActive(true);

            // Placed by the feet and then snapped: the enemy's own grounding ray decides where the
            // floor actually is, so a level edit can never leave one floating or buried.
            host.transform.position = feet + new Vector2(0f, BodyHeight * 0.5f);
            controller.Initialize(definition, null);
            controller.ResetForSpawn(feet);
            return controller;
        }
    }
}
