using System.Collections;
using System.Reflection;
using Aether.Core.Combat;
using Aether.Data.Config;
using Aether.Gameplay.Combat;
using Aether.Gameplay.Enemies;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Player;
using Aether.Gameplay.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Validates melee combat execution, combo lunges, hit reactions, and enemy AI awareness.
    /// </summary>
    public sealed class CombatPipelineTests
    {
        private GameObject _world;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _world = new GameObject("CombatPipelineTests World");
            CreateGround(new Vector2(0f, -0.6f), new Vector2(20f, 1f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        private PlayerController SpawnPlayer(Vector2 position)
        {
            PlayerTuningData tuning = Resources.Load<PlayerTuningData>("Content/PlayerTuning");
            AttackDefinition attack = Resources.Load<AttackDefinition>("Content/Attack.Strike");
            return PlayerFactory.Create(position, tuning, attack, _world.transform);
        }

        private EnemyController SpawnEnemy(Vector2 feet)
        {
            EnemyDefinition definition = null;
            EnemyDefinition[] all = Resources.LoadAll<EnemyDefinition>("Content/Enemies");
            if (all != null && all.Length > 0)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].TypeId == "forest_stalker")
                    {
                        definition = all[i];
                        break;
                    }
                }
                if (definition == null && all.Length > 0) definition = all[0];
            }

            Assert.IsNotNull(definition, "An EnemyDefinition is required to spawn an enemy.");
            return EnemyFactory.Create(definition, feet, _world.transform, 3);
        }

        /// <summary>
        /// Initial attack and chained combo follow-up both apply their authored lunge velocities.
        /// </summary>
        [UnityTest]
        public IEnumerator CombatComboChain_AppliesFollowUpLunge()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            yield return new WaitForFixedUpdate();

            AttackDefinition strike = Resources.Load<AttackDefinition>("Content/Attack.Strike");
            Assert.IsNotNull(strike.NextInCombo, "Strike must have a follow-up in combo.");

            // Start first attack while grounded
            bool firstStarted = player.Combat.TryStartAttack();
            Assert.IsTrue(firstStarted, "First strike must start.");
            Assert.AreEqual(strike.LungeSpeed, player.Motor.VelocityX, 0.01f,
                "First strike must apply initial lunge impulse.");

            // Advance through Startup and Active into Recovery
            float advance = strike.Startup + strike.Active + 0.01f;
            player.Combat.Tick(advance);
            Assert.AreEqual(AttackPhase.Recovery, player.Combat.Phase);

            // Queue the combo follow-up
            player.Combat.TryStartAttack();

            // Next tick in recovery will trigger Begin(NextInCombo)
            player.Combat.Tick(0.01f);

            Assert.AreEqual(strike.NextInCombo.Id, player.Combat.CurrentAttack.Id,
                "AttackRunner must chain into the follow-up attack.");
            Assert.AreEqual(strike.NextInCombo.LungeSpeed, player.Motor.VelocityX, 0.01f,
                "Chained follow-up attack must apply its own authored lunge impulse.");
        }

        /// <summary>
        /// When an enemy takes damage, vertical lift speed is applied along with horizontal knockback.
        /// </summary>
        [UnityTest]
        public IEnumerator EnemyDamage_AppliesVerticalLiftSpeed()
        {
            EnemyController enemy = SpawnEnemy(new Vector2(5f, 0f));
            yield return new WaitForFixedUpdate();

            var damage = new DamageInfo(
                1f,
                new Vector2(3f, 0f), // Hit from the left
                6f,                  // Knockback speed
                2.5f,                // Lift speed
                DamageKind.Melee,
                null);

            enemy.Health.TakeDamage(damage);

            Assert.Greater(enemy.Motor.VelocityX, 0f, "Enemy must be knocked back away from hit origin.");
            Assert.AreEqual(2.5f, enemy.Motor.VelocityY, 0.01f,
                "Enemy must receive the authored lift speed upward pop.");
        }

        /// <summary>
        /// Enemies receive the player target from the director, enabling line-of-sight and perception.
        /// </summary>
        [UnityTest]
        public IEnumerator Enemy_ReceivesPlayerTarget_CanPerceivePlayer()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            EnemyController enemy = SpawnEnemy(new Vector2(2f, 0f));
            yield return new WaitForFixedUpdate();

            enemy.SetTarget(player.transform);
            enemy.Motor.FacingSign = -1; // Face left towards player at x = 0

            MethodInfo canSee = enemy.GetType().GetMethod("CanSeePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(canSee);

            bool spotted = (bool)canSee.Invoke(enemy, null);
            Assert.IsTrue(spotted, "Enemy facing the player within detection range must see the player.");
        }

        /// <summary>
        /// In ChaseState, when within attack range, enemy brakes to settle before attacking
        /// instead of continuously running through the player.
        /// </summary>
        [UnityTest]
        public IEnumerator Enemy_ChaseBrakesWhenInAttackRange()
        {
            PlayerController player = SpawnPlayer(new Vector2(0f, 0.7f));
            EnemyController enemy = SpawnEnemy(new Vector2(1f, 0f));
            yield return new WaitForFixedUpdate();

            enemy.SetTarget(player.transform);
            enemy.Motor.FacingSign = -1;
            enemy.Motor.VelocityX = -enemy.Definition.MoveSpeed; // Running at full speed

            // Trigger FixedTick of ChaseState
            var machine = (Aether.Core.States.StateMachine<EnemyController, EnemyStateId>)
                typeof(EnemyController).GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
            machine.ChangeState(enemy, EnemyStateId.Chase);

            machine.FixedTick(enemy, 0.02f);

            Assert.Less(Mathf.Abs(enemy.Motor.VelocityX), enemy.Definition.MoveSpeed,
                "Enemy in attack range must decelerate towards a stop to settle before attacking.");
        }
    }
}
