using System.Collections;
using Aether.Core.Combat;
using Aether.Data.Config;
using Aether.Gameplay;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Player;
using Aether.Gameplay.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests validating the protagonist animation pipeline:
    /// catalog loading, frame counts, state transitions, combat synchronization, and facing flipping.
    /// </summary>
    public sealed class PlayerAnimationWiringTests
    {
        private GameObject _world;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AetherBoot.AutoBootEnabled = false;
            foreach (var b in Object.FindObjectsByType<LevelBootstrap>(FindObjectsSortMode.None))
            {
                Object.Destroy(b.gameObject);
            }

            _world = new GameObject("PlayerAnimationWiringTests World");
            CreateGround(new Vector2(0f, -0.5f), new Vector2(40f, 1f));
            Physics2D.SyncTransforms();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AetherBoot.AutoBootEnabled = true;
            if (_world != null) Object.Destroy(_world);
            _world = null;
            yield return null;
        }

        private GameObject CreateGround(Vector2 position, Vector2 size)
        {
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_world.transform, false);
            ground.transform.position = position;
            GameplayLayers.Assign(ground, GameplayLayers.GroundName);
            var col = ground.AddComponent<BoxCollider2D>();
            col.size = size;
            return ground;
        }

        private PlayerController SpawnTunedPlayer(Vector2 position)
        {
            var tuning = Resources.Load<PlayerTuningData>("Content/PlayerTuning");
            var attack = Resources.Load<AttackDefinition>("Content/Attack.Strike");
            return PlayerFactory.Create(position, tuning, attack, _world.transform);
        }

        [Test]
        public void AnimationCatalogHasAllFifteenSequencesConfigured()
        {
            var requiredSequences = new[]
            {
                PlayerAnimationId.Idle,
                PlayerAnimationId.IdleAlt,
                PlayerAnimationId.Walk,
                PlayerAnimationId.Run,
                PlayerAnimationId.RunStop,
                PlayerAnimationId.JumpAnticipation,
                PlayerAnimationId.JumpRise,
                PlayerAnimationId.JumpApex,
                PlayerAnimationId.JumpFall,
                PlayerAnimationId.JumpLand,
                PlayerAnimationId.Attack1,
                PlayerAnimationId.Attack2,
                PlayerAnimationId.Dodge,
                PlayerAnimationId.Hurt,
                PlayerAnimationId.Dead
            };

            foreach (var id in requiredSequences)
            {
                PlayerAnimationClip clip = PlayerAnimationCatalog.GetClip(id);
                Assert.IsNotNull(clip, $"PlayerAnimationCatalog missing clip for {id}");
                Assert.Greater(clip.FrameCount, 0, $"Clip {id} has 0 frames");
                Assert.IsNotNull(clip.GetFrame(0), $"Clip {id} first frame is null");
                Assert.Greater(clip.Fps, 0f, $"Clip {id} must have positive FPS");
            }
        }

        [UnityTest]
        public IEnumerator PlayerSpawnsInIdleAnimationAndWiredCorrectly()
        {
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();
            yield return null;

            PlayerAnimator animator = player.GetComponent<PlayerAnimator>();
            Assert.IsNotNull(animator, "Factory must attach PlayerAnimator to player host.");
            Assert.IsTrue(animator.IsInitialized, "PlayerAnimator must report initialized.");
            Assert.AreEqual(PlayerAnimationId.Idle, animator.CurrentAnimation,
                "Stationary player on spawn must be playing Idle animation.");

            SpriteRenderer renderer = player.GetComponentInChildren<SpriteRenderer>();
            Assert.IsNotNull(renderer, "Player must have a SpriteRenderer.");
            Assert.IsNotNull(renderer.sprite, "SpriteRenderer must have an active sprite.");
        }

        [UnityTest]
        public IEnumerator PlayerTransitionsBetweenWalkAndRunBasedOnSpeed()
        {
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();
            yield return null;

            PlayerAnimator animator = player.GetComponent<PlayerAnimator>();
            PlayerMotor motor = player.Motor;

            // Moderate speed -> Walk
            motor.VelocityX = 1.0f;
            yield return null;
            Assert.AreEqual(PlayerAnimationId.Walk, animator.CurrentAnimation,
                "Player moving at moderate speed (1.0) must transition to Walk.");

            // Fast sprint -> Run
            motor.VelocityX = 4.0f;
            yield return null;
            Assert.AreEqual(PlayerAnimationId.Run, animator.CurrentAnimation,
                "Player sprinting (>2.5) must transition to Run.");

            // Stopped -> Return to Idle (or RunStop)
            motor.VelocityX = 0f;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(PlayerAnimationId.Idle, animator.CurrentAnimation,
                "Settled player must return to Idle.");
        }

        [UnityTest]
        public IEnumerator PlayerTransitionsAirborneAnimationsBasedOnVerticalVelocity()
        {
            // Spawn airborne high up
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 10f));
            yield return null;

            PlayerAnimator animator = player.GetComponent<PlayerAnimator>();
            PlayerMotor motor = player.Motor;

            // Rising
            motor.VelocityY = 6.0f;
            yield return null;
            Assert.AreEqual(PlayerAnimationId.JumpRise, animator.CurrentAnimation,
                "Airborne player rising at +6.0 must play JumpRise.");

            // Apex
            motor.VelocityY = 0.5f;
            yield return null;
            Assert.AreEqual(PlayerAnimationId.JumpApex, animator.CurrentAnimation,
                "Airborne player at crest (|vy| <= 2) must play JumpApex.");

            // Falling
            motor.VelocityY = -6.0f;
            yield return null;
            Assert.AreEqual(PlayerAnimationId.JumpFall, animator.CurrentAnimation,
                "Airborne player falling at -6.0 must play JumpFall.");
        }

        [UnityTest]
        public IEnumerator PlayerTransitionsToAttackWhenCombatStarts()
        {
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();
            yield return null;

            PlayerAnimator animator = player.GetComponent<PlayerAnimator>();
            PlayerCombat combat = player.Combat;

            bool started = combat.TryStartAttack();
            Assert.IsTrue(started, "Melee attack should start successfully.");
            yield return null;

            Assert.AreEqual(PlayerAnimationId.Attack1, animator.CurrentAnimation,
                "Player must transition to Attack1 during attack execution.");
        }

        [UnityTest]
        public IEnumerator PlayerTransitionsToDeadOnDefeat()
        {
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();
            yield return null;

            PlayerAnimator animator = player.GetComponent<PlayerAnimator>();
            PlayerHealth health = player.Health;

            // Fatal damage
            health.TakeDamage(new DamageInfo(999f, Vector2.zero, 0f, 0f));
            yield return null;

            Assert.IsFalse(health.IsAlive, "Player must be dead.");
            Assert.AreEqual(PlayerAnimationId.Dead, animator.CurrentAnimation,
                "Defeated player must transition to Dead animation.");
        }

        [UnityTest]
        public IEnumerator FacingSignCorrectlyFlipsSpriteRenderer()
        {
            PlayerController player = SpawnTunedPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();
            yield return null;

            SpriteRenderer renderer = player.GetComponentInChildren<SpriteRenderer>();
            PlayerMotor motor = player.Motor;

            motor.FacingSign = 1;
            yield return null;
            Assert.IsFalse(renderer.flipX, "FacingSign +1 must have flipX == false.");

            motor.FacingSign = -1;
            yield return null;
            Assert.IsTrue(renderer.flipX, "FacingSign -1 must have flipX == true.");
        }
    }
}
