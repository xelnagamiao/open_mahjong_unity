using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas : MonoBehaviour {
    [System.Serializable]
    public class ActionButtonColorPreset {
        [InspectorName("普通")]
        public Color normalColor = new Color(0.341367f, 0.538903f, 0.841509f, 1f);
        [InspectorName("高亮")]
        public Color highlightedColor = new Color(0.033236448f, 0f, 0.4943396f, 1f);
        [InspectorName("按下")]
        public Color pressedColor = new Color(0.1764706f, 0.1764706f, 0.1764706f, 1f);
        [InspectorName("禁用")]
        public Color disabledColor = new Color(0.1764706f, 0.1764706f, 0.1764706f, 0.5019608f);

        public ColorBlock ToColorBlock() {
            return new ColorBlock {
                normalColor = normalColor,
                highlightedColor = highlightedColor,
                pressedColor = pressedColor,
                selectedColor = pressedColor,
                disabledColor = disabledColor,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
        }
    }

    [System.Serializable]
    public class ActionButtonColorPresets {
        [InspectorName("吃")]
        public ActionButtonColorPreset chi = new ActionButtonColorPreset();
        [InspectorName("碰")]
        public ActionButtonColorPreset peng = new ActionButtonColorPreset();
        [InspectorName("杠")]
        public ActionButtonColorPreset gang = new ActionButtonColorPreset();
        [InspectorName("补张")]
        public ActionButtonColorPreset buzhang = new ActionButtonColorPreset();
        [InspectorName("暗杠")]
        public ActionButtonColorPreset angang = new ActionButtonColorPreset();
        [InspectorName("加杠")]
        public ActionButtonColorPreset jiagang = new ActionButtonColorPreset();
        [InspectorName("和")]
        public ActionButtonColorPreset hu = new ActionButtonColorPreset();
        [InspectorName("自摸")]
        public ActionButtonColorPreset huSelf = new ActionButtonColorPreset();
        [InspectorName("补花")]
        public ActionButtonColorPreset buhua = new ActionButtonColorPreset();
        [InspectorName("九种九牌")]
        public ActionButtonColorPreset jiuzhongjiupai = new ActionButtonColorPreset();
        [InspectorName("立直")]
        public ActionButtonColorPreset riichi = new ActionButtonColorPreset();
        [InspectorName("过")]
        public ActionButtonColorPreset pass = new ActionButtonColorPreset();
        [InspectorName("默认")]
        public ActionButtonColorPreset fallback = new ActionButtonColorPreset();
    }

    [Header("操作按钮颜色预设")]
    [SerializeField] private ActionButtonColorPresets actionButtonColorPresets = new ActionButtonColorPresets();

    /// <summary>本次 SetActionButton 新建的一次性询问按钮数；常驻槽位按钮不计。</summary>
    private int createdRegularButtonCount;

    private ActionButton CreateActionButton(ActionButtonColorPreset preset) {
        ActionButton actionButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
        Button button = actionButton.GetComponent<Button>();
        if (button != null && preset != null) {
            button.colors = preset.ToColorBlock();
        }
        ConfigureActionButtonText(actionButton.TextObject);
        createdRegularButtonCount++;
        return actionButton;
    }

    /// <summary>
    /// 常驻槽位按钮（ExtraActionButton）：显示词表里归为 Persistent 的词；传 null 隐藏。
    /// 槽位不随询问生成/销毁，所以文案与配色都从词表取，核心不认识具体的词。
    /// </summary>
    private void SetPersistentActionButton(string persistentWord) {
        if (ExtraActionButton == null) return;
        ActionButton actionButton = ExtraActionButton.GetComponent<ActionButton>();
        if (actionButton == null) {
            ExtraActionButton.gameObject.SetActive(false);
            return;
        }
        actionButton.actionTypeList.Clear();
        bool visible = !string.IsNullOrEmpty(persistentWord);
        if (visible) {
            actionButton.actionTypeList.Add(persistentWord);
            actionButton.TextObject.text = ActionWords.LabelOf(persistentWord) ?? persistentWord;
            ConfigureActionButtonText(actionButton.TextObject);
            Button button = actionButton.GetComponent<Button>();
            ActionButtonColorPreset preset = GetActionButtonColorPreset(persistentWord);
            if (button != null && preset != null) button.colors = preset.ToColorBlock();
        }
        ExtraActionButton.gameObject.SetActive(visible);
    }

    private static void ConfigureActionButtonText(TMP_Text text) {
        if (text == null) return;
        text.enableAutoSizing = true;
        text.fontSizeMax = Mathf.Max(text.fontSize, 36f);
        text.fontSizeMin = 18f;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.alignment = TextAlignmentOptions.Center;
    }

    private ActionButtonColorPreset GetActionButtonColorPreset(string action) {
        if (!GameSettings.Current.ActionButtonColorEnabled) {
            return actionButtonColorPresets.fallback;
        }
        switch (action) {
            case "chi_left":
            case "chi_mid":
            case "chi_right":
                return actionButtonColorPresets.chi;
            case "peng":
                return actionButtonColorPresets.peng;
            case "gang":
                return actionButtonColorPresets.gang;
            case "buzhang":
                return actionButtonColorPresets.buzhang;
            case "angang":
                return actionButtonColorPresets.angang;
            case "jiagang":
                return actionButtonColorPresets.jiagang;
            case "hu":
            case "hu_first":
            case "hu_second":
            case "hu_third":
                return actionButtonColorPresets.hu;
            case "hu_self":
            case "hu_flower":
            case "initial_hu":
                return actionButtonColorPresets.huSelf;
            case "buhua":
                return actionButtonColorPresets.buhua;
            case "jiuzhongjiupai":
                return actionButtonColorPresets.jiuzhongjiupai;
            case "riichi_cut":
                return actionButtonColorPresets.riichi;
            case "pass":
            case "force_pass":
                return actionButtonColorPresets.pass;
            default:
                return GetActionButtonColorPresetByKind(ActionWords.KindOf(action));
        }
    }

    /// <summary>规则模块登记的词按词表类别配色，未归类的词用默认色。</summary>
    private ActionButtonColorPreset GetActionButtonColorPresetByKind(ActionWordKind kind) {
        switch (kind) {
            case ActionWordKind.Chi: return actionButtonColorPresets.chi;
            case ActionWordKind.Peng: return actionButtonColorPresets.peng;
            case ActionWordKind.MingGang: return actionButtonColorPresets.gang;
            case ActionWordKind.Ron: return actionButtonColorPresets.hu;
            case ActionWordKind.Tsumo: return actionButtonColorPresets.huSelf;
            case ActionWordKind.Pass: return actionButtonColorPresets.pass;
            default: return actionButtonColorPresets.fallback;
        }
    }

    /// <summary>标准词的按钮文案：清单覆盖（长沙"开杠"……）→ 词表 → 通用文案。</summary>
    private static string ButtonCaption(string word) {
        return StandardActionCaptions.Resolve(RuleRegistry.Current, word);
    }

    /// <summary>按当前开关与预设，刷新 ActionButtonContainer 内已有按钮配色。</summary>
    public void RefreshActionButtonColors() {
        if (ActionButtonContainer == null) return;
        for (int i = 0; i < ActionButtonContainer.childCount; i++) {
            ActionButton actionButton = ActionButtonContainer.GetChild(i).GetComponent<ActionButton>();
            if (actionButton == null || actionButton.actionTypeList == null || actionButton.actionTypeList.Count == 0) {
                continue;
            }
            ActionButtonColorPreset preset = GetActionButtonColorPreset(actionButton.actionTypeList[0]);
            Button button = actionButton.GetComponent<Button>();
            if (button != null && preset != null) {
                button.colors = preset.ToColorBlock();
            }
        }
    }

    // 显示可用行动按钮
    public void SetActionButton(List<string> action_list){
        action_list = action_list ?? new List<string>();
        bool isSeaBottomAsk = action_list.Contains("sea_bottom");
        string persistentWord = ActionWords.First(action_list, ActionWordKind.Persistent);
        // 用于跟踪吃牌按钮
        ActionButton chiButton = null;
        // 用于跟踪暗杠按钮
        ActionButton angangButton = null;
        // 用于跟踪补张按钮
        ActionButton buzhangButton = null;
        // 用于跟踪加杠按钮
        ActionButton jiagangButton = null;

        // 清空按钮
        foreach (Transform child in ActionButtonContainer){
            if (child != ExtraActionButton) Destroy(child.gameObject);
        }
        SetPersistentActionButton(persistentWord);

        createdRegularButtonCount = 0;

        for (int i = 0; i < action_list.Count; i++){

            Debug.Log($"询问操作: {action_list[i]}");
            ActionButtonColorPreset colorPreset = GetActionButtonColorPreset(action_list[i]);

            // 常驻槽位词已由 SetPersistentActionButton 处理，不再生成一次性按钮。
            if (action_list[i] == persistentWord) continue;

            // 规则模块登记并自带文案的词：一词一按钮，展开/直发由 ActionButton 按词表决定。
            string moduleLabel = ActionWords.LabelOf(action_list[i]);
            if (moduleLabel != null) {
                ActionButton actionButton = CreateActionButton(colorPreset);
                actionButton.TextObject.text = moduleLabel;
                actionButton.actionTypeList.Add(action_list[i]);
                continue;
            }

            // 碰牌
            if (action_list[i] == "peng"){
                Debug.Log($"碰牌");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "碰";
                Debug.Log($"碰牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 杠牌
            else if (action_list[i] == "gang"){
                Debug.Log($"杠牌");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = ButtonCaption("gang");
                Debug.Log($"杠牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 胡牌
            else if (action_list[i] == "hu_self"){
                string huSelfText = ButtonCaption("hu_self");
                Debug.Log(huSelfText);
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = huSelfText;
                Debug.Log($"{huSelfText}按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "hu_flower"){
                Debug.Log("花胡");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "花胡";
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "initial_hu"){
                Debug.Log($"起手胡");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "起手胡";
                Debug.Log($"起手胡按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "sea_bottom"){
                Debug.Log($"要海底");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "要海底";
                Debug.Log($"要海底按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "hu" || action_list[i] == "hu_first" || action_list[i] == "hu_second" || action_list[i] == "hu_third"){
                Debug.Log($"和牌");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "和";
                Debug.Log($"和牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 补花
            else if (action_list[i] == "buhua"){
                Debug.Log($"补花");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "补花";
                Debug.Log($"补花按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 暗杠加杠吃牌可能有多个选择的选项，将这些选项添加入单个按钮。
            // 暗杠
            else if (action_list[i] == "angang"){
                if (angangButton == null) {
                    angangButton = CreateActionButton(colorPreset);
                    TMP_Text buttonText = angangButton.TextObject;
                    buttonText.text = ButtonCaption("angang");
                    Debug.Log($"暗杠按钮: {angangButton}");
                }
                angangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加暗杠选项: {action_list[i]}");
            }
            // 补张
            else if (action_list[i] == "buzhang"){
                if (buzhangButton == null) {
                    buzhangButton = CreateActionButton(colorPreset);
                    TMP_Text buttonText = buzhangButton.TextObject;
                    buttonText.text = "补张";
                    Debug.Log($"补张按钮: {buzhangButton}");
                }
                buzhangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加补张选项: {action_list[i]}");
            }
            // 加杠
            else if (action_list[i] == "jiagang"){
                if (jiagangButton == null) {
                    jiagangButton = CreateActionButton(colorPreset);
                    TMP_Text buttonText = jiagangButton.TextObject;
                    buttonText.text = ButtonCaption("jiagang");
                    Debug.Log($"加杠按钮: {jiagangButton}");
                }
                jiagangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加加杠选项: {action_list[i]}");
            }
            // 吃牌
            else if (action_list[i] == "chi_left" || action_list[i] == "chi_right" || action_list[i] == "chi_mid"){
                if (chiButton == null) {
                    chiButton = CreateActionButton(colorPreset);
                    TMP_Text buttonText = chiButton.TextObject;
                    buttonText.text = "吃";
                    Debug.Log($"创建吃牌按钮: {chiButton}");
                }
                chiButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加吃牌选项: {action_list[i]}");
            }
            // 九老峰回
            else if (action_list[i] == "jiuzhongjiupai"){
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "九老峰回";
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 听牌声明（仅在服务器允许时下发；点击后进入声明切牌选择模式）
            else if (action_list[i] == "riichi_cut"){
                string readyText = ButtonCaption("riichi_cut");
                Debug.Log(readyText);
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = readyText;
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 取消
            else if (action_list[i] == "pass"){
                Debug.Log($"取消");
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                TMP_Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = isSeaBottomAsk ? "不要" : "取消";
                Debug.Log($"取消按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "force_pass"){
                ActionButton ActionButtonObj = CreateActionButton(colorPreset);
                ActionButtonObj.TextObject.text = "放弃";
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
        }

        // 播放操作按钮出现音效：只数本次新建的询问按钮。
        // 单独出现常驻槽位按钮时保持安静；被 Destroy 的旧按钮本帧仍在 childCount 里，不能拿它判断。
        if (createdRegularButtonCount > 0) {
            SoundManager.Instance.PlayActionButtonAppearSound();
        }

        // 食替禁切等场景需要按当前状态变暗手牌
        GameCanvas.Instance.RefreshHandTileSelectability();
    }

    // 选择行动
    public void ChooseAction(string actionType, int targetTile, int chiComboIndex = -1){
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        // 有自己出站通道的族（如虹雀）自行发送动作，核心不再走通用 SendAction
        IGameState state = RuleRegistry.ActiveGameState;
        if (state != null && state.TryChooseAction(actionType)) return;
        AutoActionPolicy.Current.Cancel($"ChooseAction({actionType})");
        NormalGameStateManager.Instance.SwitchCurrentPlayer("self","ClearAction",0);
        // 发送行动：立直麻将涉赤 5 时通过 chiComboIndex 指明所选吃牌候选（默认 0 表示优先非赤 5）
        int idx = chiComboIndex >= 0 ? chiComboIndex : 0;
        GameStateNetworkManager.Instance.SendAction(actionType, targetTile, idx);
    }

    public void TrySendPassFromShortcut() {
        string passAction = ActionWords.First(NormalGameStateManager.Instance.allowActionList, ActionWordKind.Pass);
        if (passAction == null) return;
        ChooseAction(passAction, 0);
    }
}
