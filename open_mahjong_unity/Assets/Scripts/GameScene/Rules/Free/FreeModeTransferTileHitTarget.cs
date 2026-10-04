using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>仅覆盖中央共享牌牌顶的点击面，同时供全局出牌快捷键识别并避让。</summary>
public sealed class FreeModeTransferTileHitTarget : MonoBehaviour, IPointerClickHandler {
    private FreeModeTransferTile owner;

    public void Bind(FreeModeTransferTile value) => owner = value;

    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button == PointerEventData.InputButton.Left && owner != null) owner.TakeTile();
    }
}
