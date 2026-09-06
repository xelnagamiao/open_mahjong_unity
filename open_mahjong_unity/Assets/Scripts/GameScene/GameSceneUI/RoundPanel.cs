using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 轮数面板总控制器（1 管 3）：
/// - SimpleRoundPanel / RiichiRoundPanel：常规局况，二选一显示
/// - CommitmentSaltDetailPanel：点击本面板区域后淡入展示承诺值与盐，3 秒后隐藏
/// </summary>
public class RoundPanel : MonoBehaviour, IPointerClickHandler {
    public static RoundPanel Instance { get; private set; }

    [Header("子面板引用")]
    [SerializeField] private SimpleRoundPanel simpleRoundPanel;
    [SerializeField] private RiichiRoundPanel riichiRoundPanel;
    [SerializeField] private CommitmentSaltDetailPanel commitmentSaltDetailPanel;

    private string _cachedCommitment = "";
    private string _cachedSalt = "";
    private string _cachedMasterSeed = "";

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        RegisterClickRelay(simpleRoundPanel != null ? simpleRoundPanel.gameObject : null);
        RegisterClickRelay(riichiRoundPanel != null ? riichiRoundPanel.gameObject : null);
    }

    private const float CommitmentSaltDisplaySeconds = 3f;

    private void OnDisable() {
        CancelCommitmentSaltDetail();
    }

    /// <summary>
    /// 初始化轮数面板：根据房间规则切换 Simple / Riichi。
    /// </summary>
    public void UpdateRoomInfo(GameInfo gameInfo, string roomRule, string masterSeed = null) {
        _cachedCommitment = gameInfo?.commitment ?? "";
        _cachedSalt = gameInfo?.salt ?? "";
        _cachedMasterSeed = masterSeed ?? "";
        commitmentSaltDetailPanel?.SetContent(_cachedCommitment, _cachedSalt, _cachedMasterSeed);

        bool useRiichi = IsRiichiLayout(gameInfo, roomRule);
        if (simpleRoundPanel != null) simpleRoundPanel.gameObject.SetActive(!useRiichi);
        if (riichiRoundPanel != null) riichiRoundPanel.gameObject.SetActive(useRiichi);

        if (useRiichi) {
            riichiRoundPanel?.UpdateRoomInfo(gameInfo, roomRule);
        } else {
            simpleRoundPanel?.UpdateRoomInfo(gameInfo, roomRule);
        }
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        RequestShowCommitmentSaltDetail();
    }

    /// <summary>
    /// 点击 RoundPanel 区域：淡入展示承诺值与盐值详情，3 秒后隐藏；重复点击重置计时。
    /// </summary>
    public void RequestShowCommitmentSaltDetail() {
        if (commitmentSaltDetailPanel == null) return;
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
    /// 立直房间的场况刷新（本场、立直棒、宝牌指示牌）。
    /// 杠宝牌指示追加到 doraIndicators 末尾即可。
    /// </summary>
    public void RefreshRiichi(int honba, int riichiSticks, List<int> doraIndicators, List<int> kanDoraIndicators) {
        if (riichiRoundPanel == null || !riichiRoundPanel.gameObject.activeSelf) return;
        var merged = doraIndicators != null ? new List<int>(doraIndicators) : new List<int>();
        if (kanDoraIndicators != null) merged.AddRange(kanDoraIndicators);
        riichiRoundPanel.RefreshFieldState(honba, riichiSticks, merged);
    }

    private static void RegisterClickRelay(GameObject target) {
        if (target == null) return;
        if (target.GetComponent<RoundPanelClickRelay>() == null) {
            target.AddComponent<RoundPanelClickRelay>();
        }
    }

    private static bool IsRiichiLayout(GameInfo gameInfo, string roomRule) {
        // roomRule 参数可能是 "riichi/xxx" 这种子规则串，也按前缀解析
        RuleManifest manifest = RuleRegistry.Resolve(roomRule, roomRule)
            ?? (gameInfo != null ? RuleRegistry.Resolve(gameInfo.room_rule, gameInfo.sub_rule) : null);
        return manifest != null && manifest.RiichiRoundLayout;
    }
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
