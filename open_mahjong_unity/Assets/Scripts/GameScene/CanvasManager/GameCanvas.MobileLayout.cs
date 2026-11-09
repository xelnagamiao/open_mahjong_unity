using UnityEngine;

public partial class GameCanvas {
    void EnsureMobileLayout() {
        if (!MobileViewportLayout.Enabled || !openStickerPanelButton || !stickerPanel) return;
        MobileGameUiLayout.Install(this,(RectTransform)openStickerPanelButton.transform,(RectTransform)stickerPanel.transform);
    }
}
