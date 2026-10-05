using UnityEngine;

namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// A scene that owns what happens after it loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AetherBoot"/> starts a region automatically in any scene that does not claim
    /// responsibility for itself, and that is what makes a fresh clone playable by pressing play.
    /// The rule needs a way to be answered "no, this scene has its own plans", and this class is
    /// that answer: a scene containing a <see cref="SceneFlowOwner"/> decides for itself what
    /// happens next, and the automatic boot stands down.
    /// </para>
    /// <para>
    /// The studio intro is the first owner — it hands over to the next scene when it ends. The main
    /// menu will be the second — it stays on screen until the player chooses to leave. Neither has
    /// to be known by name to the boot, which is the point: adding a scene in front of the game
    /// must not mean editing the thing that starts the game.
    /// </para>
    /// <para>
    /// This is deliberately an empty base class rather than an interface. A scene owner is a
    /// component, and <c>FindAnyObjectByType</c> finds components; an interface would force the
    /// search through every MonoBehaviour in the scene or a second registration path, for a marker
    /// that has no methods.
    /// </para>
    /// </remarks>
    public abstract class SceneFlowOwner : MonoBehaviour
    {
    }
}
