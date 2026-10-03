using System.Collections.Generic;
using Aether.Core.States;
using NUnit.Framework;

namespace Aether.Tests
{
    /// <summary>Unit tests for the shared state machine used by the player, enemies and the boss.</summary>
    public sealed class StateMachineTests
    {
        private enum TestState
        {
            Idle,
            Moving,
            Finished,
        }

        private sealed class TestStateBehaviour : IState<TestContext>
        {
            public readonly List<string> Log = new List<string>();
            public readonly string Label;

            public TestStateBehaviour(string label)
            {
                Label = label;
            }

            public void Enter(TestContext context) => Log.Add($"enter:{Label}");

            public void Tick(TestContext context, float deltaTime) => Log.Add($"tick:{Label}");

            public void FixedTick(TestContext context, float deltaTime) => Log.Add($"fixed:{Label}");

            public void Exit(TestContext context) => Log.Add($"exit:{Label}");
        }

        private sealed class TestContext
        {
        }

        private static StateMachine<TestContext, TestState> BuildMachine(
            out TestContext context,
            out TestStateBehaviour idle,
            out TestStateBehaviour moving,
            out TestStateBehaviour finished)
        {
            context = new TestContext();
            idle = new TestStateBehaviour("idle");
            moving = new TestStateBehaviour("moving");
            finished = new TestStateBehaviour("finished");

            var machine = new StateMachine<TestContext, TestState>(3);
            machine.Add(TestState.Idle, idle);
            machine.Add(TestState.Moving, moving);
            machine.Add(TestState.Finished, finished);
            return machine;
        }

        [Test]
        public void Start_EntersTheInitialState()
        {
            var machine = BuildMachine(out TestContext context, out var idle, out _, out _);

            machine.Start(context, TestState.Idle);

            Assert.That(machine.Current, Is.EqualTo(TestState.Idle));
            Assert.That(machine.IsRunning, Is.True);
            Assert.That(idle.Log, Has.Count.EqualTo(1));
            Assert.That(idle.Log[0], Is.EqualTo("enter:idle"));
        }

        [Test]
        public void ChangeState_ExitsThenEntersAndRaisesChanged()
        {
            var machine = BuildMachine(out TestContext context, out var idle, out var moving, out _);
            machine.Start(context, TestState.Idle);

            TestState from = TestState.Idle;
            TestState to = TestState.Idle;
            machine.Changed += (previous, current) =>
            {
                from = previous;
                to = current;
            };

            machine.ChangeState(context, TestState.Moving);

            Assert.That(idle.Log, Does.Contain("exit:idle"));
            Assert.That(moving.Log, Does.Contain("enter:moving"));
            Assert.That(from, Is.EqualTo(TestState.Idle));
            Assert.That(to, Is.EqualTo(TestState.Moving));
            Assert.That(machine.Previous, Is.EqualTo(TestState.Idle));
        }

        [Test]
        public void ChangeState_IgnoresSelfTransitions()
        {
            var machine = BuildMachine(out TestContext context, out var idle, out _, out _);
            machine.Start(context, TestState.Idle);

            machine.ChangeState(context, TestState.Idle);

            // Re-entering a state would restart its timers and replay its enter animation.
            Assert.That(idle.Log, Has.Count.EqualTo(1));
        }

        [Test]
        public void Tick_AccumulatesTimeInStateAndResetsOnTransition()
        {
            var machine = BuildMachine(out TestContext context, out _, out var moving, out _);
            machine.Start(context, TestState.Idle);

            machine.Tick(context, 0.25f);
            machine.Tick(context, 0.25f);
            Assert.That(machine.TimeInState, Is.EqualTo(0.5f).Within(0.0001f));

            machine.ChangeState(context, TestState.Moving);

            Assert.That(machine.TimeInState, Is.Zero);
            Assert.That(moving.Log, Does.Contain("enter:moving"));
        }

        [Test]
        public void TickAfterStop_DoesNotRunStateLogic()
        {
            var machine = BuildMachine(out TestContext context, out var idle, out _, out _);
            machine.Start(context, TestState.Idle);
            machine.Stop(context);

            int before = idle.Log.Count;
            machine.Tick(context, 1f);
            machine.FixedTick(context, 1f);

            Assert.That(machine.IsRunning, Is.False);
            Assert.That(idle.Log, Has.Count.EqualTo(before));
        }

        [Test]
        public void ChangeStateBeforeStart_IsIgnored()
        {
            var machine = BuildMachine(out TestContext context, out var idle, out _, out _);

            machine.ChangeState(context, TestState.Moving);

            Assert.That(machine.IsRunning, Is.False);
            Assert.That(idle.Log, Is.Empty);
        }

        [Test]
        public void Start_WithUnregisteredState_Throws()
        {
            var machine = new StateMachine<TestContext, TestState>(1);
            var context = new TestContext();

            Assert.Throws<KeyNotFoundException>(() => machine.Start(context, TestState.Moving));
        }

        [Test]
        public void Start_WhenAlreadyRunning_Throws()
        {
            var machine = BuildMachine(out TestContext context, out _, out _, out _);
            machine.Start(context, TestState.Idle);

            Assert.Throws<System.InvalidOperationException>(() => machine.Start(context, TestState.Moving));
        }
    }
}
