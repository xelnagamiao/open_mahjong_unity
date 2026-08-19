using System.Collections.Generic;

public partial class NormalGameStateManager {
    readonly HashSet<int> mutedStickerUserIds = new HashSet<int>();

    public bool IsStickerMuted(int userId) {
        return userId >= 10 && mutedStickerUserIds.Contains(userId);
    }

    /// <summary>切换本场表情屏蔽。返回切换后是否处于屏蔽状态。</summary>
    public bool ToggleStickerMute(int userId) {
        if (userId < 10) return false;
        if (mutedStickerUserIds.Remove(userId)) return false;
        mutedStickerUserIds.Add(userId);
        return true;
    }

    public void ClearStickerMutes() {
        mutedStickerUserIds.Clear();
    }
}
