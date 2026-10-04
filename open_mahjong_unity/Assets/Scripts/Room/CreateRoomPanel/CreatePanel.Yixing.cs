using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField] private Toggle YixingSevenPairsToggle;
    private void CreateYixingRoom() {
        var config = new Qingque_Create_RoomConfig {
            Rule=YixingGameState.RuleId, SubRule=YixingGameState.SubRule,
            RoomName=roomNameInput.text.Trim(), GameRound=GetSelectedGameTime(),
            Password=passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed=SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            RoundTimer=GetSelectedRoundTimer(), StepTimer=GetSelectedStepTimer(),
            Tips=tipsToggle.isOn, CountTips=countTipsToggle.isOn, PointerTips=pointerTipsToggle.isOn,
            TouristLimit=TouristLimitToggle.isOn, AllowSpectator=AllowSpectatorToggle.isOn,
            ClaimProtection=false, TacticalCall=false, EventId=_venueEventId,
        };
        if (!config.Validate(out string error,passwordToggle.isOn,SetRandomSeedToggle.isOn)) {
            NotificationManager.Instance.ShowTip("create_room",false,error); return;
        }
        RoomNetworkManager.Instance.Create_Yixing_Room(config,YixingSevenPairsToggle && YixingSevenPairsToggle.isOn);
    }
#if UNITY_EDITOR
    public void BakeYixingRoomControls() {
        YixingSevenPairsToggle=EnsureClonedToggle(tipsToggle,YixingSevenPairsToggle,"YixingSevenPairs","七小对",false);
        YixingSevenPairsToggle.group=null;
        YixingSevenPairsToggle.onValueChanged=new Toggle.ToggleEvent();
        YixingSevenPairsToggle.transform.SetAsLastSibling();
        YixingSevenPairsToggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
