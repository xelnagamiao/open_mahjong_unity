using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直和牌计算结果。与服务端 riichi_hepai_check.py 返回字段对齐。
    /// </summary>
    public class RiichiHandResult {
        public bool IsValid;
        public int Han;
        /// <summary>实际役番，不含宝牌、赤宝、里宝及拔北宝牌；用于起和番。</summary>
        public int YakuHan;
        /// <summary>包含宝牌的闲家荣和等值，不含本场、供托及浪涌倍率；用于起和线。</summary>
        public int StandardRonScore;
        public int Fu;
        /// <summary>
        /// 和牌者从对手处总收分（不含本场棒/供托）。
        /// 荣和：放铳者支付（庄家 6×base，闲家 4×base）。
        /// 自摸：闲家支付 base，庄家支付 2×base；庄家自摸每人支付 2×base，各自向上取整至百点。
        /// </summary>
        public int Score;
        public List<string> Yaku = new List<string>();
        public int AkaCount;
        public string Error;

        /// <summary>役满倍数（0=非役满；1=役满；2=双倍役满…）</summary>
        public int YakumanMultiplier;

        public bool MeetsWinRequirements(int minimumHan, int minimumScore = 0) =>
            IsValid && YakuHan >= System.Math.Max(1, minimumHan) && StandardRonScore >= minimumScore;
    }
}
