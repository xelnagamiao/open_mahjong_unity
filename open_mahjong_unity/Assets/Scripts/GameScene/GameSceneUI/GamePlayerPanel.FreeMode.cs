using UnityEngine;

public partial class GamePlayerPanel {
    public void SetFreeHandCount(int? count) {
        SetPlayerVariable(VariableHandCount, count.HasValue ? $"手牌 {Mathf.Max(0, count.Value)}" : null);
    }
}
