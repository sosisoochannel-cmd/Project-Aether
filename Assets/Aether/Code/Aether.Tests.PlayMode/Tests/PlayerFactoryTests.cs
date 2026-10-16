using System.Collections;
using System.Text.RegularExpressions;
using Aether.Core.Combat;
using Aether.Data.Config;
using Aether.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Builds the player the way the game builds it and checks the wiring the movement loop assumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These live in PlayMode because the fault they guard is a lifecycle fault. Unity satisfies
    /// <c>[RequireComponent]</c> by <b>adding</b> the missing component to the object, and the added
    /// component awakes immediately when the object is active — before the factory can hand it a
    /// tuning asset. In edit mode no component awakes, so the fault cannot appear there and a test
    /// there would prove nothing.
    /// </para>
    /// <para>
    /// The numbers checked here are deliberately structural (how many components, which instance,
    /// initialized or not) rather than gameplay figures, which belong to the tuning asset.
    /// </para>
    /// </remarks>
    public sealed class PlayerFactoryTests
    {
        private GameObject _world;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _world = new GameObject("PlayerFactoryTests world");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null) Object.Destroy(_world);
            _world = null;
            yield return null;
        }

        private static PlayerTuningData LoadTuning()
        {
            PlayerTuningData tuning = Resources.Load<PlayerTuningData>("Content/PlayerTuning");
            Assert.IsNotNull(tuning,
                "Content/PlayerTuning is missing, so the shipped game cannot build a playable player.");
            return tuning;
        }

        private PlayerController SpawnTunedPlayer()
        {
            return PlayerFactory.Create(
                Vector2.zero,
                LoadTuning(),
                Resources.Load<AttackDefinition>("Content/Attack.Strike"),
                _world.transform);
        }

        /// <summary>
        /// A factory-built player carries one controller and one health, and the controller it returns
        /// is the live one.
        /// </summary>
        [UnityTest]
        public IEnumerator FactoryPlayerHasExactlyOneOfEachPart()
        {
            PlayerController controller = SpawnTunedPlayer();
            yield return null;

            GameObject host = controller.gameObject;
            Assert.AreEqual(1, host.GetComponents<PlayerController>().Length,
                "A player with two controllers moves twice, and the second one has no tuning asset.");
            Assert.AreEqual(1, host.GetComponents<PlayerHealth>().Length,
                "A player with two health components takes every hit at least once against the wrong one.");
            Assert.AreEqual(1, host.GetComponents<PlayerCombat>().Length,
                "A player with two combat components can run two attack timelines at once.");
            Assert.AreEqual(1, host.GetComponents<PlayerMotor>().Length,
                "A player with two motors has two sources of truth for its velocity.");
            Assert.AreEqual(1, host.GetComponents<PlayerAnimator>().Length,
                "A player with two animators would fight over sprite playback.");
            Assert.AreSame(controller, host.GetComponent<PlayerController>(),
                "The controller the factory returned must be the one on the object.");
            Assert.IsTrue(host.activeInHierarchy,
                "The factory must return the player already switched on.");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The player the factory returns is initialized with the tuning asset it was given.
        /// </summary>
        [UnityTest]
        public IEnumerator FactoryPlayerIsInitializedWithItsTuningAsset()
        {
            PlayerTuningData tuning = LoadTuning();
            PlayerController controller = PlayerFactory.Create(
                Vector2.zero, tuning, Resources.Load<AttackDefinition>("Content/Attack.Strike"),
                _world.transform);
            yield return null;

            Assert.IsTrue(controller.IsInitialized,
                "The controller must report itself initialized; it disables itself otherwise.");
            Assert.IsTrue(controller.enabled,
                "A player built with a tuning asset must be running.");
            Assert.AreSame(tuning, controller.Tuning,
                "The controller must hold the exact tuning asset the factory was given.");
            Assert.AreSame(controller.Health, controller.gameObject.GetComponent<PlayerHealth>(),
                "The controller must be wired to the health on its own object.");
            Assert.Greater(tuning.CoyoteTime, 0f,
                "The shipped tuning asset must define a coyote window, or the first FixedUpdate has " +
                "nothing to compare against.");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Adding the player's parts does not silently create a controller behind the factory's back.
        /// </summary>
        /// <remarks>
        /// This is the root cause of the shipped PlayMode failure, written as a test. The components are
        /// added in the order that used to exist (combat and health before the controller) precisely
        /// because a <c>RequireComponent</c> pointing back at the controller was what created a second,
        /// untuned one. The object is inactive, and this still holds: Unity applies component
        /// requirements when the component is added, not when the object awakes.
        /// </remarks>
        [Test]
        public void PlayerPartsDoNotAutoCreateAController()
        {
            var host = new GameObject("Player parts, added without a controller");
            host.SetActive(false);
            host.AddComponent<Rigidbody2D>();
            host.AddComponent<BoxCollider2D>();
            host.AddComponent<PlayerMotor>();
            host.AddComponent<PlayerCombat>();
            host.AddComponent<PlayerHealth>();

            Assert.IsNotNull(host.GetComponent<PlayerHealth>(),
                "The part under test was not added, so this test proves nothing.");
            Assert.IsNull(host.GetComponent<PlayerController>(),
                "A player part must not require the controller: Unity would add a controller here that " +
                "never receives a tuning asset, and it throws once per physics step.");

            Object.Destroy(host);
        }

        /// <summary>
        /// A controller with no tuning asset switches itself off and never throws.
        /// </summary>
        /// <remarks>
        /// The previous behaviour logged the missing asset and kept running, so every physics step threw
        /// a <c>NullReferenceException</c> from <c>FixedUpdate</c>, hid the real message and stopped the
        /// state machine. Unexpected errors — which is what that exception was — fail this test.
        /// </remarks>
        [UnityTest]
        public IEnumerator ControllerWithoutTuningDisablesItselfInsteadOfThrowing()
        {
            var host = new GameObject("Player without tuning");
            host.SetActive(false);
            host.AddComponent<Rigidbody2D>();
            host.AddComponent<BoxCollider2D>();
            host.AddComponent<PlayerMotor>();
            host.AddComponent<PlayerHealth>();
            host.AddComponent<PlayerCombat>();
            host.AddComponent<PlayerInputReader>();
            PlayerController controller = host.AddComponent<PlayerController>();

            LogAssert.Expect(LogType.Error, new Regex(
                "PlayerController on 'Player without tuning' has no PlayerTuningData assigned"));

            host.SetActive(true);
            yield return null;

            Assert.IsFalse(controller.IsInitialized,
                "A controller with no tuning asset must not claim to be initialized.");
            Assert.IsFalse(controller.enabled,
                "A controller with no tuning asset must switch itself off rather than throw every step.");

            // Several fixed steps: the old code threw on each one, so an unchanged regression appears
            // as repeated exceptions here.
            for (int step = 0; step < 5; step++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(controller.enabled,
                "The controller must stay switched off for as long as it has no tuning asset.");
            Assert.AreEqual(1, host.GetComponents<PlayerController>().Length,
                "Nothing may add a second controller while the player is failing safely.");
            LogAssert.NoUnexpectedReceived();

            Object.Destroy(host);
        }

        /// <summary>
        /// Health works on an object that has no controller, instead of throwing on the hit reaction.
        /// </summary>
        /// <remarks>
        /// <see cref="PlayerHealth"/> used to require a controller and dereference it unguarded while
        /// applying knockback, so removing the requirement without guarding that call would turn a
        /// configuration mistake into an exception inside damage handling.
        /// </remarks>
        [UnityTest]
        public IEnumerator HealthWithoutControllerStillAppliesDamage()
        {
            var host = new GameObject("Damageable body without a controller");
            host.SetActive(false);
            host.AddComponent<Rigidbody2D>();
            host.AddComponent<BoxCollider2D>();
            host.AddComponent<PlayerMotor>();
            PlayerHealth health = host.AddComponent<PlayerHealth>();
            host.SetActive(true);
            yield return null;

            float before = health.Current;
            // One point, so the hit is survivable and the reaction path (the guarded controller call)
            // is the one being exercised rather than the death path.
            health.TakeDamage(new DamageInfo(1f, Vector2.right * 4f, 3f, 1f));
            yield return new WaitForFixedUpdate();

            Assert.Less(health.Current, before,
                "Damage must land whether or not the object has a controller.");
            Assert.IsTrue(health.IsAlive,
                "A single point of damage must not kill a full-health player.");
            LogAssert.NoUnexpectedReceived();

            Object.Destroy(host);
        }
    }
}
