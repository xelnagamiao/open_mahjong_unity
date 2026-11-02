using UnityEngine;

namespace Salasasa.Home
{
    // Opt in only the buttons whose hover tint must not multiply their themed surface.
    [DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class AbsoluteButtonTint : MonoBehaviour { public bool useHoverTint = true; }
}
