using System.Collections;
using UnityEngine;

/// <summary>
/// 和牌倒牌策略入口：按规则与 hu_class 选择展示方式，委托 Game3DManager 执行 3D 演出。
/// </summary>
public static partial class HepaiRevealDirector {
    public static IEnumerator Play(int hepaiPlayerIndex, int[] hepaiPlayerHand, string huClass, string[] huFan) {
        if (hepaiPlayerHand == null || hepaiPlayerHand.Length == 0) {
            yield break;
        }
        if (NormalGameStateManager.Instance == null
            || !NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex, out string winnerPos)) {
            yield break;
        }

        string ruleKey = ResolveLiveRuleKey();
        string discardPos = NormalGameStateManager.Instance.lastDiscardPlayerPosition;
        HepaiPresentationRequest request = BuildRequestCore(winnerPos, huClass, hepaiPlayerHand, huFan, ruleKey, discardPos);
        yield return Game3DManager.Instance.PlayHepaiHandReveal(request);
        // 错和续局的手牌恢复在 ready 结束后由 NormalGameStateManager.TryResumeAfterCuoheContinue 统一处理
    }

    public static HepaiPresentationRequest BuildRequest(string winnerPosition, string huClass, int[] hand, string[] huFan) {
        string ruleKey = ResolveLiveRuleKey();
        string discardPos = NormalGameStateManager.Instance?.lastDiscardPlayerPosition;
        return BuildRequestCore(winnerPosition, huClass, hand, huFan, ruleKey, discardPos);
    }

    internal static HepaiPresentationRequest BuildRequestCore(
        string winnerPosition,
        string huClass,
        int[] hand,
        string[] huFan,
        string ruleKey,
        string discardPlayerPosition) {
        bool isCuohe = ContainsCuohe(huFan);
        bool isGuobiao = IsGuobiaoRuleKey(ruleKey);
        bool isSelfDraw = huClass == "hu_self";

        HepaiWinTilePresentMode mode;
        if (isSelfDraw) {
            mode = HepaiWinTilePresentMode.TsumoTravel;
        }
        else if (isGuobiao && !isCuohe) {
            mode = HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver;
        }
        else {
            mode = HepaiWinTilePresentMode.RonInstantThenPause;
        }

        int hepaiTile = hand != null && hand.Length > 0 ? hand[hand.Length - 1] : 0;

        return new HepaiPresentationRequest {
            HepaiPlayerIndex = -1,
            WinnerPosition = winnerPosition,
            HuClass = huClass,
            HepaiPlayerHand = hand,
            WinTileMode = mode,
            RestoreMidGameAfterReveal = false,
            DiscardPlayerPosition = discardPlayerPosition,
            HepaiTile = hepaiTile,
            IsCuoheRon = isCuohe,
            RecordRule = ruleKey,
        };
    }

    private static string ResolveLiveRuleKey() {
        var gsm = NormalGameStateManager.Instance;
        if (gsm == null) return "";
        if (!string.IsNullOrEmpty(gsm.roomRule)) return gsm.roomRule;
        return gsm.subRule ?? "";
    }

    internal static bool IsGuobiaoRuleKey(string ruleKey) {
        if (string.IsNullOrEmpty(ruleKey)) return false;
        ruleKey = ruleKey.ToLowerInvariant();
        return ruleKey == "guobiao" || ruleKey.StartsWith("guobiao");
    }

    private static bool IsGuobiaoRule() => IsGuobiaoRuleKey(ResolveLiveRuleKey());

    private static bool ContainsCuohe(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "错和") return true;
        }
        return false;
    }

    /// <summary>四川血战·中途和牌：按 hu_class / multiRon / 抢杠 选择补花区摆牌策略。</summary>
    public static HepaiPresentationRequest BuildSichuanMidGameRequest(
        string winnerPosition,
        string huClass,
        int hepaiTile,
        bool multiRon,
        int? ronDiscarderIndex,
        bool recycleDiscard,
        bool isQianggang = false) {
        HepaiWinTilePresentMode mode;
        if (huClass == "hu_self") {
            mode = HepaiWinTilePresentMode.SichuanZimoToBuhuaFaceDown;
        } else if (multiRon || isQianggang) {
            // 一炮多响 / 抢杠和：透明克隆，保留河牌或加杠牌直至最后回收
            mode = HepaiWinTilePresentMode.SichuanRonMultiToBuhua;
        } else {
            mode = HepaiWinTilePresentMode.SichuanRonSingleToBuhua;
        }

        return new HepaiPresentationRequest {
            WinnerPosition = winnerPosition,
            HuClass = huClass,
            HepaiTile = hepaiTile,
            WinTileMode = mode,
            RecycleDiscardAfterPresent = recycleDiscard,
            IsQianggang = isQianggang,
            DiscardPlayerPosition = NormalGameStateManager.Instance?.ResolveRonDiscarderPosition(ronDiscarderIndex),
        };
    }

    public static IEnumerator PlaySichuanMidGame(
        int hepaiPlayerIndex,
        string huClass,
        int hepaiTile,
        bool multiRon,
        int? ronDiscarderIndex,
        bool recycleDiscard,
        bool isQianggang = false) {
        if (NormalGameStateManager.Instance == null
            || !NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex, out string winnerPos)) {
            yield break;
        }
        if (Game3DManager.Instance == null) yield break;

        HepaiPresentationRequest request = BuildSichuanMidGameRequest(
            winnerPos, huClass, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang);
        yield return Game3DManager.Instance.PlaySichuanMidGameHu(request);
    }
}
