using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField] private Button restoreDefaultsButton;
    [SerializeField] private TMP_Text roomHelpText;
    [SerializeField] private RectTransform roomHelpPanel;
    [SerializeField] private CreateRoomHelpLifetime roomHelpLifetime;
    private MessagePrefab roomHelpNavigationPrompt;
    private const float RoomHelpGutter = 64;

    private static float RoomHelpWidth(float panelWidth) => Mathf.Clamp((panelWidth - 1440) / 2 - RoomHelpGutter, 176, 300);

    internal bool IsRoomHelpNavigationOpen => roomHelpNavigationPrompt;

    public void ShowRoomHelp(string message, Component source = null) {
        if (!roomHelpText) return;
        if (IsShanghaiQiaoma && source && source.gameObject == InputHepaiLimitToggle.gameObject)
            message = "一番和：开启后至少1番才能和牌，花数不算番；杠开、抢杠计入番数，原有花数门槛仍生效。关闭后允许垃圾和。";
        if (_ruleState == "sichuan" && source && (source.gameObject == InputHepaiLimitToggle.gameObject
            || source.transform.IsChildOf(InputHepaiLimitPlane.transform)))
            message = "修改起和番：输入0–64的整数。默认0番，允许平和；低于设置番数不能和牌，番数提示和流局查叫同步使用此限制。";
        roomHelpText.text = message;
        if (roomHelpPanel) roomHelpPanel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        ReflowRoomHelp();
        if (!string.IsNullOrEmpty(message) && roomHelpLifetime) roomHelpLifetime.MarkShown();
    }

    private void ReflowRoomHelp() {
        if (!roomHelpPanel || !roomHelpText) return;
        var body = (RectTransform)transform.Find("Create_Panel");
        float sideWidth = RoomHelpWidth(body.rect.width);
        roomHelpText.fontSize = 22;
        float height = Mathf.Max(78, Mathf.Ceil(roomHelpText.GetPreferredValues(roomHelpText.text, sideWidth - 20, 0).y) + 24);
        roomHelpPanel.anchorMin = roomHelpPanel.anchorMax = roomHelpPanel.pivot = Vector2.one;
        roomHelpPanel.anchoredPosition = new Vector2(-16, body.offsetMax.y - 16);
        roomHelpPanel.sizeDelta = new Vector2(sideWidth, height);
        RoomFill(roomHelpText.rectTransform, 10, 12, 10, 12);
    }

    private static string ResolveRoomHelpUrl(string linkId) {
        string path = linkId == "duplicate" ? "/duplicate"
            : linkId == "tactical-call" ? "/guide#tactical-call" : null;
        return path == null ? null : ConfigManager.webUrl.TrimEnd('/') + path;
    }

    public void ConfirmRoomHelpNavigation(string linkId) {
        string url = ResolveRoomHelpUrl(linkId);
        if (url == null || roomHelpNavigationPrompt || !NotificationManager.Instance) return;
        roomHelpNavigationPrompt = NotificationManager.Instance.ShowConfirmation(
            "跳转外部网页", "需要跳转外部网页，您确定吗？", () => Application.OpenURL(url), "确定", "取消");
        if (!roomHelpNavigationPrompt) return;
        foreach (var button in roomHelpNavigationPrompt.GetComponentsInChildren<Button>(true))
            if (button.GetComponentInChildren<TMP_Text>(true)?.text == "确定") RoomPrimaryButton(button);
    }

    private void ApplyDefaultRoomPreset() {
        if (_ruleState == "guobiao") ApplyGuobiaoTierPreset(0);
        else if (_ruleState == "riichi") ApplyRiichiPreset(0);
        else if (_ruleState == "hongkong") ResetHongKongMainControls();
    }

    private void ResetRoomDefaults() {
        int subRule = SubRuleDropdown.value;
        CancelDetailedConfigChanges();
        DuplicateWallToggle.isOn = false;
        passwordInput.text = randomSeedInput.text = DuplicateKeyInput.text = "";
        roomNameInput.text = GetDefaultRoomName();
        ApplyRuleDefaults(_ruleState);
        if (_ruleState == "taiwan") ResetDetailedConfig(GetDetailedConfigState("taiwan"));
        ApplyDefaultRoomPreset();
        SubRuleDropdown.SetValueWithoutNotify(subRule);
        OnSubRuleChanged(subRule);
        if (_ruleState == "hongkong") ResetHongKongMainControls();
        RefreshRoomPresentation();
        ShowRoomHelp("已恢复默认设置。");
    }
}
