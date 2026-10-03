#if UNITY_EDITOR
using System;
using TMPro;
using UnityEngine;

public partial class PlayerRoomPanel {
    public void BakeEmptySeatBots(UnityEngine.UI.Button autoTemplate, UnityEngine.UI.Button efficiencyTemplate,
        UnityEngine.UI.Button heuristicTemplate, bool moveOriginals = false) {
        if (!emptySeatBotControls) {
            emptySeatBotControls = new GameObject("EmptySeatBots", typeof(RectTransform));
            emptySeatBotControls.transform.SetParent(transform, false);
        }
        var container = (RectTransform)emptySeatBotControls.transform;
        container.anchorMin = container.anchorMax = container.pivot = new Vector2(.5f, .5f);
        container.anchoredPosition = new Vector2(0, 25);
        container.sizeDelta = new Vector2(290, 288);
        // Above the full-card player-info hit area, so clicks reach these buttons.
        container.SetAsLastSibling();
        addAutoBotButton = BakeSeatButton(addAutoBotButton, autoTemplate, "AddAutoBot", "添加摸切机器人", 112, moveOriginals);
        addEfficiencyBotButton = BakeSeatButton(addEfficiencyBotButton, efficiencyTemplate, "AddEfficiencyBot", "添加牌效机器人", 0, moveOriginals);
        addHeuristicBotButton = BakeSeatButton(addHeuristicBotButton, heuristicTemplate, "AddHeuristicBot", "添加高性能机器人", -112, moveOriginals);
        emptySeatBotControls.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void BakeSeatStatus(PlayerRoomPanel template) {
        if (!readyIcon && template.readyIcon) {
            readyIcon = Instantiate(template.readyIcon, transform, false);
            readyIcon.name = "SeatStatus";
            readyIcon.SetActive(false);
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    private UnityEngine.UI.Button BakeSeatButton(UnityEngine.UI.Button existing, UnityEngine.UI.Button template,
        string objectName, string label, float y, bool moveOriginal) {
        var button = existing ? existing : moveOriginal ? template : Instantiate(template);
        button.name = objectName;
        button.transform.SetParent(emptySeatBotControls.transform, false);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(284, 64);
        rect.localScale = Vector3.one;
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        button.interactable = true;
        button.gameObject.SetActive(true);
        var text = button.GetComponentInChildren<TMP_Text>(true);
        text.text = label;
        text.enableAutoSizing = false;
        text.fontSize = 27;
        return button;
    }

    public void ValidateEmptySeatBots() {
        if (!emptySeatBotControls || !addAutoBotButton || !addEfficiencyBotButton || !addHeuristicBotButton
            || !emptySeatBotControls.transform.IsChildOf(transform))
            throw new InvalidOperationException(name + " 空座位机器人按钮未烘焙。");
    }
}
#endif
