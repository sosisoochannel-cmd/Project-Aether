using System.Collections;
using System.Reflection;
using Aether.Data.Config;
using Aether.Gameplay;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Player;
using Aether.Gameplay.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Validates player physics, coyote time, jump buffering, jump cut arcs, and state events.
    /// </summary>
    public sealed class PlayerMovementTests
    {
        private GameObject _world;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AetherBoot.AutoBootEnabled = false;
            var boot = Object.FindObjectsByType<LevelBootstrap>(FindObjectsSortMode.None);
            for (int i = 0; i < boot.Length; i++)
            {
                if (boot[i] != null) Object.DestroyImmediate(boot[i].gameObject);
            }
            Physics2D.SyncTransforms();

            _world = new GameObject("PlayerMovementTests World");
            CreateGround(new Vector2(0f, -0.5f), new Vector2(20f, 1f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AetherBoot.AutoBootEnabled = true;
            if (_world != null) Object.DestroyImmediate(_world);
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

        private static PlayerTuningData LoadTuning()
        {
            PlayerTuningData tuning = Resources.Load<PlayerTuningData>("Content/PlayerTuning");
            Assert.IsNotNull(tuning, "Content/PlayerTuning asset is required for movement tests.");
            return tuning;
        }

        private PlayerController SpawnPlayer(Vector2 position)
        {
            return PlayerFactory.Create(
                position,
                LoadTuning(),
                Resources.Load<AttackDefinition>("Content/Attack.Strike"),
                _world.transform);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
            return field.GetValue(target);
        }

        private static object InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
            return method.Invoke(target, args);
        }

        /// <summary>
        /// Cutting a jump early must apply the cut multiplier once rather than repeatedly multiplying
        /// vertical velocity on every physics step until it vanishes.
        /// </summary>
        [UnityTest]
        public IEnumerator JumpCut_AppliedOnce_DoesNotDecayIndefinitely()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            PlayerTuningData tuning = player.Tuning;
            player.Motor.StartJump(tuning);
            float initialVy = player.Motor.VelocityY;
            Assert.AreEqual(tuning.JumpVelocity, initialVy, 0.001f);

            // Buffer jump held as false so jump cut applies
            SetPrivateField(player, "_jumpCutApplied", false);
            InvokePrivateMethod(player, "HandleJumpCut");

            float afterFirstCut = player.Motor.VelocityY;
            float expectedCutVy = initialVy * tuning.JumpCutMultiplier;
            Assert.AreEqual(expectedCutVy, afterFirstCut, 0.01f,
                "First jump cut must scale velocity by JumpCutMultiplier.");

            // A second call within the same jump arc must NOT cut again
            InvokePrivateMethod(player, "HandleJumpCut");
            float afterSecondCall = player.Motor.VelocityY;
            Assert.AreEqual(afterFirstCut, afterSecondCall, 0.001f,
                "Subsequent calls must not apply repeated cuts to vertical velocity.");
        }

        /// <summary>
        /// A jump taken from the ground must clear the coyote window so a rapid second press does
        /// not produce an unauthorized second ground jump in mid-air.
        /// </summary>
        [UnityTest]
        public IEnumerator JumpFromGround_ClearsCoyoteWindow()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            // Simulate grounded timestamp
            SetPrivateField(player, "_lastGroundedAt", Time.time);
            SetPrivateField(player, "_jumpBufferedAt", Time.time);

            bool jumped = (bool)InvokePrivateMethod(player, "TryJump");
            Assert.IsTrue(jumped, "First jump from ground must succeed.");

            float lastGroundedAt = (float)GetPrivateField(player, "_lastGroundedAt");
            Assert.AreEqual(float.NegativeInfinity, lastGroundedAt,
                "Jump take-off must clear _lastGroundedAt so coyote time cannot be abused in mid-air.");

            // Another buffered jump immediately after take-off should fail because air jumps are 0
            SetPrivateField(player, "_jumpBufferedAt", Time.time);
            bool secondJump = (bool)InvokePrivateMethod(player, "TryJump");
            Assert.IsFalse(secondJump,
                "Without air jumps and with coyote time cleared, a second jump in mid-air must be rejected.");
        }

        /// <summary>
        /// Jump buffering must remember a press made within the buffer window and execute when grounded.
        /// </summary>
        [UnityTest]
        public IEnumerator JumpBuffering_FiresWhenGrounded()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            // In mid-air with coyote expired
            SetPrivateField(player, "_lastGroundedAt", float.NegativeInfinity);
            SetPrivateField(player, "_jumpBufferedAt", Time.time);

            // Cannot jump yet while airborne without air jumps
            bool jumpedInAir = (bool)InvokePrivateMethod(player, "TryJump");
            Assert.IsFalse(jumpedInAir, "Cannot jump while airborne without air jumps.");

            // Buffer still valid; now ground the player
            SetPrivateField(player, "_lastGroundedAt", Time.time);
            bool jumpedOnTouchdown = (bool)InvokePrivateMethod(player, "TryJump");
            Assert.IsTrue(jumpedOnTouchdown,
                "Buffered jump must execute once the player touches ground.");
        }

        /// <summary>
        /// LeftGround and Landed events must fire cleanly on state transitions without spamming.
        /// </summary>
        [UnityTest]
        public IEnumerator StateEvents_FireExactlyOncePerTransition()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            int leftGroundCount = 0;
            int landedCount = 0;
            player.LeftGround += () => leftGroundCount++;
            player.Landed += () => landedCount++;

            // Ensure player is Grounded
            var machine = (Aether.Core.States.StateMachine<PlayerController, PlayerStateId>)
                GetPrivateField(player, "_machine");
            if (player.State != PlayerStateId.Grounded)
            {
                machine.ChangeState(player, PlayerStateId.Grounded);
            }
            leftGroundCount = 0;
            landedCount = 0;

            // Grounded -> Airborne (take off)
            machine.ChangeState(player, PlayerStateId.Airborne);

            Assert.AreEqual(1, leftGroundCount, "LeftGround must fire exactly once on leaving ground.");
            Assert.AreEqual(0, landedCount, "Landed must not fire while airborne.");

            // Land: Airborne -> Grounded
            machine.ChangeState(player, PlayerStateId.Grounded);

            Assert.AreEqual(1, leftGroundCount, "LeftGround must remain 1 after landing.");
            Assert.AreEqual(1, landedCount, "Landed must fire exactly once on touchdown.");
        }

        /// <summary>
        /// Dodging and attacking must be rejected while recovering from damage (Hurt state).
        /// </summary>
        [UnityTest]
        public IEnumerator HurtState_LocksOutDodgeAndAttack()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            player.EnterHurt(0.3f);
            Assert.AreEqual(PlayerStateId.Hurt, player.State);

            // Attempt to dodge during hurt
            InvokePrivateMethod(player, "TryStartDodge");
            Assert.AreEqual(PlayerStateId.Hurt, player.State,
                "Player must not escape Hurt state by dodging.");

            // Attempt to attack during hurt
            bool attackStarted = player.Combat.TryStartAttack();
            Assert.IsFalse(attackStarted,
                "Player must not attack while in Hurt state.");
            Assert.AreEqual(PlayerStateId.Hurt, player.State);
        }
    }
}
