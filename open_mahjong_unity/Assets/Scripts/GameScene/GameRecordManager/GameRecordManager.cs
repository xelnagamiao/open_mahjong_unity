using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 极简牌谱管理器：
/// - 所有 JSON 解析与巡目分割逻辑在 GameRecordJsonDecoder 中完成；
/// - 本类只保存解析结果数据，供其它系统读取与驱动回放。
/// </summary>
public class GameRecordManager : MonoBehaviour {

    [SerializeField] private Button nextXunmuButton;
    [SerializeField] private Button backXunmuButton;
    [SerializeField] private Button nextStepButton;
    [SerializeField] private Button backStepButton;

    [SerializeField] private Button showGameRoundContentButton;
    [SerializeField] private Button showXunmuContentButton;

    [SerializeField] private Button showTileListButton;
    [SerializeField] private Button showGameInfoButton;
    [SerializeField] private Button showRoundInfoButton;
    



    public static GameRecordManager Instance { get; private set; }

    private void Awake(){
        if (Instance == null){
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 所有局的 ActionNode 列表。
    /// 外层索引：round 序号（从 0 开始，对应 round_index_1、2、...）。
    /// 内层元素：形如 ["cut", "55", "true"] 的字符串列表，对应一条 action_ticks 记录（不包含 "Next"）。
    /// </summary>
    public readonly List<List<List<string>>> gameRoundActionNodeList =
        new List<List<List<string>>>();

    /// <summary>
    /// 每局的「巡目 → ActionNode 起始索引」映射。
    /// 外层索引：round 序号（从 0 开始）。
    /// 字典 key：巡目索引（0 开始，按 "Next" 分割得到）。
    /// 字典 value：在该局 action node 列表中的起始下标。
    /// </summary>
    public readonly List<Dictionary<int, int>> gameRoundXunmuToActionIndex =
        new List<Dictionary<int, int>>();


    public void HideGameRecord(){
        gameObject.SetActive(false);
    }

    public void LoadRecord(string recordJson){
        LoadAllRounds(recordJson);
        GameSceneUIManager.Instance.ClearTemporaryPanels();
        gameObject.SetActive(true);
    }

    public void LoadAllRounds(string recordJson){
        gameRoundActionNodeList.Clear();
        gameRoundXunmuToActionIndex.Clear();
        GameRecordJsonDecoder.ParseAllRoundsFromJson(
            recordJson,
            gameRoundActionNodeList,
            gameRoundXunmuToActionIndex
        );

        // 调试输出：打印所有 round 的 action 列表与巡目映射
        Debug.Log($"[GameRecordManager] 解析完成，round 数量: {gameRoundActionNodeList.Count}");
        for (int r = 0; r < gameRoundActionNodeList.Count; r++){
            var roundActions = gameRoundActionNodeList[r];
            var xunmuMap = gameRoundXunmuToActionIndex.Count > r
                ? gameRoundXunmuToActionIndex[r]
                : null;

            Debug.Log($"[GameRecordManager] round {r} action 数量: {roundActions.Count}");

            if (xunmuMap != null){
                foreach (var kv in xunmuMap){
                    Debug.Log($"[GameRecordManager] round {r} 巡目 {kv.Key} 起始 actionIndex = {kv.Value}");
                }
            }
        }
    }


    // 
    private void InitRecord(){
        
    }
}
