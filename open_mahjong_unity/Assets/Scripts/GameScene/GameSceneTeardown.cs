/// <summary>
/// 对局场景统一卸载：清理 3D 牌面、临时 UI、玩家信息面板与对局内存状态，还原到可再次进入的初始态。
/// </summary>
public static class GameSceneTeardown {
    public static void ResetToIdle() {
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.ClearRecordChongHintVisuals();
            GameRecordManager.Instance.HideRecordTips();
        }

        if (Game3DManager.Instance != null) {
            Game3DManager.Instance.StopAllRunningAnimations();
            Game3DManager.Instance.Clear3DTile();
        }

        if (GameSceneUIManager.Instance != null) {
            GameSceneUIManager.Instance.ClearTemporaryPanels();
        }

        if (AutoAction.Instance != null) {
            AutoAction.Instance.gameObject.SetActive(false);
        }

        if (BoardCanvas.Instance != null) {
            BoardCanvas.Instance.ResetForExit();
        }

        if (GameCanvas.Instance != null) {
            GameCanvas.Instance.ResetForExit();
        }

        if (NormalGameStateManager.Instance != null) {
            NormalGameStateManager.Instance.ResetForExit();
        }

        if (GameSceneMouseInputController.Instance != null) {
            GameSceneMouseInputController.Instance.SetState(GameSceneMouseInputController.StateIdle);
        }

        if (ExitButtonManager.Instance != null) {
            ExitButtonManager.Instance.HideAll();
        }

        if (RankChangePanel.Instance != null) {
            RankChangePanel.Instance.gameObject.SetActive(false);
        }
    }
}
