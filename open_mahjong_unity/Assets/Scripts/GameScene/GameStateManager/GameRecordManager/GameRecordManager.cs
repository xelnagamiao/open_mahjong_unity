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

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 结构化的牌谱数据（推荐使用）
    /// </summary>
    public GameRecord gameRecord { get; private set; }

    /// <summary>
    /// 用于在 Inspector 中显示牌谱数据（运行时可见）
    /// </summary>
    [SerializeField] private GameRecord _gameRecordInspector;

    /// <summary>
    /// 用于在 Inspector 中显示所有局（Dictionary 在 Inspector 中不可见，所以用 List）
    /// </summary>
    [SerializeField] private List<Round> _roundsListForInspector;

    /// <summary>
    /// 所有局的 ActionNode 列表（向后兼容，已废弃，请使用 gameRecord）。
    /// 外层索引：round 序号（从 0 开始，对应 round_index_1、2、...）。
    /// 内层元素：形如 ["cut", "55", "true"] 的字符串列表，对应一条 action_ticks 记录（不包含 "Next"）。
    /// </summary>
    [System.Obsolete("请使用 gameRecord 属性获取结构化的牌谱数据")]
    public readonly List<List<List<string>>> gameRoundActionNodeList =
        new List<List<List<string>>>();

    /// <summary>
    /// 每局的「巡目 → ActionNode 起始索引」映射（向后兼容，已废弃，请使用 gameRecord）。
    /// 外层索引：round 序号（从 0 开始）。
    /// 字典 key：巡目索引（0 开始，按 "Next" 分割得到）。
    /// 字典 value：在该局 action node 列表中的起始下标。
    /// </summary>
    [System.Obsolete("请使用 gameRecord 属性获取结构化的牌谱数据")]
    public readonly List<Dictionary<int, int>> gameRoundXunmuToActionIndex =
        new List<Dictionary<int, int>>();

    public void HideGameRecord() {
        gameObject.SetActive(false);
    }

    public void LoadRecord(string recordJson) {
        LoadAllRounds(recordJson);
        GameSceneUIManager.Instance.ClearTemporaryPanels();
        gameObject.SetActive(true);
    }

    public void LoadAllRounds(string recordJson) {
        gameRecord = GameRecordJsonDecoder.ParseGameRecord(recordJson);
        _gameRecordInspector = gameRecord;
        _roundsListForInspector = gameRecord.gameRound.GetRoundsList();

        foreach (var round in _roundsListForInspector) {
            round.UpdateActionTicksDisplay();
        }
    }
}
