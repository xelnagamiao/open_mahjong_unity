using UnityEngine;
using UnityEngine.UI;

// The portrait's UI geometry provides the bounds, frame corners and masking for both layers.
[DisallowMultipleComponent]
public sealed class AvatarPortraitBackground : MonoBehaviour {
    public const int ReimuItemId = 2302;
    public const int CirnoItemId = 2303;
    private static Material charcoal;
    private Material previousMaterial;

    public static void SetSprite(UnityEngine.UI.Image image, Sprite sprite, int itemId) {
        image.sprite = sprite;
        Apply(image, sprite ? itemId : 0);
    }

    public static void Apply(Graphic graphic, int itemId) {
        graphic.TryGetComponent<AvatarPortraitBackground>(out var background);
        if (itemId != ReimuItemId && itemId != CirnoItemId) {
            if (background && charcoal && graphic.material == charcoal)
                graphic.material = background.previousMaterial;
            return;
        }
        if (!charcoal) {
            var shader = Resources.Load<Shader>("Shaders/AvatarPortraitBackground");
            charcoal = new Material(shader) { name = "Avatar charcoal background", hideFlags = HideFlags.HideAndDontSave };
        }
        if (graphic.material == charcoal) return;
        if (!background) background = graphic.gameObject.AddComponent<AvatarPortraitBackground>();
        background.previousMaterial = graphic.material == graphic.defaultMaterial ? null : graphic.material;
        graphic.material = charcoal;
    }
}
