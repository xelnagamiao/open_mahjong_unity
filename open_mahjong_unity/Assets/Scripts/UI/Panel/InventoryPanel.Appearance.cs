using Salasasa.Home;
using UnityEngine;

public sealed partial class InventoryPanel
{
    public void ApplyAppearance(InventoryPalette.Palette palette)
    {
        categorySurface = palette[InventoryPalette.Role.Category];
        categorySelectedSurface = palette[InventoryPalette.Role.Accent];
        categoryInk = palette[InventoryPalette.Role.SidebarInk];
        categorySelectedInk = palette[InventoryPalette.Role.OnAccent];
        rowTemplate.ApplyAppearance(palette);
        foreach (var row in rows) if (row) row.ApplyAppearance(palette);
        quantityInput.caretColor = palette[InventoryPalette.Role.Ink];
        var highlight = palette[InventoryPalette.Role.Accent]; highlight.a = .3f;
        quantityInput.selectionColor = highlight;
        if (redeemButton) {
            redeemButton.targetGraphic.color = categorySurface;
            redeemButton.GetComponentInChildren<TMPro.TMP_Text>(true).color = categoryInk;
        }
        UpdateControls();
    }
}
