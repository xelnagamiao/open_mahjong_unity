using UnityEngine;
using UnityEngine.UI;

/// <summary>One supplied sprite per icon; no additional legacy icon is drawn underneath.</summary>
[ExecuteAlways, RequireComponent(typeof(Image))]
public sealed class TableHudSourceGraphic : MonoBehaviour {
    public bool whiteLabel;
    public bool glyph;
    public bool transportBackdrop;
    public bool utilityIcon;
    public void Apply(TableHudTheme.Style style, Material material) {
        if (!TableHudTheme.IsDesignTarget(transform)) return;
        var image = GetComponent<Image>();
        image.enabled = true;
        image.material = utilityIcon ? material : null;
        // The supplied arrow already includes its white gradient and dark outline.
        image.color = Color.white;
    }
}
