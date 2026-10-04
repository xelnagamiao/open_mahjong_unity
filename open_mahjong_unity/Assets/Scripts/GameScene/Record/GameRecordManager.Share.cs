using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public partial class GameRecordManager {
    [SerializeField] private Button copyRecordLinkButton;
    [SerializeField] private Button copyRecordPositionLinkButton;
    private string recordShareGameId;

    private bool CanShareRecord =>
        CurrentMode == RecordManagerMode.Record
        && !IsSpectating
        && !string.IsNullOrWhiteSpace(recordShareGameId)
        && !SharedRecordLink.IsLocalConvertedGameId(recordShareGameId);

    /// <summary>复用左侧信息按钮；已确认保存云端的牌谱（含本地副本）可显示分享按钮。</summary>
    private void InitializeRecordShareButtons() {
        if (showGameInfoButton == null) return;

        if (copyRecordLinkButton == null) {
            copyRecordLinkButton = CreateRecordShareButton("CopyRecordLink", "复制牌谱链接");
        }
        if (copyRecordPositionLinkButton == null) {
            copyRecordPositionLinkButton = CreateRecordShareButton("CopyRecordPositionLink", "复制场况链接");
        }
        BindRecordShareButton(copyRecordLinkButton, () => CopyRecordShareLink(false));
        BindRecordShareButton(copyRecordPositionLinkButton, () => CopyRecordShareLink(true));
        UpdateRecordShareButtonVisibility();
    }

    private Button CreateRecordShareButton(string objectName, string label) {
        GameObject buttonObject = Instantiate(
            showGameInfoButton.gameObject, showGameInfoButton.transform.parent, false);
        buttonObject.name = objectName;
        Button button = buttonObject.GetComponent<Button>();
        TMP_Text text = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (text != null) {
            text.text = label;
            text.enableAutoSizing = false;
            text.fontSize = 32f;
        }
        buttonObject.SetActive(false);
        return button;
    }

    private static void BindRecordShareButton(Button button, UnityAction onClick) {
        // 场景内的按钮与旧场景的模板副本都只绑定一次当前牌谱回调。
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(onClick);
    }

    private void UpdateRecordShareButtonVisibility() {
        bool visible = CanShareRecord;
        if (copyRecordLinkButton != null) copyRecordLinkButton.gameObject.SetActive(visible);
        if (copyRecordPositionLinkButton != null) copyRecordPositionLinkButton.gameObject.SetActive(visible);
    }

    private void CopyRecordShareLink(bool includePosition) {
        if (!CanShareRecord) return;

        string shareUrl = includePosition
            ? SharedRecordLink.BuildShareUrl(recordShareGameId, currentRoundIndex, currentNode)
            : SharedRecordLink.BuildShareUrl(recordShareGameId);
        ClipboardUtility.Copy(shareUrl);
        GameHost.Current.ShowTip("牌谱", true,
            includePosition ? "已复制目前局面牌谱链接" : "已复制本局牌谱链接");
    }
}
