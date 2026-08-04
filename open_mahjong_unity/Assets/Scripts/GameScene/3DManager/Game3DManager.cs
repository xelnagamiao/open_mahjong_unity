using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class Game3DManager : MonoBehaviour {
    [SerializeField] private GameObject tile3DPrefab;    // 卡牌 3D预制体
    [SerializeField] private GameObject riichiTenbouPrefab; // 立直点棒（1000）3D预制体
    [SerializeField] private GameObject claimTileGlowPrefab; // 吃碰杠询问时对可操作牌底部生成的光圈（可空，空时走 Resources 兜底）
    [Header("吃碰杠光圈设置")]
    [SerializeField] private Color claimGlowColor = new Color(0.3f, 1f, 0.5f, 0.55f);
    [SerializeField, Range(0f, 4f)] private float claimGlowIntensity = 1f;
    [SerializeField, Range(0f, 2f)] private float claimGlowSpread = 0.55f;       // 光环扩散距离，相对牌宽
    [SerializeField, Range(0f, 0.5f)] private float claimGlowPulseAmount = 0.1f;
    [SerializeField, Range(0f, 8f)] private float claimGlowPulseSpeed = 2.2f;
    [SerializeField, Range(0f, 0.3f)] private float claimGlowCornerRadius = 0.12f; // 圆角半径，相对牌宽
    [SerializeField, Range(-0.5f, 1.5f)] private float claimGlowHeightOffset = 0.12f; // 光圈相对牌背（顶部）的向下偏移，以牌厚为比例，适配牌缩放

    // 吃碰杠光圈实时调参：Inspector 改动由 ClaimTileGlow 每帧拉取，下一帧立即生效。
    public Color ClaimGlowColor => claimGlowColor;
    public float ClaimGlowIntensity => claimGlowIntensity;
    public float ClaimGlowSpread => claimGlowSpread;
    public float ClaimGlowPulseAmount => claimGlowPulseAmount;
    public float ClaimGlowPulseSpeed => claimGlowPulseSpeed;
    public float ClaimGlowCornerRadius => claimGlowCornerRadius;
    public float ClaimGlowHeightOffset => claimGlowHeightOffset;
    [Header("3D位置面板")]
    [SerializeField] private PosPanel3D selfPosPanel;    // 自家位置面板
    [SerializeField] private PosPanel3D leftPosPanel;    // 左家位置面板
    [SerializeField] private PosPanel3D topPosPanel;     // 对家位置面板
    [SerializeField] private PosPanel3D rightPosPanel;    // 右家位置面板

    private Vector3 selfSetCombinationsPoint; // 组合指针用于存储各家组合牌生成位置
    private Vector3 leftSetCombinationsPoint;
    private Vector3 topSetCombinationsPoint;
    private Vector3 rightSetCombinationsPoint;
    /// <summary>各家上一组副露末张沿排布方向的槽宽；跨组起步用半槽累加，避免吃后再吃叠牌。</summary>
    private readonly Dictionary<string, float> _combinationLastSlotWidth = new Dictionary<string, float> {
        { "self", 0f }, { "left", 0f }, { "top", 0f }, { "right", 0f },
    };

    // 出牌飞行动画协程按玩家隔离：避免 A 的飞牌协程被 B 的鸣牌 StopCoroutine 误终止，
    // 也避免多家并发出牌时互相覆盖引用造成河牌停在中途或被错误回收。
    private readonly Dictionary<string, Coroutine> _discardMoveCoroutinesByPlayer = new Dictionary<string, Coroutine>();

    // 删除手牌的位置按玩家隔离：出牌飞牌起点只取本家删牌位，避免他家并发删牌覆盖起点导致牌飞错河。
    private readonly Dictionary<string, Vector3> _lastRemovePosByPlayer = new Dictionary<string, Vector3> {
        { "self", Vector3.zero }, { "left", Vector3.zero },
        { "top", Vector3.zero }, { "right", Vector3.zero },
    };

    // 每家「最新弃牌」的 3D 对象 + 牌 id：在 Discard / DiscardWithoutAnimation Spawn 时登记。
    // 鸣牌认走弃牌时优先用这个确切对象（校验 tileId），避免河里同类牌歧义（两张 7p 认错旧的）。
    private readonly Dictionary<string, GameObject> _lastDiscardObjByPlayer = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> _lastDiscardTileIdByPlayer = new Dictionary<string, int>();

    // 每家「最新加杠牌」的 3D 对象 + 牌 id：加杠动画放置时登记，供抢杠/荣和取牌（按玩家隔离，避免全局覆盖）。
    private readonly Dictionary<string, GameObject> _lastJiagangObjByPlayer = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> _lastJiagangTileIdByPlayer = new Dictionary<string, int>();

    // 每家「最新可操作牌」的登记序号：出牌/加杠时递增，供吃碰杠询问定位光圈目标，
    // 避免场上存在同点数弃牌/加杠牌时认错张。
    private readonly Dictionary<string, long> _claimTargetSequenceByPos = new Dictionary<string, long>();
    private long _claimTargetSequenceCounter;
    private ClaimTileGlow _claimTileGlow;
    private Coroutine _claimGlowRetryCoroutine;
    private bool _claimGlowPrefabMissingLogged;

    private void RegisterLastDiscard(string playerPosition, GameObject obj, int tileId) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        _lastDiscardObjByPlayer[playerPosition] = obj;
        _lastDiscardTileIdByPlayer[playerPosition] = tileId;
        _claimTargetSequenceByPos[playerPosition] = ++_claimTargetSequenceCounter;
    }

    private void ClearLastDiscard(string playerPosition) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        _lastDiscardObjByPlayer[playerPosition] = null;
        _claimTargetSequenceByPos[playerPosition] = 0;
    }

    private void RegisterLastJiagang(string playerPosition, GameObject obj, int tileId) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        _lastJiagangObjByPlayer[playerPosition] = obj;
        _lastJiagangTileIdByPlayer[playerPosition] = tileId;
        _claimTargetSequenceByPos[playerPosition] = ++_claimTargetSequenceCounter;
    }

    private void ClearLastJiagang(string playerPosition) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        _lastJiagangObjByPlayer[playerPosition] = null;
        _claimTargetSequenceByPos[playerPosition] = 0;
    }

    private void ClearLastJiagangIfMatches(string playerPosition, GameObject obj) {
        if (string.IsNullOrEmpty(playerPosition) || obj == null) return;
        if (_lastJiagangObjByPlayer.TryGetValue(playerPosition, out GameObject reg) && reg == obj) {
            ClearLastJiagang(playerPosition);
        }
    }

    private void ClearAllLastDiscardAndJiagang() {
        foreach (string pos in new[] { "self", "left", "top", "right" }) {
            ClearLastDiscard(pos);
            ClearLastJiagang(pos);
            _lastDiscardTileIdByPlayer[pos] = 0;
            _lastJiagangTileIdByPlayer[pos] = 0;
        }
    }

    /// <summary>
    /// 吃碰杠询问：给最新“可操作牌”（弃牌或加杠牌）底部挂上光圈。
    /// expectedTileId 为服务端下发的切牌/杠牌 id；>0 时仅匹配该牌张，避免场上同点数歧义。
    /// </summary>
    public void ShowClaimGlow(int expectedTileId = 0) {
        if (_claimGlowRetryCoroutine != null) StopCoroutine(_claimGlowRetryCoroutine);
        _claimGlowRetryCoroutine = StartCoroutine(ClaimGlowRetryCoroutine(expectedTileId));
    }

    /// <summary>吃碰杠询问结束：隐藏可操作牌底部的光圈。</summary>
    public void HideClaimGlow() {
        if (_claimGlowRetryCoroutine != null) {
            StopCoroutine(_claimGlowRetryCoroutine);
            _claimGlowRetryCoroutine = null;
        }
        if (_claimTileGlow != null) _claimTileGlow.Hide();
    }

    /// <summary>
    /// 询问弹出时弃牌 3D 对象可能仍在动画队列中尚未登记，轮询等待目标出现后再挂光圈；
    /// 目标出现后继续每帧复查，防止延迟登记的新弃牌覆盖旧牌时挂在旧牌上。
    /// </summary>
    private IEnumerator ClaimGlowRetryCoroutine(int expectedTileId) {
        bool loggedWaiting = false;
        float deadline = Time.unscaledTime + 5f;
        while (Time.unscaledTime < deadline) {
            GameObject target = FindLatestClaimTarget(expectedTileId);
            if (target != null) {
                ClaimTileGlow glow = EnsureClaimGlowInstance();
                if (glow != null) glow.AttachTo(target);
                loggedWaiting = false;
            } else if (!loggedWaiting) {
                loggedWaiting = true;
                Debug.Log($"ClaimTileGlow 等待弃牌/杠牌 3D 对象登记: expectedTileId={expectedTileId}");
            }
            yield return null;
        }
        _claimGlowRetryCoroutine = null;
        Debug.Log("ClaimTileGlow 5 秒内未等到可操作牌登记，放弃显示");
    }

    private ClaimTileGlow EnsureClaimGlowInstance() {
        if (_claimTileGlow != null) return _claimTileGlow;
        GameObject prefab = claimTileGlowPrefab;
        if (prefab == null) {
            // 场景引用未配置或场景未重载时，回退到 Resources 运行时加载，保证光圈始终可用。
            prefab = Resources.Load<GameObject>("Effects/ClaimTileGlow/ClaimTileGlow");
        }
        if (prefab == null) {
            if (!_claimGlowPrefabMissingLogged) {
                _claimGlowPrefabMissingLogged = true;
                Debug.LogWarning("Game3DManager 未配置 claimTileGlowPrefab 且 Resources 加载失败，吃碰杠光圈不可用");
            }
            return null;
        }
        _claimTileGlow = Instantiate(prefab).GetComponent<ClaimTileGlow>();
        if (_claimTileGlow == null) {
            Debug.LogError("ClaimTileGlow prefab 缺少 ClaimTileGlow 组件");
            return null;
        }
        _claimTileGlow.gameObject.SetActive(false);
        return _claimTileGlow;
    }

    /// <summary>在所有登记中找“最新”且（可选）牌张匹配的可操作牌对象。</summary>
    private GameObject FindLatestClaimTarget(int expectedTileId) {
        string bestPos = null;
        bool bestIsJiagang = false;
        long bestSeq = -1;
        foreach (KeyValuePair<string, GameObject> kvp in _lastDiscardObjByPlayer) {
            string pos = kvp.Key;
            if (kvp.Value == null || !kvp.Value.activeInHierarchy) continue;
            if (!_claimTargetSequenceByPos.TryGetValue(pos, out long seq) || seq <= 0) continue;
            if (!_lastDiscardTileIdByPlayer.TryGetValue(pos, out int tileId)) continue;
            if (expectedTileId > 0 && !TilesMatchForDiscardLookup(tileId, expectedTileId)) continue;
            if (seq > bestSeq) { bestSeq = seq; bestPos = pos; bestIsJiagang = false; }
        }
        foreach (KeyValuePair<string, GameObject> kvp in _lastJiagangObjByPlayer) {
            string pos = kvp.Key;
            if (kvp.Value == null || !kvp.Value.activeInHierarchy) continue;
            if (!_claimTargetSequenceByPos.TryGetValue(pos, out long seq) || seq <= 0) continue;
            if (!_lastJiagangTileIdByPlayer.TryGetValue(pos, out int tileId)) continue;
            if (expectedTileId > 0 && !TilesMatchForDiscardLookup(tileId, expectedTileId)) continue;
            if (seq > bestSeq) { bestSeq = seq; bestPos = pos; bestIsJiagang = true; }
        }
        if (bestPos == null) return null;
        return bestIsJiagang ? _lastJiagangObjByPlayer[bestPos] : _lastDiscardObjByPlayer[bestPos];
    }

    /// <summary>登记 tileId 与期望牌张是否一致（含赤宝 105/15 归一化）。期望 id 无效则不匹配。</summary>
    private static bool TilesMatchForDiscardLookup(int registeredTileId, int expectedTileId) {
        if (expectedTileId <= 0) return false;
        if (registeredTileId == expectedTileId) return true;
        return GameRecordMeldCodec.NormalizeMeldsLookupTileId(registeredTileId)
            == GameRecordMeldCodec.NormalizeMeldsLookupTileId(expectedTileId);
    }

    /// <summary>
    /// 认走/荣和取走弃牌时，仅认 Discard 分支 RegisterLastDiscard 登记的对象。
    /// 不扫描河牌；认不到返回 null，由调用方打日志并放弃。
    /// </summary>
    private GameObject ResolveLastDiscardObject(string discarderPos, int expectedTileId) {
        if (string.IsNullOrEmpty(discarderPos)) return null;
        if (!_lastDiscardObjByPlayer.TryGetValue(discarderPos, out GameObject regObj)
            || regObj == null || !regObj.activeInHierarchy) {
            return null;
        }
        if (!_lastDiscardTileIdByPlayer.TryGetValue(discarderPos, out int regTile)) {
            return null;
        }
        if (!TilesMatchForDiscardLookup(regTile, expectedTileId)) {
            return null;
        }
        return regObj;
    }

    /// <summary>
    /// 抢杠/加杠和牌张：副露区 id 匹配，或该玩家加杠动画登记对象（校验 tileId）。
    /// 不扫描河牌；供荣和/抢杠取牌，不用于鸣牌回收河牌。
    /// </summary>
    private GameObject TryResolveJiagangSourceObject(string playerPosition, int expectedTileId) {
        GameObject jiagangObj = FindJiagangTileObject(playerPosition, expectedTileId);
        if (jiagangObj != null) return jiagangObj;
        if (string.IsNullOrEmpty(playerPosition)) return null;
        if (!_lastJiagangObjByPlayer.TryGetValue(playerPosition, out GameObject regObj)
            || regObj == null || !regObj.activeInHierarchy) {
            return null;
        }
        if (!_lastJiagangTileIdByPlayer.TryGetValue(playerPosition, out int regTile)) {
            return null;
        }
        if (!TilesMatchForDiscardLookup(regTile, expectedTileId)) {
            return null;
        }
        return regObj;
    }

    /// <summary>
    /// 牌谱 Goto 重建专用：仅当河末张 id 与和牌张一致时登记，不做全河扫描。
    /// </summary>
    private GameObject TryRegisterRecordRiverLastDiscard(string sourcePlayerPosition, int expectedTileId) {
        PosPanel3D panel = GetPosPanel(sourcePlayerPosition);
        Transform river = panel?.discardsPosition;
        if (river == null || river.childCount == 0) return null;
        GameObject lastObj = river.GetChild(river.childCount - 1).gameObject;
        Tile3D lastTile = lastObj.GetComponent<Tile3D>();
        if (lastTile == null || !TilesMatchForDiscardLookup(lastTile.GetTileId(), expectedTileId)) {
            return null;
        }
        RegisterLastDiscard(sourcePlayerPosition, lastObj, lastTile.GetTileId());
        return lastObj;
    }

    private GameObject FindJiagangTileObject(string playerPosition, int expectedTileId) {
        if (string.IsNullOrEmpty(playerPosition) || expectedTileId < 10) return null;
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel?.combination3DObjects == null) return null;

        GameObject lastMatch = null;
        foreach (Transform comboParent in panel.combination3DObjects) {
            if (comboParent == null) continue;
            for (int i = 0; i < comboParent.childCount; i++) {
                Tile3D tile3D = comboParent.GetChild(i).GetComponent<Tile3D>();
                if (tile3D != null && TilesMatchForDiscardLookup(tile3D.GetTileId(), expectedTileId)) {
                    lastMatch = tile3D.gameObject;
                }
            }
        }
        return lastMatch;
    }

    /// <summary>牌谱 Goto 重建：同步跑完副露放置（无动画），避免加杠 3D 下一帧才生成。</summary>
    public void RunMeldRebuildImmediate(string playerIndex, string actionType, int[] combinationMask) {
        RunCoroutineImmediate(ActionAnimationCoroutine(playerIndex, actionType, combinationMask, false));
    }

    private static void RunCoroutineImmediate(IEnumerator routine) {
        if (routine == null) return;
        var stack = new System.Collections.Generic.Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0) {
            IEnumerator current = stack.Peek();
            if (!current.MoveNext()) {
                stack.Pop();
                continue;
            }
            if (current.Current is IEnumerator nested) {
                stack.Push(nested);
            }
        }
    }

    /// <summary>认走/荣和取走弃牌后：停掉该打牌者飞牌协程并清掉登记，
    /// 避免被取走的牌仍在飞行/后续落到河里，或被后续鸣牌/荣和重复认走。</summary>
    private void OnLastDiscardTaken(string discarderPos) {
        StopDiscardMoveCoroutine(discarderPos);
        ClearLastDiscard(discarderPos);
    }

    private Dictionary<int,Vector3> pengToJiagangPosDict = new Dictionary<int,Vector3>(); // 碰牌的加杠预留指针

    private void SetLastRemovePos(string playerPosition, Vector3 pos) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        _lastRemovePosByPlayer[playerPosition] = pos;
    }

    private Vector3 GetLastRemovePos(string playerPosition) {
        if (string.IsNullOrEmpty(playerPosition)) return Vector3.zero;
        return _lastRemovePosByPlayer.TryGetValue(playerPosition, out Vector3 pos) ? pos : Vector3.zero;
    }

    private void StopDiscardMoveCoroutine(string playerPosition) {
        if (string.IsNullOrEmpty(playerPosition)) return;
        if (_discardMoveCoroutinesByPlayer.TryGetValue(playerPosition, out Coroutine c) && c != null) {
            StopCoroutine(c);
        }
        _discardMoveCoroutinesByPlayer[playerPosition] = null;
    }

    private void StopAllDiscardMoveCoroutines() {
        foreach (var kvp in _discardMoveCoroutinesByPlayer) {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        _discardMoveCoroutinesByPlayer.Clear();
    }

    private float cardWidth; // 卡片宽度 组合牌 3D手牌使用
    private float cardHeight; // 卡片高度
    private float cardScale; // 卡片缩放
    private float widthSpacing; // 弃牌/补花间距
    private float heightSpacing; // 弃牌/补花纵向间距
    private Vector3 RightDirection; // 右方向
    private Vector3 LeftDirection; // 左方向
    private Vector3 FrontDirection; // 前方向
    private Vector3 BackDirection; // 后方向

    public static Game3DManager Instance { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (GetComponent<ConcealedTile3DPeekController>() == null) {
            gameObject.AddComponent<ConcealedTile3DPeekController>();
        }
        ApplyTileOutlineSettings();
        // 初始化配置（Mode B 描边不依赖物理缝；手牌/副露间隙→30%，牌河间隙→80%）
        this.cardScale = tile3DPrefab.transform.localScale.z;
        Renderer tileRenderer = tile3DPrefab.GetComponent<Renderer>();
        float tileW = tileRenderer.bounds.size.x;
        float tileH = tileRenderer.bounds.size.y;
        const float handStepRatio = 1.10f;
        const float handHeightRatio = 1.06f;
        const float riverExtraRatio = 1.06f;
        this.cardWidth = tileW * (1f + (handStepRatio - 1f) * 0.3f);
        this.cardHeight = tileH * (1f + (handHeightRatio - 1f) * 0.3f);
        float riverWidthOrig = tileW * handStepRatio * riverExtraRatio;
        float riverHeightOrig = tileH * handHeightRatio * riverExtraRatio;
        this.widthSpacing = tileW + (riverWidthOrig - tileW) * 0.8f;
        this.heightSpacing = tileH + (riverHeightOrig - tileH) * 0.8f;
        // 初始化放置组合牌指针
        selfSetCombinationsPoint = selfPosPanel.combinationsPosition.position;
        leftSetCombinationsPoint = leftPosPanel.combinationsPosition.position;
        topSetCombinationsPoint = topPosPanel.combinationsPosition.position;
        rightSetCombinationsPoint = rightPosPanel.combinationsPosition.position;
        ResetCombinationLastSlotWidths();
        // 初始化世界位置
        RightDirection = new Vector3(1,0,0);
        LeftDirection = new Vector3(-1,0,0);
        FrontDirection = new Vector3(0,0,1);
        BackDirection = new Vector3(0,0,-1);
        ResetHandRevealAnimators();
    }

    /// <summary>应用玩家描边预设。</summary>
    private void ApplyTileOutlineSettings() {
        ConfigManager.Instance?.ApplyTileOutlinePreset();
    }

    /// <summary>
    /// 重置四家手牌展开动画到 Idle 状态，避免上一局展开末帧影响下一局摆牌。
    /// </summary>
    public void ResetHandRevealAnimators() {
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel }) {
            ForceHandRevealIdle(panel);
        }
    }

    /// <summary>
    /// 立直棒动画：从该位玩家的 outputPos 飞向 tenbouPos；预制体由 inspector 直接绑定 riichiTenbouPrefab。
    /// </summary>
    public void PlayRiichiTenbouFlight(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform startTransform = panel.outputPos != null ? panel.outputPos : panel.cardsPosition;
        Transform endTransform = panel.tenbouPos;
        if (startTransform == null || endTransform == null) {
            Debug.LogWarning($"PosPanel3D[{playerPosition}] 缺少 outputPos 或 tenbouPos，无法播放立直棒飞行动画");
            return;
        }
        if (riichiTenbouPrefab == null) {
            Debug.LogWarning("Game3DManager.riichiTenbouPrefab 未绑定");
            return;
        }
        Quaternion endRot = RiichiTenbouPlacementRotation(playerPosition);
        GameObject tenbou = Instantiate(riichiTenbouPrefab, startTransform.position, endRot);
        tenbou.name = $"RiichiTenbou_{playerPosition}";
        tenbou.transform.SetParent(panel.tenbouPos.parent, worldPositionStays: true);
        StartCoroutine(MoveTenbouCoroutine(tenbou, startTransform.position, endTransform.position, endRot, 0.6f));
    }

    /// <summary>
    /// 重连/牌谱场景：直接把立直点棒摆放到 tenbouPos，不播放飞行动画；用于断线回归后玩家立直状态恢复展示。
    /// 同一位置已有立直棒时不重复创建。
    /// </summary>
    public void PlaceRiichiTenbouAt(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform endTransform = panel.tenbouPos;
        if (endTransform == null || riichiTenbouPrefab == null) return;
        if (panel.tenbouPos.parent != null) {
            for (int i = 0; i < panel.tenbouPos.parent.childCount; i++) {
                Transform t = panel.tenbouPos.parent.GetChild(i);
                if (t != null && t.name.StartsWith("RiichiTenbou_")) return;
            }
        }
        Quaternion endRot = RiichiTenbouPlacementRotation(playerPosition);
        GameObject tenbou = Instantiate(riichiTenbouPrefab, endTransform.position, endRot);
        tenbou.name = $"RiichiTenbou_{playerPosition}";
        tenbou.transform.SetParent(endTransform.parent, worldPositionStays: true);
    }

    /// <summary>立直棒落点朝向：与各家河牌俯视一致，不继承 UI 节点旋转。</summary>
    private Quaternion RiichiTenbouPlacementRotation(string playerPosition) {
        if (playerPosition == "self") return Quaternion.Euler(90, 0, 180);
        if (playerPosition == "left") return Quaternion.Euler(90, 0, 90);
        if (playerPosition == "top") return Quaternion.Euler(90, 0, 0);
        if (playerPosition == "right") return Quaternion.Euler(90, 0, 270);
        return Quaternion.identity;
    }

    /// <summary>清空场上所有立直点棒（每局 Clear3DTile 时调用，避免下一局残留）。</summary>
    public void ClearAllRiichiTenbous() {
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel }) {
            if (panel.tenbouPos == null || panel.tenbouPos.parent == null) continue;
            for (int i = panel.tenbouPos.parent.childCount - 1; i >= 0; i--) {
                Transform t = panel.tenbouPos.parent.GetChild(i);
                if (t != null && (t.name.StartsWith("RiichiTenbou_") || t.name.StartsWith("FieldRiichiTenbou_"))) {
                    Destroy(t.gameObject);
                }
            }
        }
    }

    private IEnumerator MoveTenbouCoroutine(GameObject obj, Vector3 from, Vector3 to, Quaternion endRot, float duration) {
        float t = 0f;
        Quaternion startRot = obj.transform.rotation;
        while (t < duration) {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            obj.transform.position = Vector3.Lerp(from, to, u);
            obj.transform.rotation = Quaternion.Slerp(startRot, endRot, u);
            yield return null;
        }
        obj.transform.position = to;
        obj.transform.rotation = endRot;
    }

    /// <summary>自家手牌立牌朝向（与他家 Get3D 侧视一致）；日麻流局听牌展示时由 Animator 播倒下。</summary>
    private Quaternion SelfHandStandingRotation() {
        return Quaternion.Euler(-180, 180, 0);
    }

    /// <summary>河牌/副露俯视朝向，与各家弃牌区一致。</summary>
    private Quaternion RiverTileWorldRotation(string playerPosition) {
        if (playerPosition == "self") return Quaternion.Euler(90, 0, 180);
        if (playerPosition == "left") return Quaternion.Euler(90, 0, 90);
        if (playerPosition == "top") return Quaternion.Euler(90, 0, 0);
        if (playerPosition == "right") return Quaternion.Euler(90, 0, 270);
        return Quaternion.identity;
    }

    /// <summary>倒牌动画 Cube 节点：cardsPosition 向上查找名为 Cube 的父节点。</summary>
    private Transform FindHandRevealCube(PosPanel3D panel) {
        if (panel == null || panel.cardsPosition == null) return null;
        Transform t = panel.cardsPosition.parent;
        while (t != null) {
            if (t.name == "Cube") return t;
            if (panel.handRevealAnimator != null && t == panel.handRevealAnimator.transform) break;
            t = t.parent;
        }
        return null;
    }

    /// <summary>
    /// 局终/牌谱明牌摆牌初始朝向：Expand 动画将 Cube 绕本地 X 轴 +90°，
    /// 初始世界朝向取「河牌俯视」在该旋转下的逆变换，动画全程牌面与河牌一致。
    /// </summary>
    private Quaternion RecordHandTileRotation(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Quaternion riverFacing = RiverTileWorldRotation(playerPosition);
        Transform cube = FindHandRevealCube(panel);
        if (cube == null || cube.parent == null) return riverFacing;
        Quaternion parentWorld = cube.parent.rotation;
        Quaternion expandRot = Quaternion.Euler(90f, 0f, 0f);
        return parentWorld * Quaternion.Inverse(expandRot) * Quaternion.Inverse(parentWorld) * riverFacing;
    }

    /// <summary>自家牌：仅手牌容器为立面；河、副露、补花仍为俯视位姿。</summary>
    private Quaternion SelfTileWorldRotation(Transform setPosition) {
        if (setPosition == selfPosPanel.cardsPosition) {
            return SelfHandStandingRotation();
        }
        return Quaternion.Euler(90, 0, 180);
    }

    /// <summary>清空自家 3D 手牌区；对局中使用 2D 手牌，不在此处生成白背牌。</summary>
    public void ClearSelf3DHandTiles() {
        if (selfPosPanel == null) return;
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }
    }

    // 3D手牌处理入口：暗杠/加杠与杠后岭上摸牌走各家串行队列；其余走 Change3DTileCoroutine。
    public void Change3DDiscardTiles(int[] tileIds, string PlayerPosition, bool cut_class, bool isRiichi = false, bool playCutPhysicsSound = false) {
        if (tileIds == null || tileIds.Length == 0) {
            return;
        }
        if (tileIds.Length == 1 && !HasPendingHandAnimWork(PlayerPosition)) {
            Change3DTile("Discard", tileIds[0], 0, PlayerPosition, cut_class, null, isRiichi, playCutPhysicsSound: playCutPhysicsSound);
            return;
        }
        for (int i = 0; i < tileIds.Length; i++) {
            EnqueueDiscardHandWork(PlayerPosition, tileIds[i], cut_class, isRiichi, playCutPhysicsSound && i == 0);
        }
    }

    public void Change3DTile(string actionType,int tileId,int removeCount,string PlayerPosition,bool cut_class,int[] combination_mask, bool isRiichi = false, bool isMoGang = false, bool playCutPhysicsSound = false, string meldDiscarderPos = null, int meldClaimedTile = 0){
        // 牌谱重建/重连的无动画分支直接执行，避免队列协程逐帧处理
        if (actionType == "SetDiscardWithoutAnimation" || actionType == "SetBuhuacardWithoutAnimation" || actionType == "SetRecordDiscardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (panel == null) return;

            if (actionType == "SetDiscardWithoutAnimation"){
                Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", PlayerPosition, isMoqie: false, isRiichi: isRiichi);
            }
            else if (actionType == "SetRecordDiscardWithoutAnimation"){
                Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", PlayerPosition, ShouldApplyMoqieDiscardGray(cut_class), isRiichi: isRiichi);
            }
            else {
                Set3DTile(tileId, panel.buhuaPosition, "BuhuaWithoutAnimation", PlayerPosition);
            }
            return;
        }

        // 初始化手牌走同步路径，避免与紧随其后的补花/摸牌广播触发的删/增手牌协程交错，
        // 导致 InitHandCards 在 yield 帧之后才补出 13 张手牌叠加在已增量的牌上。
        if (actionType == "InitHandCards"){
            InitHandCardsImmediate();
            return;
        }

        // 牌谱跳转重建手牌须同步完成，避免 GotoAction 后紧跟 NextStep 和牌倒牌时，
        // 异步 Init 在下一帧用牌背覆盖 PlayHepaiHandReveal 已铺好的明牌（他家）。
        if (actionType == "InitHandCardsFromRecord") {
            InitHandCardsFromRecordImmediate();
            return;
        }

        // 加杠把 isMoGang 传入 cut_class 位；暗杠走命名参数 isMoGang
        bool queueMoGang = actionType == "jiagang" ? cut_class : isMoGang;
        if (TryEnqueueAnkanHandChange(actionType, tileId, removeCount, PlayerPosition, combination_mask, queueMoGang)) {
            return;
        }
        if (actionType == "Discard" && HasPendingHandAnimWork(PlayerPosition)) {
            EnqueueDiscardHandWork(PlayerPosition, tileId, cut_class, isRiichi, playCutPhysicsSound);
            return;
        }

        StartCoroutine(Change3DTileCoroutine(actionType, tileId, removeCount, PlayerPosition, cut_class, combination_mask, isRiichi, playCutPhysicsSound, meldDiscarderPos, meldClaimedTile));
    }

    // 同步初始化各家手牌：清空当前 cardsPosition，按 player_to_info 与 selfHandTiles 立即生成
    private void InitHandCardsImmediate() {
        StopAllHandAnimationQueues();
        ClearAllRiichiTenbous();
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }

        int leftCount = NormalGameStateManager.Instance.player_to_info["left"].hand_tiles_count;
        int topCount = NormalGameStateManager.Instance.player_to_info["top"].hand_tiles_count;
        int rightCount = NormalGameStateManager.Instance.player_to_info["right"].hand_tiles_count;
        for (int i = 0; i < leftCount; i++) {
            Get3DTile("left", "init");
        }
        for (int i = 0; i < topCount; i++) {
            Get3DTile("top", "init");
        }
        for (int i = 0; i < rightCount; i++) {
            Get3DTile("right", "init");
        }
    }

    /// <summary>牌谱 GotoAction / 切换明牌展示：同步清空并重建 left/top/right 手牌区。</summary>
    private void InitHandCardsFromRecordImmediate() {
        StopAllHandAnimationQueues();
        ClearRecordHandCardsImmediate();
        RenderRecordPlayerHand("left");
        RenderRecordPlayerHand("top");
        RenderRecordPlayerHand("right");
    }

    private void ClearRecordHandCardsImmediate() {
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(leftPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.ShowCardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            if (obj != null) {
                MahjongObjectPool.Instance.Return(-1, obj);
            }
        }
    }

    public IEnumerator Change3DTileCoroutine(string actionType, int tileId, int removeCount, string PlayerPosition, bool cut_class, int[] combination_mask, bool isRiichi = false, bool playCutPhysicsSound = false, string meldDiscarderPos = null, int meldClaimedTile = 0) {
        PosPanel3D panel = GetPosPanel(PlayerPosition);
        if (panel == null) {
            yield break;
        }

        // 鸣牌保护节奏由服务器驱动：受保护观众的鸣牌帧本身被延迟 gap 后才发出，
        // 客户端到帧即呈现。认走的打牌者+牌张在派发时捕获（meldDiscarderPos/meldClaimedTile），
        // 不读共享字段，避免被后续鸣牌覆盖导致回收错河牌。

        if (actionType == "GetCard") {
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return RecordShowCardGetCoroutine(PlayerPosition, tileId);
                yield break;
            }
            yield return Get3DTileCoroutine(PlayerPosition, "get");
            yield break;
        }

        if (actionType == "Discard" || actionType == "RecordDiscard") {
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return RecordDiscardShowCardsCoroutine(PlayerPosition, tileId, cut_class, isRiichi);
                yield break;
            }
            if (IsSelfCardsPosition(panel.cardsPosition)) {
                yield return RemoveSelfHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null, skipRearrange: true, PlayerPosition);
            }
            else {
                yield return RemoveHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null, skipRearrange: true, PlayerPosition);
            }
            if (playCutPhysicsSound) {
                SoundManager.Instance.PlayPhysicsSound("cut");
            }
            bool moqieGrayOnDiscard = ShouldApplyMoqieDiscardGray(cut_class);
            yield return Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition, moqieGrayOnDiscard, isRiichi: isRiichi);
            if (PlayerPosition != "self" && DiscardSettlePauseSec > 0f) {
                yield return new WaitForSeconds(DiscardSettlePauseSec);
            }
            yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
            yield break;
        }

        if (actionType == "Buhua") {
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return RecordBuhuaShowCardsCoroutine(PlayerPosition, tileId, cut_class);
                yield break;
            }
            if (IsSelfCardsPosition(panel.cardsPosition)) {
                yield return RemoveSelfHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null, skipRearrange: true, PlayerPosition);
            }
            else {
                yield return RemoveHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null, skipRearrange: true, PlayerPosition);
            }
            yield return Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", PlayerPosition);
            // 摸补：摸牌区删花后保留主列与摸牌区间距，不收拢；手补仍收拢主列
            if (!cut_class) {
                yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
            }
            yield break;
        }

        if (IsMeldHandAction(actionType)) {
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                bool removeDrawFirst = (actionType == "angang" || actionType == "jiagang") && cut_class;
                yield return RecordMeldShowCardsCoroutine(PlayerPosition, actionType, combination_mask, removeDrawFirst, tileId, meldDiscarderPos, meldClaimedTile);
                yield break;
            }
            // 先等打牌者 cut 完成 RegisterLastDiscard 再回收河牌，避免队列延迟导致认牌失败。
            // 副露动画与删手牌并行（与原 StartMeldPresentation 行为一致）。
            yield return ReturnLastCutTileForMeldCoroutine(actionType, meldDiscarderPos, meldClaimedTile);
            StartCoroutine(ActionAnimationCoroutine(PlayerPosition, actionType, combination_mask, true));
            bool meldCutClass = actionType == "jiagang" && cut_class;
            int meldDiscardId = actionType == "jiagang" && tileId >= 2 ? tileId : -1;
            if (IsSelfCardsPosition(panel.cardsPosition)) {
                yield return RemoveSelfHandCardsCoroutine(panel.cardsPosition, removeCount, meldCutClass, meldDiscardId, combination_mask, skipRearrange: true, PlayerPosition);
            }
            else {
                yield return RemoveHandCardsCoroutine(panel.cardsPosition, removeCount, meldCutClass, meldDiscardId, combination_mask, skipRearrange: true, PlayerPosition);
            }
            yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
        }
    }

    /// <summary>河牌摸切灰显：仅牌谱/观战回放可读 RecordSetting；对局与实时观战不显示。</summary>
    private bool ShouldApplyMoqieDiscardGray(bool cutClass) {
        if (!cutClass) return false;
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr == null || !recordMgr.gameObject.activeSelf || recordMgr.gameRecord == null) {
            return false;
        }
        // 牌谱阅览：以左侧手摸切开关为准
        return RecordSetting.Instance.IsShowMoqieMode;
    }

    // 根据玩家位置获取对应的位置面板
    private PosPanel3D GetPosPanel(string playerPosition){
        switch (playerPosition){
            case "self":
                return selfPosPanel;
            case "left":
                return leftPosPanel;
            case "top":
                return topPosPanel;
            case "right":
                return rightPosPanel;
            default:
                Debug.LogError($"未知的玩家位置: {playerPosition}");
                return null;
        }
    }

    public void RefreshRecordHandDisplay(){
        Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.RefreshRecordChongHint();
        }
    }

    /// <summary>牌谱明牌模式：对 ShowCardsPosition 下的手牌施加铳牌红色遮罩。</summary>
    public void ApplyRecordChongHintToShowHands(
        Dictionary<string, GameRecordManager.RecordPlayer> players,
        string roomRule,
        HashSet<string> hiddenHandPositions,
        IDictionary<string, object> detailedConfig = null) {
        if (players == null) return;
        if (!RecordSetting.Instance.IsShowCardsMode) return;

        Color overlayColor = Card3DHoverManager.Instance.DangerOverlayColor;
        float intensity = Card3DHoverManager.Instance.DangerOverlayIntensity;

        ApplyChongHintForPosition("left", leftPosPanel.ShowCardsPosition, players, roomRule, hiddenHandPositions, overlayColor, intensity, detailedConfig);
        ApplyChongHintForPosition("top", topPosPanel.ShowCardsPosition, players, roomRule, hiddenHandPositions, overlayColor, intensity, detailedConfig);
        ApplyChongHintForPosition("right", rightPosPanel.ShowCardsPosition, players, roomRule, hiddenHandPositions, overlayColor, intensity, detailedConfig);
    }

    private static void ApplyChongHintForPosition(
        string position,
        Transform showCardsPosition,
        Dictionary<string, GameRecordManager.RecordPlayer> players,
        string roomRule,
        HashSet<string> hiddenHandPositions,
        Color overlayColor,
        float intensity,
        IDictionary<string, object> detailedConfig) {
        if (hiddenHandPositions != null && hiddenHandPositions.Contains(position)) return;
        HashSet<int> dangerTileIds = RecordChongHintCalculator.ComputeRonDangerForHandOwner(players, position, roomRule, detailedConfig);
        if (dangerTileIds.Count == 0) return;
        ApplyChongHintToShowCardsTransform(showCardsPosition, dangerTileIds, overlayColor, intensity);
    }

    private static void ApplyChongHintToShowCardsTransform(
        Transform showCardsPosition,
        HashSet<int> dangerTileIds,
        Color overlayColor,
        float intensity) {
        if (showCardsPosition == null) return;
        for (int i = 0; i < showCardsPosition.childCount; i++) {
            Transform child = showCardsPosition.GetChild(i);
            Tile3D tile3D = child.GetComponent<Tile3D>();
            if (tile3D == null) continue;
            int tileId = TileIdOrder.Normalize(tile3D.GetTileId());
            if (!dangerTileIds.Contains(tileId)) continue;
            Card3DHoverManager.Instance.SetCardDangerOverlay(child.gameObject, overlayColor, intensity);
        }
    }

    /// <summary>牌谱局中错和确认续打：清除倒牌展示并按当前牌谱状态重建和牌者手牌区。</summary>
    public void RestoreRecordPlayerHandAfterCuoheReveal(string winnerPosition) {
        if (string.IsNullOrEmpty(winnerPosition) || GameRecordManager.Instance == null) return;

        PosPanel3D panel = GetPosPanel(winnerPosition);
        if (panel != null) {
            ClearHandCardsPosition(panel.cardsPosition);
            ForceHandRevealIdle(panel);
        }

        if (winnerPosition == "self") {
            RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
            return;
        }

        RenderRecordPlayerHand(winnerPosition);
    }

    private void RenderRecordPlayerHand(string playerPosition){
        List<int> handTiles = GameRecordManager.Instance.recordPlayer_to_info[playerPosition].tileList;
        bool pinDraw = GameRecordManager.Instance.recordPlayer_to_info[playerPosition].showHandDrawSlotActive;
        PosPanel3D panel = GetPosPanel(playerPosition);
        ClearPlayerRecordHandObjects(panel);
        bool isShowCardsMode = RecordSetting.Instance.IsShowCardsMode;
        if (isShowCardsMode){
            LayRecordShowHandTiles(playerPosition, panel.ShowCardsPosition, handTiles, pinDraw);
            return;
        }
        for (int i = 0; i < handTiles.Count; i++){
            Get3DTile(playerPosition,"init");
        }
    }

    private bool IsRecordShowCardsModeActive(){
        return RecordSetting.Instance.IsShowCardsMode &&
               GameRecordManager.Instance.gameObject.activeSelf;
    }

    private void ClearPlayerRecordHandObjects(PosPanel3D panel){
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(panel.cardsPosition, objectsToReturn);
        CollectChildren(panel.ShowCardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn){
            MahjongObjectPool.Instance.Return(-1, obj);
        }
    }

    // 清除3D手牌
    public void Clear3DTile(){
        ClearAllLastDiscardAndJiagang();
        // 重置组合牌指针
        selfSetCombinationsPoint = selfPosPanel.combinationsPosition.position;
        leftSetCombinationsPoint = leftPosPanel.combinationsPosition.position;
        topSetCombinationsPoint = topPosPanel.combinationsPosition.position;
        rightSetCombinationsPoint = rightPosPanel.combinationsPosition.position;
        ResetCombinationLastSlotWidths();
        pengToJiagangPosDict.Clear();

        // 先收集所有要归还的对象，避免在遍历时修改集合导致跳过元素
        List<GameObject> objectsToReturn = new List<GameObject>();

        // 收集所有面板的弃牌
        CollectChildren(leftPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.discardsPosition, objectsToReturn);

        // 收集所有面板的补花
        CollectChildren(leftPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(topPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(rightPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(selfPosPanel.buhuaPosition, objectsToReturn);

        // 收集所有面板的展开手牌
        CollectChildren(leftPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.ShowCardsPosition, objectsToReturn);

        // 收集所有面板的手牌（含上局和牌/听牌摆出的明牌残留）；由同步 Clear3DTile 单点回收，
        // 避免协程清手与下一局摆牌竞态导致对象池耗尽与重复堆叠。
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);

        // 收集所有面板的组合牌3D对象数组中的子物体
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel })
        {
            foreach (Transform combinationObj in panel.combination3DObjects)
            {
                if (combinationObj != null)
                {
                    CollectChildren(combinationObj, objectsToReturn);
                }
            }
        }

        // 统一归还所有收集到的对象
        foreach (GameObject obj in objectsToReturn)
        {
            if (obj != null)
            {
                MahjongObjectPool.Instance.Return(-1, obj);
            }
        }

        // 立直点棒与手牌展开 Animator 的清理：避免上一局立直棒/展开姿势残留到下一局。
        StopAllHandAnimationQueues();
        ClearAllRiichiTenbous();
        ResetHandRevealAnimators();
    }

    /// <summary>
    /// 清空本对象上所有正在执行的协程（用于牌谱重新推理手牌前，避免旧动画与重建画面冲突）
    /// </summary>
    public void StopAllRunningAnimations() {
        StopAllDiscardMoveCoroutines();
        ClearAllLastDiscardAndJiagang();
        StopAllHandAnimationQueues();
        StopAllCoroutines();
    }

    /// <summary>
    /// 收集 Transform 的所有子对象到列表中（避免在遍历时修改集合）
    /// </summary>
    private void CollectChildren(Transform parent, List<GameObject> collection)
    {
        if (parent == null) return;

        // 使用倒序遍历，避免索引变化问题
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null)
            {
                collection.Add(child.gameObject);
            }
        }
    }

}
