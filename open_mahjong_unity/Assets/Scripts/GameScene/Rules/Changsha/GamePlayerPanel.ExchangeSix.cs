using TMPro;
using UnityEngine;

public partial class GamePlayerPanel {
    private GameObject exchangeSixDeclarationBadge;
    private TMP_Text exchangeSixDeclarationText;

    public void ShowExchangeSixDeclaration(string caption) {
        if (exchangeSixDeclarationBadge == null && !string.IsNullOrEmpty(caption)) {
            if (playerLangyongBadge != null) {
                exchangeSixDeclarationBadge = Instantiate(playerLangyongBadge, playerLangyongBadge.transform.parent);
                exchangeSixDeclarationText = exchangeSixDeclarationBadge.GetComponentInChildren<TMP_Text>(true);
            } else {
                exchangeSixDeclarationText = Instantiate(playerNameText, playerNameText.transform.parent);
                exchangeSixDeclarationBadge = exchangeSixDeclarationText.gameObject;
                exchangeSixDeclarationText.rectTransform.anchoredPosition += new Vector2(0, -28);
            }
            exchangeSixDeclarationBadge.name = "ChangshaExchangeSixDeclaration";
            if (exchangeSixDeclarationText != null) exchangeSixDeclarationText.raycastTarget = false;
        }
        if (exchangeSixDeclarationBadge == null) return;
        exchangeSixDeclarationBadge.SetActive(!string.IsNullOrEmpty(caption));
        if (exchangeSixDeclarationText != null) exchangeSixDeclarationText.text = caption;
    }
}
