/// <summary>
/// 抢杠和牌展示适配。
/// </summary>
public static partial class HepaiRevealDirector {
    /// <summary>
    /// 只有规则结果明确提供了被抢加杠的牌源信息，公共 3D 流程才会回收加杠展示中的第四张牌。
    /// </summary>
    private readonly struct RulePresentationCapabilities {
        public readonly bool SupportsRobbedAddedKongSource;

        public RulePresentationCapabilities(bool supportsRobbedAddedKongSource) {
            SupportsRobbedAddedKongSource = supportsRobbedAddedKongSource;
        }
    }

    /// <summary>
    /// 当前仅台湾麻将适配了所需的完整抢杠信息；未来规则若提供相同协议，可在此注册能力。
    /// </summary>
    private static RulePresentationCapabilities ResolveRulePresentationCapabilities(string ruleKey) {
        return IsTaiwanRuleKey(ruleKey)
            ? new RulePresentationCapabilities(supportsRobbedAddedKongSource: true)
            : default;
    }

    /// <summary>
    /// 服务端先按加杠发送暂态牌面；抢杠成立后，记录责任方和被抢牌，由公共 3D 演出层回收暂态牌源并保留原碰牌。
    /// </summary>
    private static void ConfigureRuleSpecificRonRequest(
        HepaiPresentationRequest request,
        string ruleKey,
        bool isQianggang,
        int? ronDiscarderIndex,
        int hepaiTile) {
        RulePresentationCapabilities capabilities = ResolveRulePresentationCapabilities(ruleKey);
        if (request == null || !isQianggang || !capabilities.SupportsRobbedAddedKongSource) return;

        request.IsQianggang = true;
        request.DiscardPlayerPosition = NormalGameStateManager.Instance.ResolveRonDiscarderPosition(ronDiscarderIndex);
        if (hepaiTile > 0) request.HepaiTile = hepaiTile;
    }

    /// <summary>
    /// 牌谱先记录加杠动作，再记录和牌节点；只有确认和牌节点包含抢杠且不是错和时，才将请求标记为需要回收暂态加杠牌源。
    /// </summary>
    private static void ConfigureRuleSpecificRecordRequest(
        HepaiPresentationRequest request,
        string ruleKey,
        string[] huFan) {
        RulePresentationCapabilities capabilities = ResolveRulePresentationCapabilities(ruleKey);
        if (request == null || !capabilities.SupportsRobbedAddedKongSource || request.IsCuoheRon) return;
        if (huFan == null) return;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "抢杠") {
                request.IsQianggang = true;
                return;
            }
        }
    }
}
