using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private static readonly int[] SichuanDuplicateBoardCounts = { 1, 4, 8, 12, 16 };
    [SerializeField] private TMP_Dropdown SichuanDuplicateRounds;
    [SerializeField] private Toggle SichuanDuplicateSyncToggle;
    [SerializeField] private UnityEngine.UI.Image creationHeader;
    [SerializeField] private UnityEngine.UI.Image creationFooter;
    [SerializeField] private Color ordinaryHeaderColor = new Color32(44, 58, 116, 255);
    [SerializeField] private Color ordinaryFooterColor = new Color32(43, 57, 116, 255);
    [SerializeField] private Color venueCreationColor = new Color32(43, 103, 79, 255);
    private string duplicateVenueDraftId;
    private bool IsSichuanDuplicate => _ruleState == "sichuan" && SichuanLobby.IsDuplicate(GetSelectedSubRule());

    private void ApplyCreationContextColors() {
        bool venue = context != CreationContext.Room && !string.IsNullOrEmpty(_venueEventId);
        if (creationHeader) creationHeader.color = venue ? venueCreationColor : ordinaryHeaderColor;
        if (creationFooter) creationFooter.color = venue ? venueCreationColor : ordinaryFooterColor;
    }

    private void RefreshSichuanDuplicateControls() {
        bool paired = IsSichuanDuplicate;
        if (SichuanDuplicateRounds) {
            SichuanDuplicateRounds.transform.parent.gameObject.SetActive(paired);
            SichuanDuplicateRounds.gameObject.SetActive(paired);
            SichuanDuplicateRounds.interactable = paired && string.IsNullOrWhiteSpace(SelectedDuplicateKey);
            if (paired && !SichuanDuplicateRounds.interactable)
                TableText.Set(SichuanDuplicateRounds.captionText, "跟随密钥", "sichuan");
            else SichuanDuplicateRounds.RefreshShownValue();
        }
        if (SichuanDuplicateSyncToggle) SichuanDuplicateSyncToggle.gameObject.SetActive(paired);
        if (paired) {
            foreach (var toggle in RoomRoundToggles) if (toggle) toggle.gameObject.SetActive(false);
            var rounds = RoomContent.Find("RoundSelection");
            if (rounds) rounds.gameObject.SetActive(false);
            SetRandomSeedToggle.SetIsOnWithoutNotify(false);
            SetRandomSeedToggle.gameObject.SetActive(false);
            SetRandomSeedPanel.SetActive(false);
        } else SetRandomSeedToggle.gameObject.SetActive(true);
        if (DuplicateKeyInput && DuplicateKeyInput.placeholder is TMP_Text placeholder) {
            TableText.Set(placeholder, paired ? "留空自动生成" : "牌山密钥", _ruleState);
            if (paired) placeholder.gameObject.SetActive(string.IsNullOrEmpty(DuplicateKeyInput.text));
        }
    }

    private void BindSichuanDuplicateControls() {
        if (DuplicateKeyInput) DuplicateKeyInput.onValueChanged.AddListener(_ => RefreshSichuanDuplicateControls());
    }

#if UNITY_EDITOR
    public void BakeSichuanDuplicateControls() {
        if (!SichuanDuplicateRounds) {
            var row = Instantiate(roundTimer.transform.parent.gameObject, roundTimer.transform.parent.parent);
            row.name = "SichuanDuplicateRounds";
            row.transform.SetSiblingIndex(roundTimer.transform.parent.GetSiblingIndex());
            SichuanDuplicateRounds = row.GetComponentInChildren<TMP_Dropdown>(true);
            SichuanDuplicateRounds.onValueChanged = new TMP_Dropdown.DropdownEvent();
            SichuanDuplicateRounds.ClearOptions();
            SichuanDuplicateRounds.AddOptions(SichuanDuplicateBoardCounts.Select(n => n + "副").ToList());
            SichuanDuplicateRounds.SetValueWithoutNotify(2);
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.GetComponentInParent<TMP_Dropdown>(true)) continue;
                TableText.Set(text, "副数", "sichuan");
            }
            foreach (var help in row.GetComponentsInChildren<CreateRoomHelpTrigger>(true))
                help.Configure(this, "密钥留空时按所选副数生成；输入已有密钥时，副数跟随密钥。");
            row.SetActive(false);
        }
        if (!SichuanDuplicateSyncToggle) {
            SichuanDuplicateSyncToggle = Instantiate(DuplicateWallToggle, DuplicateWallToggle.transform.parent);
            SichuanDuplicateSyncToggle.name = "SichuanDuplicateSync";
            SichuanDuplicateSyncToggle.group = null;
            SichuanDuplicateSyncToggle.onValueChanged = new Toggle.ToggleEvent();
            SichuanDuplicateSyncToggle.SetIsOnWithoutNotify(false);
            TableText.Set(SichuanDuplicateSyncToggle.GetComponentInChildren<TMP_Text>(true), "同步模式", "sichuan");
            var help = SichuanDuplicateSyncToggle.GetComponent<CreateRoomHelpTrigger>();
            if (help) help.Configure(this, "同步模式：两室每副同时开始。非同步模式：各室可连续进行，全部结束后公开最终比分。");
            SichuanDuplicateSyncToggle.gameObject.SetActive(false);
        }
        if (!creationHeader) {
            creationHeader = transform.Find("HeaderPanel").GetComponent<UnityEngine.UI.Image>();
            ordinaryHeaderColor = creationHeader.color;
        }
        SichuanDuplicateRounds.gameObject.SetActive(true);
        if (!creationFooter) {
            creationFooter = transform.Find("FooterPanel").GetComponent<UnityEngine.UI.Image>();
            ordinaryFooterColor = creationFooter.color;
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
