using UnityEngine;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// Everything gameplay is allowed to know about input.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyboard, gamepad and touch all produce this same small vocabulary, and the player controller
    /// only ever sees this interface. That is the point: a second input path added later (a
    /// controller with paddles, a replay system, an automated test) plugs in as another source
    /// instead of a second set of conditionals inside gameplay.
    /// </para>
    /// <para>
    /// <b>The button members are press flags, not held states.</b> A press stays latched until
    /// <see cref="ConsumeJump"/>, <see cref="ConsumeAttack"/>, <see cref="ConsumeDodge"/> or
    /// <see cref="ConsumeInteract"/> clears it, so a press seen in a frame with two physics steps
    /// cannot be acted on twice, and a press seen in <c>Update</c> cannot be missed by a physics step
    /// that runs later.
    /// </para>
    /// </remarks>
    public interface IGameplayInput
    {
        /// <summary>Movement axis, normalised to at most unit length.</summary>
        Vector2 Move { get; }

        /// <summary>True while the jump control is held. Drives variable jump height.</summary>
        bool JumpHeld { get; }

        /// <summary>Latched jump press.</summary>
        bool JumpPressed { get; }

        /// <summary>Latched attack press.</summary>
        bool AttackPressed { get; }

        /// <summary>Latched dodge press.</summary>
        bool DodgePressed { get; }

        /// <summary>Latched interact press.</summary>
        bool InteractPressed { get; }

        /// <summary>When false the source reports nothing at all.</summary>
        bool Enabled { get; set; }

        /// <summary>Clears the jump press.</summary>
        void ConsumeJump();

        /// <summary>Clears the attack press.</summary>
        void ConsumeAttack();

        /// <summary>Clears the dodge press.</summary>
        void ConsumeDodge();

        /// <summary>Clears the interact press.</summary>
        void ConsumeInteract();
    }
}
