using System.Collections;
using UnityEngine;

/// <summary>
/// 和牌倒牌策略入口：按规则与 hu_class 选择展示方式，委托 Game3DManager 执行 3D 演出。
/// </summary>
public static class HepaiRevealDirector {
    public static IEnumerator Play(int hepaiPlayerIndex, int[] hepaiPlayerHand, string huClass, string[] huFan) {
        if (hepaiPlayerHand == null || hepaiPlayerHand.Length == 0) {
            yield break;
        }
        if (NormalGameStateManager.Instance == null
            || !NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex, out string winnerPos)) {
            yield break;
        }

        HepaiPresentationRequest request = BuildRequest(winnerPos, huClass, hepaiPlayerHand, huFan);
        yield return Game3DManager.Instance.PlayHepaiHandReveal(request);
        // 错和续局的手牌恢复在 ready 结束后由 NormalGameStateManager.TryResumeAfterCuoheContinue 统一处理
    }

    public static HepaiPresentationRequest BuildRequest(string winnerPosition, string huClass, int[] hand, string[] huFan) {
        bool isCuohe = ContainsCuohe(huFan);
        bool isGuobiao = IsGuobiaoRule();
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

        string discardPos = NormalGameStateManager.Instance.lastDiscardPlayerPosition;

        return new HepaiPresentationRequest {
            HepaiPlayerIndex = -1,
            WinnerPosition = winnerPosition,
            HuClass = huClass,
            HepaiPlayerHand = hand,
            WinTileMode = mode,
            RestoreMidGameAfterReveal = false,
            DiscardPlayerPosition = discardPos,
        };
    }

    private static bool IsGuobiaoRule() {
        var gsm = NormalGameStateManager.Instance;
        if (gsm == null) return false;
        if (gsm.roomRule == "guobiao") return true;
        return !string.IsNullOrEmpty(gsm.subRule) && gsm.subRule.StartsWith("guobiao");
    }

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
