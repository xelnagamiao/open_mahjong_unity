#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class RoomPanel {
    public void BakeBotSpeedUi() {
        RemoveLegacyClaimProtectionControl();
        if (!hostNameText) hostNameText = transform.Find("HostName")?.GetComponent<TMP_Text>();
        if (hostNameText) {
            hostNameText.transform.SetParent(playerPanel1.transform, true);
            hostNameText.raycastTarget = false;
        }
        // Keep the room's existing robot button styling and serialize it in each empty seat.
        playerPanel2.BakeEmptySeatBots(addBotButton, addSmartBotButton, addGuobiaoHeuristicBotButton, true);
        playerPanel1.BakeEmptySeatBots(addBotButton, addSmartBotButton, addGuobiaoHeuristicBotButton);
        playerPanel1.BakeSeatStatus(playerPanel2);
        playerPanel3.BakeEmptySeatBots(addBotButton, addSmartBotButton, addGuobiaoHeuristicBotButton);
        playerPanel4.BakeEmptySeatBots(addBotButton, addSmartBotButton, addGuobiaoHeuristicBotButton);
        var row = transform.Find("BotSpeedRow") as RectTransform;
        if (!row && botSpeedDropdown) row = botSpeedDropdown.transform.parent as RectTransform;
        if (!row) {
            row = new GameObject("BotSpeedRow", typeof(RectTransform)).GetComponent<RectTransform>();
        }
        row.SetParent(startButton.transform.parent, false);
        row.SetAsLastSibling();
        row.SetSiblingIndex(startButton.transform.GetSiblingIndex());
        SetBotRect(row, Vector2.zero, new Vector2(450, 75));
        row.pivot = new Vector2(.5f, .5f);
        var footerLayout = row.parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (footerLayout) footerLayout.childAlignment = TextAnchor.MiddleRight;
        var label = BotSpeedLabel(row, "Label", "机器人速度", 0, 170);
        label.fontSize = 26;
        label.rectTransform.anchoredPosition = new Vector2(0, -11.5f);

        if (!botSpeedDropdown) {
            var createPanel = gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CreatePanel>(true)).First();
            var template = (TMP_Dropdown)new UnityEditor.SerializedObject(createPanel)
                .FindProperty("chooseRule").objectReferenceValue;
            botSpeedDropdown = Instantiate(template, row);
            botSpeedDropdown.name = "BotSpeed";
            // Keep only ordinary UI components; creator-specific helpers belong to the creator.
            foreach (var component in botSpeedDropdown.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (component is Selectable || component is Graphic || component is Mask
                    || component is RectMask2D || component is ScrollRect || component is LayoutGroup
                    || component is LayoutElement || component is ContentSizeFitter) continue;
                DestroyImmediate(component);
            }
        }
        SetBotRect((RectTransform)botSpeedDropdown.transform, new Vector2(180, -7.5f), new Vector2(270, 60));
        botSpeedDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        botSpeedDropdown.ClearOptions();
        botSpeedDropdown.AddOptions(new List<string> { "极快（0 秒）", "快速（0.5 秒）", "中等（1 秒）", "慢速（1.5 秒）" });
        var popup = botSpeedDropdown.template;
        popup.anchorMin = new Vector2(0, 1);
        popup.anchorMax = Vector2.one;
        popup.pivot = new Vector2(.5f, 0);
        popup.anchoredPosition = new Vector2(0, 6);
        popup.sizeDelta = new Vector2(0, botSpeedDropdown.options.Count * 52 + 8);
        botSpeedDropdown.SetValueWithoutNotify(1);
        botSpeedDropdown.interactable = false;
        botSpeedDropdown.gameObject.SetActive(true);
        botSpeedDropdown.template.gameObject.SetActive(false);
        botSpeedDropdown.RefreshShownValue();
        if (row.Find("Hint")) row.Find("Hint").gameObject.SetActive(false);
        row.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void RemoveLegacyClaimProtectionControl() {
        var legacy = startButton.transform.parent.Find("ClaimProtection");
        if (legacy) UnityEditor.Undo.DestroyObjectImmediate(legacy.gameObject);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private TMP_Text BotSpeedLabel(RectTransform row, string objectName, string value, float x, float width) {
        var child = row.Find(objectName);
        TMP_Text label = child ? child.GetComponent<TMP_Text>() : Instantiate(roomIdText, row);
        label.name = objectName;
        label.text = value;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        SetBotRect(label.rectTransform, new Vector2(x, 0), new Vector2(width, 52));
        return label;
    }

    private static void SetBotRect(RectTransform rect, Vector2 position, Vector2 size) {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    public void ValidateBotSpeedUi() {
        if (startButton.transform.parent.Find("ClaimProtection"))
            throw new InvalidOperationException("鸣牌保护应只在创建房间面板中设置。");
        if (!botSpeedDropdown || botSpeedDropdown.transform.parent.name != "BotSpeedRow"
            || !botSpeedDropdown.options.Select(option => option.text).SequenceEqual(new[] { "极快（0 秒）", "快速（0.5 秒）", "中等（1 秒）", "慢速（1.5 秒）" })
            || botSpeedDropdown.template.rect.height < 216
            || botSpeedDropdown.transform.parent.parent != startButton.transform.parent)
            throw new InvalidOperationException("房间面板机器人速度控件未烘焙。");
        playerPanel1.ValidateEmptySeatBots();
        playerPanel2.ValidateEmptySeatBots();
        playerPanel3.ValidateEmptySeatBots();
        playerPanel4.ValidateEmptySeatBots();
    }
}
#endif
