using Aether.Core.Combat;
using Aether.Data.Config;
using Aether.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Aether.Tests
{
    /// <summary>
    /// Tests for the attack timeline shared by the player and every enemy.
    /// </summary>
    /// <remarks>
    /// These exercise the timeline only. Hit resolution needs a populated physics world and is
    /// therefore a Play Mode concern, not something this fixture pretends to cover. The tests are
    /// still worth having because the timeline — wind-up, active window, recovery and combo
    /// chaining — is what the player actually reads, and it is easy to break silently.
    /// </remarks>
    public sealed class AttackRunnerTests
    {
        private AttackDefinition _attack;
        private AttackDefinition _followUp;

        [SetUp]
        public void SetUp()
        {
            _followUp = ScriptableObject.CreateInstance<AttackDefinition>();
            _followUp.hideFlags = HideFlags.HideAndDontSave;

            _attack = ScriptableObject.CreateInstance<AttackDefinition>();
            _attack.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_attack);
            Object.DestroyImmediate(_followUp);
        }

        /// <summary>
        /// Drives the runner forward. Uses the public Tick so the tests exercise the real path.
        /// </summary>
        private static void Advance(AttackRunner runner, float seconds, float step = 0.01f)
        {
            for (float t = 0f; t < seconds; t += step)
            {
                runner.Tick(step, Vector2.zero, 1, default, null, null);
            }
        }

        [Test]
        public void NewRunner_IsIdle()
        {
            var runner = new AttackRunner();

            Assert.That(runner.IsRunning, Is.False);
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.None));
            Assert.That(runner.Current, Is.Null);
        }

        [Test]
        public void RequestAttack_StartsInStartupPhase()
        {
            var runner = new AttackRunner();

            bool started = runner.RequestAttack(_attack);

            Assert.That(started, Is.True);
            Assert.That(runner.IsRunning, Is.True);
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.Startup));
        }

        [Test]
        public void RequestAttack_WithNull_DoesNothing()
        {
            var runner = new AttackRunner();

            Assert.That(runner.RequestAttack(null), Is.False);
            Assert.That(runner.IsRunning, Is.False);
        }

        [Test]
        public void Timeline_AdvancesStartupActiveRecoveryThenStops()
        {
            var runner = new AttackRunner();
            runner.RequestAttack(_attack);

            // Defaults are startup 0.09, active 0.07, recovery 0.17.
            Advance(runner, 0.10f);
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.Active), "expected the hitbox to be live");

            Advance(runner, 0.08f);
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.Recovery), "expected recovery");

            Advance(runner, 0.25f);
            Assert.That(runner.IsRunning, Is.False, "the attack should have finished");
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.None));
        }

        [Test]
        public void HitboxActivated_FiresExactlyOncePerAttack()
        {
            var runner = new AttackRunner();
            int activations = 0;
            runner.HitboxActivated += _ => activations++;

            runner.RequestAttack(_attack);
            Advance(runner, 0.5f);

            Assert.That(activations, Is.EqualTo(1),
                "a telegraph must resolve to exactly one active window, never two");
        }

        [Test]
        public void AttackFinished_FiresWhenTheAttackRunsOut()
        {
            var runner = new AttackRunner();
            int finished = 0;
            runner.AttackFinished += _ => finished++;

            runner.RequestAttack(_attack);
            Advance(runner, 0.5f);

            Assert.That(finished, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_FiresFinishedOnceAndIsIdempotent()
        {
            var runner = new AttackRunner();
            int finished = 0;
            runner.AttackFinished += _ => finished++;

            runner.RequestAttack(_attack);
            runner.Cancel();
            runner.Cancel();

            Assert.That(runner.IsRunning, Is.False);
            Assert.That(finished, Is.EqualTo(1), "cancelling twice must not report two attacks");
        }

        [Test]
        public void StartupProgress_TracksTheWindUp()
        {
            var runner = new AttackRunner();
            runner.RequestAttack(_attack);

            Assert.That(runner.StartupProgress, Is.EqualTo(0f).Within(0.001f));

            Advance(runner, 0.045f);

            Assert.That(runner.StartupProgress, Is.GreaterThan(0.3f));
            Assert.That(runner.StartupProgress, Is.LessThan(0.7f));
        }

        [Test]
        public void GetHitboxCenter_MirrorsWithFacing()
        {
            var runner = new AttackRunner();
            runner.RequestAttack(_attack);

            var origin = new Vector2(10f, 5f);

            Vector2 right = runner.GetHitboxCenter(origin, 1);
            Vector2 left = runner.GetHitboxCenter(origin, -1);

            Assert.That(right.y, Is.EqualTo(left.y).Within(0.0001f),
                "facing must only affect the horizontal offset");
            Assert.That(right.x, Is.GreaterThan(origin.x));
            Assert.That(left.x, Is.LessThan(origin.x));
        }

        [Test]
        public void ComboInputDuringRecovery_ChainsIntoTheFollowUp()
        {
            // Wire the chain explicitly so the test does not depend on serialised defaults.
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            var followUp = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.hideFlags = followUp.hideFlags = HideFlags.HideAndDontSave;

            SetPrivateField(attack, "_nextInCombo", followUp);
            SetPrivateField(attack, "_startup", 0.05f);
            SetPrivateField(attack, "_active", 0.05f);
            SetPrivateField(attack, "_recovery", 0.40f);
            SetPrivateField(attack, "_comboInputWindow", 0.30f);

            var runner = new AttackRunner();
            runner.RequestAttack(attack);

            // Advance into recovery, then request again.
            Advance(runner, 0.12f);
            Assert.That(runner.Phase, Is.EqualTo(AttackPhase.Recovery));

            runner.RequestAttack(attack);
            Advance(runner, 0.02f);

            Assert.That(runner.Current, Is.SameAs(followUp),
                "a queued input inside the combo window must chain, not be dropped");

            Object.DestroyImmediate(attack);
            Object.DestroyImmediate(followUp);
        }

        [Test]
        public void ComboInputAfterTheWindow_DoesNotChain()
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            var followUp = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.hideFlags = followUp.hideFlags = HideFlags.HideAndDontSave;

            SetPrivateField(attack, "_nextInCombo", followUp);
            SetPrivateField(attack, "_startup", 0.05f);
            SetPrivateField(attack, "_active", 0.05f);
            SetPrivateField(attack, "_recovery", 0.40f);
            SetPrivateField(attack, "_comboInputWindow", 0.05f);

            var runner = new AttackRunner();
            runner.RequestAttack(attack);

            // Past the combo window.
            Advance(runner, 0.25f);
            runner.RequestAttack(attack);
            Advance(runner, 0.02f);

            Assert.That(runner.Current, Is.SameAs(attack),
                "a late input must be ignored; accepting it would make the combo unbounded");

            Object.DestroyImmediate(attack);
            Object.DestroyImmediate(followUp);
        }

        /// <summary>
        /// Writes a private serialised field. Used because these fields are intentionally
        /// inspector-authored and have no public setter.
        /// </summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
