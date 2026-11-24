using UnityEngine;

// 只在换六张展示创建时挂载，牌桌隐藏或编辑器重载时回收本玩法的临时对象。
public sealed class ChangshaClaimDisplayLifetime : MonoBehaviour {
    private Game3DManager owner;
    private void Awake() => owner = GetComponent<Game3DManager>();
    private void OnDisable() {
        if (owner != null) owner.ClearChangshaExchangeClaims();
    }
}
