using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using Riichi;

public class ActionButton : MonoBehaviour {
    [SerializeField] private GameObject ActionBlockPrefab; // 次级按钮选择块
    [SerializeField] private GameObject StaticCardPrefab; // 静态牌预制体(包含在多种结果显示中的图像)
    // 通过 GameCanvas 单例获取 ActionBlockContenter，不再需要 SerializeField
    private Transform ActionBlockContenter => GameCanvas.Instance.ActionBlockContenter;

    [SerializeField] private TMP_Text textObject; // 按钮文本

    public List<string> actionTypeList = new List<string>(); // 动作类型列表 同一个按钮可能有多个行动 例如 chi_left,chi_right,chi_mid

    public TMP_Text TextObject {
        get {
            return textObject;
        }
    }

    void Start() {
        // 验证TMP_Text组件是否已赋值
        if (textObject == null)
        {
            Debug.LogError("ActionButton: TMP_Text component is not assigned in Inspector!");
        }
        else {
            ConfigureButtonText(textObject);
        }

        // 按钮点击事件
        Button button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("ActionButton: Button component not found!");
            return;
        }
        button.onClick.AddListener(OnClick);
    }

    private static void ConfigureButtonText(TMP_Text text) {
        text.enableAutoSizing = true;
        text.fontSizeMax = Mathf.Max(text.fontSize, 36f);
        text.fontSizeMin = 18f;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.alignment = TextAlignmentOptions.Center;
    }

    // 判定当前按钮是否按服务端/顺子候选集合处理（标准吃牌，或规则声明使用 chi_candidates 的鸣牌动作）
    private bool UsesClaimCandidateFlow(){
        var state = RuleRegistry.ActiveGameState;
        var chiCandidates = NormalGameStateManager.Instance.chiCandidates;
        foreach (string a in actionTypeList){
            if (a == "chi_left" || a == "chi_mid" || a == "chi_right") return true;
            if (state != null && state.UsesClaimCandidates(a)
                && chiCandidates != null && chiCandidates.TryGetValue(a, out int[][] pairs) && pairs != null && pairs.Length > 0)
                return true;
        }
        return false;
    }

    // 汇总当前吃牌/候选鸣牌按钮所有方向的候选组合
    // 每项：(actionType, chiComboIndex, [tileA, tileB]) — 两张真实牌 ID（含赤 5 的 105/205/305）
    private List<(string action, int comboIndex, int[] pair)> CollectClaimCandidates(){
        List<(string, int, int[])> result = new List<(string, int, int[])>();
        int lastCutTile = NormalGameStateManager.Instance.lastCutCardID;
        var chiCandidates = NormalGameStateManager.Instance.chiCandidates;
        var state = RuleRegistry.ActiveGameState;
        foreach (string action in actionTypeList){
            bool isChiAction = action == "chi_left" || action == "chi_mid" || action == "chi_right";
            bool ruleClaimAction = !isChiAction && state != null && state.UsesClaimCandidates(action);
            if (!isChiAction && !ruleClaimAction) continue;
            int[][] pairs;
            if (chiCandidates != null && chiCandidates.TryGetValue(action, out pairs) && pairs != null && pairs.Length > 0){
                for (int i = 0; i < pairs.Length; i++){
                    result.Add((action, i, pairs[i]));
                }
            } else if (isChiAction) {
                int[] fallback;
                switch (action){
                    case "chi_left": fallback = new int[] { lastCutTile - 2, lastCutTile - 1 }; break;
                    case "chi_mid":  fallback = new int[] { lastCutTile - 1, lastCutTile + 1 }; break;
                    default:         fallback = new int[] { lastCutTile + 1, lastCutTile + 2 }; break;
                }
                result.Add((action, 0, fallback));
            }
        }
        return result;
    }

    // 按钮点击事件
    void OnClick(){
        // 规则声明不展开候选子按钮（如自由模式喊话）：直接发送单一动作。
        if (RuleRegistry.Current?.SkipActionCandidateExpansion == true && actionTypeList.Count == 1) {
            GameCanvas.Instance.ChooseAction(actionTypeList[0], 0);
            return;
        }
        // 规则模块登记为"点击后展开"的词（虹雀 hongque_group:*）：子按钮内容由词表给出，这里只负责摆
        if (actionTypeList.Count == 1
            && ActionWords.TryExpand(actionTypeList[0], out IReadOnlyList<ActionCandidate> moduleCandidates)) {
            ToggleModuleCandidates(actionTypeList[0], moduleCandidates);
            return;
        }
        // 规则自定义点击交互（如日麻立直选牌模式）
        if (actionTypeList.Count == 1
            && RuleRegistry.ActiveGameState?.TryHandleActionButtonClick(actionTypeList[0]) == true) {
            return;
        }
        bool usesClaimCandidates = UsesClaimCandidateFlow();
        List<(string action, int comboIndex, int[] pair)> claimCands = usesClaimCandidates ? CollectClaimCandidates() : null;
        bool isAngang = actionTypeList.Contains("angang");
        bool isBuzhang = actionTypeList.Contains("buzhang");
        bool isJiagang = actionTypeList.Contains("jiagang");
        var gsm = NormalGameStateManager.Instance;
        var angangOptions = isAngang ? CollectAngangOptions(gsm.selfHandTiles, gsm.player_to_info["self"].combination_tiles) : null;
        var buzhangOptions = isBuzhang
            ? CollectBuzhangOptions(gsm.selfHandTiles, gsm.player_to_info["self"].combination_tiles)
            : null;
        var jiagangOptions = isJiagang
            ? CollectJiagangOptions(gsm.selfHandTiles, gsm.player_to_info["self"].combination_tiles)
            : null;

        // 展开条件：多种吃牌/鸣牌候选，或手牌中实际存在多种暗杠/加杠目标。
        // 服务端会对每个可杠牌各下发一条 "angang"/"jiagang"，不能按 actionTypeList 条数展开。
        bool expandSubButtons = (usesClaimCandidates && claimCands.Count > 1)
            || (angangOptions != null && angangOptions.Count > 1)
            || (buzhangOptions != null && buzhangOptions.Count > 1)
            || (jiagangOptions != null && jiagangOptions.Count > 1);

        if (expandSubButtons){
            string currentButtonType = "None";
            if (usesClaimCandidates){
                currentButtonType = actionTypeList.Contains("peng") ? "peng" : "chi";
            } else if (actionTypeList.Contains("angang")){
                currentButtonType = "angang";
            } else if (actionTypeList.Contains("buzhang")){
                currentButtonType = "buzhang";
            } else if (actionTypeList.Contains("jiagang")){
                currentButtonType = "jiagang";
            }

            if (GameCanvas.Instance.ActionBlockContainerState == "None"){
                GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
            }
            else if (GameCanvas.Instance.ActionBlockContainerState != "None"){
                GameCanvas.Instance.ChoicePanel?.Hide();
                ActionBlockContenter.GetComponent<UpwardPopupLayout>()?.ClearVirtualActions();
                foreach (Transform child in ActionBlockContenter.Cast<Transform>().ToArray()){
                    Destroyer.Instance.AddToDestroyer(child);
                }
                if (GameCanvas.Instance.ActionBlockContainerState == currentButtonType){
                    GameCanvas.Instance.ActionBlockContainerState = "None";
                    return;
                }
                else {
                    GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
                }
            }

            if (usesClaimCandidates){
                foreach (var cand in claimCands){
                    CreateChiCandidateBlock(cand.action, cand.comboIndex, cand.pair);
                }
            } else {
                if (angangOptions != null) {
                    foreach (var angangOption in angangOptions) {
                        CreateActionCards(angangOption.displayTiles, "angang", angangOption.targetTile);
                    }
                }
                if (buzhangOptions != null) {
                    foreach (var buzhangOption in buzhangOptions) {
                        CreateActionCards(buzhangOption.displayTiles, "buzhang", buzhangOption.targetTile);
                    }
                }
                if (jiagangOptions != null) {
                    foreach (var jiagangOption in jiagangOptions) {
                        CreateActionCards(jiagangOption.displayTiles, "jiagang", jiagangOption.targetTile);
                    }
                }
            }
            ActionBlockContenter.SetAsLastSibling();
            UpwardPopupLayout.ForActions(ActionBlockContenter, textObject);
            return;
        }

        // 单一候选：直接发送
        if (usesClaimCandidates){
            if (claimCands.Count == 0) return;
            var only = claimCands[0];
            Debug.Log($"选择了行动 {only.action} chiComboIndex={only.comboIndex}");
            GameCanvas.Instance.ChooseAction(only.action, 0, only.comboIndex);
            return;
        }

        if (actionTypeList[0] == "jiagang"){
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            int targetTile = jiagangOptions != null && jiagangOptions.Count > 0 ? jiagangOptions[0].targetTile : 0;
            GameCanvas.Instance.ChooseAction(actionTypeList[0], targetTile);
        } else if (actionTypeList[0] == "buzhang"){
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            int targetTile = buzhangOptions != null && buzhangOptions.Count > 0 ? buzhangOptions[0].targetTile : 0;
            GameCanvas.Instance.ChooseAction(actionTypeList[0], targetTile);
        } else if (actionTypeList[0] == "angang"){
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            int targetTile = angangOptions != null && angangOptions.Count > 0 ? angangOptions[0].targetTile : 0;
            GameCanvas.Instance.ChooseAction(actionTypeList[0], targetTile);
        } else {
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            GameCanvas.Instance.ChooseAction(actionTypeList[0],0);
        }
    }

    /// <summary>展开/收起规则模块给出的候选子按钮；再次点击同一组词时收起。</summary>
    private void ToggleModuleCandidates(string groupWord, IReadOnlyList<ActionCandidate> candidates) {
        if (candidates.Count == 1 && candidates[0].TileGroups != null) {
            GameCanvas.Instance.ChooseAction(candidates[0].ActionType, candidates[0].TargetTile);
            return;
        }
        bool close = GameCanvas.Instance.ActionBlockContainerState == groupWord;
        GameCanvas.Instance.ChoicePanel?.Hide();
        ActionBlockContenter.GetComponent<UpwardPopupLayout>()?.ClearVirtualActions();
        foreach (Transform child in ActionBlockContenter.Cast<Transform>().ToArray())
            Destroyer.Instance.AddToDestroyer(child);
        if (close) {
            GameCanvas.Instance.ActionBlockContainerState = "None";
            return;
        }
        GameCanvas.Instance.ActionBlockContainerState = groupWord;
        ActionBlockContenter.SetAsLastSibling();
        if (candidates.Any(c => !string.IsNullOrEmpty(c.Caption))) {
            bool usesPanel = ActionCandidatePager.Build(ActionBlockContenter, candidates, ActionBlockPrefab, StaticCardPrefab, textObject, (RectTransform)transform);
            if (!usesPanel) UpwardPopupLayout.ForActions(ActionBlockContenter, textObject);
            return;
        }
        foreach (ActionCandidate candidate in candidates) {
            if (string.IsNullOrEmpty(candidate.ActionType)) continue;
            GameObject blockObject = Instantiate(ActionBlockPrefab, ActionBlockContenter);
            ActionBlock block = blockObject.GetComponent<ActionBlock>();
            block.actionType = candidate.ActionType;
            block.targetTile = candidate.TargetTile;
            foreach (int tileId in candidate.TileIds) {
                block.AddTile(StaticCardPrefab, tileId);
            }
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(ActionBlockContenter as RectTransform);
        UpwardPopupLayout.ForActions(ActionBlockContenter, textObject);
    }

    private static List<(int targetTile, List<int> displayTiles)> CollectAngangOptions(
        List<int> handTiles, List<string> combinations) {
        if (RuleRegistry.ActiveGameState != null
            && RuleRegistry.ActiveGameState.TryCollectKongOptions("angang", handTiles, combinations, out var custom))
            return custom;
        return CollectStandardAngangOptions(handTiles);
    }

    internal static List<(int targetTile, List<int> displayTiles)> CollectStandardAngangOptions(
        List<int> handTiles) {
        var options = new List<(int, List<int>)>();
        var processedNorms = new HashSet<int>();
        // 不可和的花色（四川定缺）不能暗杠
        int excludedSuit = RuleRegistry.ActiveGameState?.ExcludedSuit ?? 0;
        foreach (int tileID in handTiles) {
            int norm = RiichiTileUtil.Normalize(tileID);
            if (processedNorms.Contains(norm)) continue;
            processedNorms.Add(norm);
            if (excludedSuit >= 1 && excludedSuit <= 3 && norm / 10 == excludedSuit) continue;
            if (GameRecordMeldCodec.CountNormalizedTiles(handTiles, norm) != 4) continue;
            if (!TurnClock.Current.IsKongCandidate("angang", norm)) continue;
            var actualTiles = handTiles.Where(t => RiichiTileUtil.Normalize(t) == norm).ToList();
            options.Add((norm, actualTiles));
        }
        return options;
    }

    private static List<(int targetTile, List<int> displayTiles)> CollectJiagangOptions(
        List<int> handTiles, List<string> combinations) {
        if (RuleRegistry.ActiveGameState != null
            && RuleRegistry.ActiveGameState.TryCollectKongOptions("jiagang", handTiles, combinations, out var custom))
            return custom;
        var options = new List<(int, List<int>)>();
        var processedNorms = new HashSet<int>();
        foreach (int tileID in handTiles) {
            int norm = RiichiTileUtil.Normalize(tileID);
            if (processedNorms.Contains(norm)) continue;
            if (!combinations.Contains($"k{norm}")) continue;
            if (!TurnClock.Current.IsKongCandidate("jiagang", norm)) continue;
            processedNorms.Add(norm);
            options.Add((tileID, new List<int> { tileID, tileID, tileID, tileID }));
        }
        return options;
    }

    private static List<(int targetTile, List<int> displayTiles)> CollectBuzhangOptions(
        List<int> handTiles, List<string> combinations) {
        if (RuleRegistry.ActiveGameState != null
            && RuleRegistry.ActiveGameState.TryCollectKongOptions("buzhang", handTiles, combinations, out var custom))
            return custom;
        var options = new List<(int, List<int>)>();
        var processedNorms = new HashSet<int>();
        foreach (var option in CollectJiagangOptions(handTiles, combinations)) {
            int norm = RiichiTileUtil.Normalize(option.targetTile);
            if (processedNorms.Add(norm)) {
                options.Add(option);
            }
        }
        foreach (var option in CollectAngangOptions(handTiles, combinations)) {
            int norm = RiichiTileUtil.Normalize(option.targetTile);
            if (processedNorms.Add(norm)) {
                options.Add(option);
            }
        }
        return options;
    }

    private void CreateActionCards(List<int> TipsCardsList,string actionType,int targetTile) {
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);

        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        if (targetTile != 0){
            blockClick.targetTile = targetTile;
        }

        foreach (int tile in TipsCardsList){
            blockClick.AddTile(StaticCardPrefab, tile);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(ActionBlockContenter as RectTransform);
    }

    // 生成吃牌候选子按钮：包含两张真实牌 ID（按值升序展示），点击回传对应 chiComboIndex
    private void CreateChiCandidateBlock(string actionType, int comboIndex, int[] pair){
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        blockClick.chiComboIndex = comboIndex;

        int[] sorted = new int[pair.Length];
        System.Array.Copy(pair, sorted, pair.Length);
        System.Array.Sort(sorted, TileIdOrder.Comparer);
        foreach (int tile in sorted){
            blockClick.AddTile(StaticCardPrefab, tile);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(ActionBlockContenter as RectTransform);
    }

}
