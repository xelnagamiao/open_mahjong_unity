using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>整组副露收回预览，四个固定牌槽由预制体提供。</summary>
public sealed class FreeModeMeldRow : MonoBehaviour {
    public Text title;
    public Button recallButton;
    public FreeModeTileView[] tiles;
    public void Bind(int[] mask, int number, UnityAction recall) {
        title.text = "副露 " + number;
        recallButton.onClick.RemoveAllListeners();
        recallButton.onClick.AddListener(recall);
        int count = mask == null ? 0 : mask.Length / 2;
        for (int i = 0; i < tiles.Length; i++) {
            tiles[i].gameObject.SetActive(i < count);
            if (i < count) tiles[i].Bind(mask[i * 2 + 1], mask[i * 2]);
        }
    }
}
