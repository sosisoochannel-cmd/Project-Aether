using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// The one event system the game uses, wherever the interface is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project runs on the Input System alone (<c>activeInputHandler: 2</c>), so a scene without
    /// an <see cref="InputSystemUIInputModule"/> has buttons that draw, highlight and cannot be
    /// pressed. Both scenes that carry interface — the menu and the region — need the same object,
    /// and a scene loaded on its own does not inherit the other's: the menu's event system is built
    /// with the menu scene and dies with it.
    /// </para>
    /// <para>
    /// This used to be a private method on the menu root. It moved here when the region needed it
    /// too, because two copies of "make an event system and make it speak the Input System" is two
    /// places for the legacy module to come back, and the failure it causes — no button anywhere
    /// working, on a device only — is not one worth discovering twice.
    /// </para>
    /// </remarks>
    public static class UiEventSystem
    {
        /// <summary>Finds the scene's event system, or builds one, and returns it.</summary>
        public static EventSystem Ensure()
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                var host = new GameObject("Event System");
                events = host.AddComponent<EventSystem>();
            }

            // A legacy module in the same scene would fight this one for every event, and on an
            // Input System Only project it also throws the first time it reads a key.
            StandaloneInputModule legacy = events.GetComponent<StandaloneInputModule>();
            if (legacy != null) Object.Destroy(legacy);

            if (events.GetComponent<InputSystemUIInputModule>() == null)
            {
                events.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            return events;
        }
    }
}
