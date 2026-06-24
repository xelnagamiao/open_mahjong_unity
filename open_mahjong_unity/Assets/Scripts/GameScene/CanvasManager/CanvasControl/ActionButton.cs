using System.Collections;
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

        // 按钮点击事件
        Button button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("ActionButton: Button component not found!");
            return;
        }
        button.onClick.AddListener(OnClick);
    }

    // 判定当前按钮是否为吃牌按钮
    private bool IsChiButton(){
        foreach (string a in actionTypeList){
            if (a == "chi_left" || a == "chi_mid" || a == "chi_right") return true;
        }
        return false;
    }

    // 汇总当前吃牌按钮所有方向的候选组合
    // 每项：(actionType, chiComboIndex, [tileA, tileB]) — 两张真实牌 ID（含赤 5 的 105/205/305）
    private List<(string action, int comboIndex, int[] pair)> CollectChiCandidates(){
        List<(string, int, int[])> result = new List<(string, int, int[])>();
        int lastCutTile = NormalGameStateManager.Instance.lastCutCardID;
        var chiCandidates = NormalGameStateManager.Instance.chiCandidates;
        foreach (string action in actionTypeList){
            if (action != "chi_left" && action != "chi_mid" && action != "chi_right") continue;
            int[][] pairs;
            if (chiCandidates != null && chiCandidates.TryGetValue(action, out pairs) && pairs != null && pairs.Length > 0){
                for (int i = 0; i < pairs.Length; i++){
                    result.Add((action, i, pairs[i]));
                }
            } else {
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
        // 立直按钮：进入立直选牌模式（隐藏其他按钮、按候选切牌变暗手牌、点击切牌发送 riichi_cut）
        if (actionTypeList.Count == 1 && actionTypeList[0] == "riichi_cut") {
            RiichiCutSelectionController.Instance.EnterRiichiCutMode();
            return;
        }
        bool isChi = IsChiButton();
        List<(string action, int comboIndex, int[] pair)> chiCands = isChi ? CollectChiCandidates() : null;
        bool isAngang = actionTypeList.Contains("angang");
        bool isJiagang = actionTypeList.Contains("jiagang");
        var gsm = NormalGameStateManager.Instance;
        var angangOptions = isAngang ? CollectAngangOptions(gsm.selfHandTiles) : null;
        var jiagangOptions = isJiagang
            ? CollectJiagangOptions(gsm.selfHandTiles, gsm.player_to_info["self"].combination_tiles)
            : null;

        // 展开条件：多种吃牌候选，或手牌中实际存在多种暗杠/加杠目标。
        // 服务端会对每个可杠牌各下发一条 "angang"/"jiagang"，不能按 actionTypeList 条数展开。
        bool expandSubButtons = (isChi && chiCands.Count > 1)
            || (angangOptions != null && angangOptions.Count > 1)
            || (jiagangOptions != null && jiagangOptions.Count > 1);

        if (expandSubButtons){
            string currentButtonType = "None";
            if (isChi){
                currentButtonType = "chi";
            } else if (actionTypeList.Contains("angang")){
                currentButtonType = "angang";
            } else if (actionTypeList.Contains("jiagang")){
                currentButtonType = "jiagang";
            }

            if (GameCanvas.Instance.ActionBlockContainerState == "None"){
                GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
            }
            else if (GameCanvas.Instance.ActionBlockContainerState != "None"){
                foreach (Transform child in ActionBlockContenter){
                    Destroy(child.gameObject);
                }
                if (GameCanvas.Instance.ActionBlockContainerState == currentButtonType){
                    GameCanvas.Instance.ActionBlockContainerState = "None";
                    return;
                }
                else {
                    GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
                }
            }

            if (isChi){
                foreach (var cand in chiCands){
                    CreateChiCandidateBlock(cand.action, cand.comboIndex, cand.pair);
                }
            } else {
                if (angangOptions != null) {
                    foreach (var angangOption in angangOptions) {
                        CreateActionCards(angangOption.displayTiles, "angang", angangOption.targetTile);
                    }
                }
                if (jiagangOptions != null) {
                    foreach (var jiagangOption in jiagangOptions) {
                        CreateActionCards(jiagangOption.displayTiles, "jiagang", jiagangOption.targetTile);
                    }
                }
            }
            return;
        }

        // 单一候选：直接发送
        if (isChi){
            var only = chiCands[0];
            Debug.Log($"选择了行动 {only.action} chiComboIndex={only.comboIndex}");
            GameCanvas.Instance.ChooseAction(only.action, 0, only.comboIndex);
            return;
        }

        if (actionTypeList[0] == "jiagang"){
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            int targetTile = jiagangOptions != null && jiagangOptions.Count > 0 ? jiagangOptions[0].targetTile : 0;
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

    private static List<(int targetTile, List<int> displayTiles)> CollectAngangOptions(List<int> handTiles) {
        var options = new List<(int, List<int>)>();
        var processedNorms = new HashSet<int>();
        var gsm = NormalGameStateManager.Instance;
        int dingqueSuit = gsm != null && gsm.IsSichuanRule() ? gsm.selfDingqueSuit : 0;
        foreach (int tileID in handTiles) {
            int norm = RiichiTileUtil.Normalize(tileID);
            if (processedNorms.Contains(norm)) continue;
            processedNorms.Add(norm);
            if (dingqueSuit >= 1 && dingqueSuit <= 3 && norm / 10 == dingqueSuit) continue;
            if (GameRecordMeldCodec.CountNormalizedTiles(handTiles, norm) != 4) continue;
            var actualTiles = handTiles.Where(t => RiichiTileUtil.Normalize(t) == norm).ToList();
            options.Add((norm, actualTiles));
        }
        return options;
    }

    private static List<(int targetTile, List<int> displayTiles)> CollectJiagangOptions(
        List<int> handTiles, List<string> combinations) {
        var options = new List<(int, List<int>)>();
        var processedNorms = new HashSet<int>();
        foreach (int tileID in handTiles) {
            int norm = RiichiTileUtil.Normalize(tileID);
            if (processedNorms.Contains(norm)) continue;
            if (!combinations.Contains($"k{norm}")) continue;
            processedNorms.Add(norm);
            options.Add((tileID, new List<int> { tileID, tileID, tileID, tileID }));
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
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
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
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(ActionBlockContenter as RectTransform);
    }

}

