using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas : MonoBehaviour {
    // 显示可用行动按钮
    public void SetActionButton(List<string> action_list){
        // 用于跟踪吃牌按钮
        ActionButton chiButton = null;
        // 用于跟踪暗杠按钮
        ActionButton angangButton = null;
        // 用于跟踪加杠按钮
        ActionButton jiagangButton = null;

        // 清空按钮
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
        
        for (int i = 0; i < action_list.Count; i++){

            Debug.Log($"询问操作: {action_list[i]}");

            // 碰牌
            if (action_list[i] == "peng"){
                Debug.Log($"碰牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer); // 实例化按钮
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "碰"; // 设置按钮文本
                Debug.Log($"碰牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]); // 添加按钮对应的行动
            }
            // 杠牌
            else if (action_list[i] == "gang"){
                Debug.Log($"杠牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "杠";
                Debug.Log($"杠牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 胡牌
            else if (action_list[i] == "hu_self"){
                string huSelfText = GetHuSelfActionText();
                Debug.Log(huSelfText);
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = huSelfText;
                Debug.Log($"{huSelfText}按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "hu" || action_list[i] == "hu_first" || action_list[i] == "hu_second" || action_list[i] == "hu_third"){
                Debug.Log($"和牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "和";
                Debug.Log($"和牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 补花
            else if (action_list[i] == "buhua"){
                Debug.Log($"补花");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "补花";
                Debug.Log($"补花按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            } 
            // 暗杠加杠吃牌可能有多个选择的选项，将这些选项添加入单个按钮。
            // 暗杠
            else if (action_list[i] == "angang"){
                if (angangButton == null) {
                    angangButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    TMP_Text buttonText = angangButton.TextObject;
                    buttonText.text = "暗杠";
                    Debug.Log($"暗杠按钮: {angangButton}");
                }
                angangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加暗杠选项: {action_list[i]}");
            }
            // 加杠
            else if (action_list[i] == "jiagang"){
                if (jiagangButton == null) {
                    jiagangButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    TMP_Text buttonText = jiagangButton.TextObject;
                    buttonText.text = "加杠";
                    Debug.Log($"加杠按钮: {jiagangButton}");
                }
                jiagangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加加杠选项: {action_list[i]}");
            }
            // 吃牌
            else if (action_list[i] == "chi_left" || action_list[i] == "chi_right" || action_list[i] == "chi_mid"){
                if (chiButton == null) {
                    // 第一次遇到吃牌选项时创建按钮
                    chiButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    TMP_Text buttonText = chiButton.TextObject;
                    buttonText.text = "吃";
                    Debug.Log($"创建吃牌按钮: {chiButton}");
                }
                // 将当前的吃牌选项添加到已存在的吃牌按钮中
                chiButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加吃牌选项: {action_list[i]}");
            }
            // 九种九牌 / 九老峰回（按规则区分文案）
            else if (action_list[i] == "jiuzhongjiupai"){
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = GetJiuzhongjiupaiActionText();
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 立直（仅在自家门清听牌且服务器允许时下发；点击后进入立直选牌模式）
            else if (action_list[i] == "riichi_cut"){
                Debug.Log($"立直");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "立直";
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 取消
            else if (action_list[i] == "pass"){
                Debug.Log($"取消");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "取消";
                Debug.Log($"取消按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
        }

        // 播放操作按钮出现音效
        if (ActionButtonContainer.childCount > 0 && SoundManager.Instance != null) {
            SoundManager.Instance.PlayActionButtonAppearSound();
        }

        // 食替禁切等场景需要按当前状态变暗手牌
        GameCanvas.Instance.RefreshHandTileSelectability();
    }

    // 选择行动
    public void ChooseAction(string actionType, int targetTile, int chiComboIndex = -1){
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        NormalGameStateManager.Instance.CancelWaitAutoAction($"ChooseAction({actionType})");
        NormalGameStateManager.Instance.SwitchCurrentPlayer("self","ClearAction",0);
        // 发送行动：立直麻将涉赤 5 时通过 chiComboIndex 指明所选吃牌候选（默认 0 表示优先非赤 5）
        int idx = chiComboIndex >= 0 ? chiComboIndex : 0;
        GameStateNetworkManager.Instance.SendAction(actionType, targetTile, idx);
    }

    public void TrySendPassFromShortcut() {
        if (!NormalGameStateManager.Instance.allowActionList.Contains("pass")) return;
        ChooseAction("pass", 0);
    }
}
