using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// 轮数面板总控制器（1 管 3）：
/// - SimpleRoundPanel / IndicatorRoundPanel：普通局况或有公开翻牌的局况
/// - CommitmentSaltDetailPanel：点击本面板区域后淡入展示承诺值与盐，3 秒后隐藏
/// </summary>
public partial class RoundPanel : MonoBehaviour, IPointerClickHandler {
    public static RoundPanel Instance { get; private set; }

    [Header("子面板引用")]
    [SerializeField] private SimpleRoundPanel simpleRoundPanel;
    [FormerlySerializedAs("riichiRoundPanel")]
    [SerializeField] private IndicatorRoundPanel indicatorRoundPanel;
    [SerializeField] private CommitmentSaltDetailPanel commitmentSaltDetailPanel;

    [SerializeField] private TableHudMatchInfo matchInfo;

    private string _cachedCommitment = "";
    private string _cachedSalt = "";
    private string _cachedMasterSeed = "";
    private RuleManifest displayedManifest;
    private GameInfo displayedInfo;
    private string displayedRule;
    private bool pairedDuplicate;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        RegisterClickRelay(simpleRoundPanel != null ? simpleRoundPanel.gameObject : null);
        RegisterClickRelay(indicatorRoundPanel != null ? indicatorRoundPanel.gameObject : null);
    }

    private const float CommitmentSaltDisplaySeconds = 3f;

    private void OnDisable() {
        ConfigManager.OnLanguageChanged -= RefreshLocalizedLayout;
        CancelCommitmentSaltDetail();
    }

    /// <summary>
    /// 初始化轮数面板。规则只提供本局已知的牌、待翻槽及局况数据。
    /// </summary>
    public void UpdateRoomInfo(GameInfo gameInfo, string roomRule, string masterSeed = null) {
        // The snapshot is authoritative. The manager's cached sub-rule can still
        // belong to the previous table during the first UI frame after a switch.
        string snapshotRule = !string.IsNullOrEmpty(gameInfo?.sub_rule)
            ? gameInfo.sub_rule : gameInfo?.room_rule;
        if (!string.IsNullOrEmpty(snapshotRule)) roomRule = snapshotRule;
        else if (roomRule == null) roomRule = "";
        pairedDuplicate = gameInfo?.duplicate_match != null;
        CancelCommitmentSaltDetail();
        _cachedCommitment = gameInfo?.commitment ?? "";
        _cachedSalt = gameInfo?.salt ?? "";
        _cachedMasterSeed = masterSeed ?? "";
        commitmentSaltDetailPanel?.SetContent(_cachedCommitment, _cachedSalt, _cachedMasterSeed);

        displayedInfo = gameInfo;
        displayedRule = roomRule;
        displayedManifest = ResolveManifest(gameInfo, roomRule);
        ShowIndicators(displayedManifest?.RoundIndicators?.Invoke(gameInfo), initialize: true);
        matchInfo?.RefreshInfo(gameInfo);
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        RequestShowCommitmentSaltDetail();
    }

    /// <summary>
    /// 点击 RoundPanel 区域：淡入展示承诺值与盐值详情，3 秒后隐藏；重复点击重置计时。
    /// </summary>
    public void RequestShowCommitmentSaltDetail() {
        if (pairedDuplicate || commitmentSaltDetailPanel == null) return;
        commitmentSaltDetailPanel.SetContent(_cachedCommitment, _cachedSalt, _cachedMasterSeed);
        commitmentSaltDetailPanel.ShowWithFadeIn();
        CoroutineManager.Ensure();
        CoroutineManager.Instance?.RunNamed(
            CoroutineKeys.CommitmentSaltHide,
            HideCommitmentSaltAfterDelay(),
            restartIfRunning: true
        );
    }

    private void CancelCommitmentSaltDetail() {
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.CommitmentSaltHide);
        commitmentSaltDetailPanel?.HideImmediate();
    }

    private IEnumerator HideCommitmentSaltAfterDelay() {
        yield return new WaitForSecondsRealtime(CommitmentSaltDisplaySeconds);
        commitmentSaltDetailPanel?.HideImmediate();
    }

    /// <summary>
    /// 实战、重连、牌谱推演使用同一入口；切换规则后忽略上一规则的清理通知。
    /// </summary>
    public void RefreshIndicators(string sourceRule, RoundIndicatorDisplay display) {
        if (displayedManifest?.RoundIndicators == null ||
            (sourceRule != displayedInfo?.sub_rule && sourceRule != displayedManifest.RuleId)) return;
        ShowIndicators(display, initialize: false);
    }

    private void ShowIndicators(RoundIndicatorDisplay display, bool initialize) {
        bool hasTiles = display != null && display.Tiles.Count > 0;
        if (initialize || (indicatorRoundPanel && indicatorRoundPanel.gameObject.activeSelf != hasTiles)) {
            if (simpleRoundPanel) simpleRoundPanel.gameObject.SetActive(!hasTiles);
            if (indicatorRoundPanel) indicatorRoundPanel.gameObject.SetActive(hasTiles);
            if (hasTiles) indicatorRoundPanel?.UpdateRoomInfo(displayedInfo, displayedRule, displayedManifest);
            else simpleRoundPanel?.UpdateRoomInfo(displayedInfo, displayedRule);
        }
        if (hasTiles) indicatorRoundPanel?.RefreshIndicators(display);
        matchInfo?.SetStatus(display?.Status);
        RefreshLocalizedLayout();
    }

    public void UpdateMatchReplayScores(MatchReplayProjection.Scores scores) => simpleRoundPanel?.UpdateMatchReplayScores(scores);

    public void UpdateDuplicateStatus(DuplicateMatchInfo info) => simpleRoundPanel?.UpdateDuplicateStatus(info);

    public void UpdateSichuanSettlement(ShowResultInfo info) => simpleRoundPanel?.UpdateSichuanSettlement(info);

    private static void RegisterClickRelay(GameObject target) {
        if (target == null) return;
        if (target.GetComponent<RoundPanelClickRelay>() == null) {
            target.AddComponent<RoundPanelClickRelay>();
        }
    }

    internal static RuleManifest ResolveManifest(GameInfo gameInfo, string roomRule) =>
        (!string.IsNullOrEmpty(gameInfo?.sub_rule) ? RuleRegistry.Resolve(gameInfo.room_rule, gameInfo.sub_rule) : null)
        ?? RuleRegistry.Resolve(roomRule, roomRule);
}

/// <summary>
/// 将子面板上的点击转发给 RoundPanel；由 RoundPanel 在 Awake 时自动挂载，无需手动配置。
/// </summary>
internal class RoundPanelClickRelay : MonoBehaviour, IPointerClickHandler {
    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        RoundPanel.Instance?.RequestShowCommitmentSaltDetail();
    }
}
