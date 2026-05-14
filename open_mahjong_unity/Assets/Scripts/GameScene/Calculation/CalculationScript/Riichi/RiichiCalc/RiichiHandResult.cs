using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直和牌计算结果。与服务端 riichi_hepai_check.py 返回字段对齐。
    /// </summary>
    public class RiichiHandResult {
        public bool IsValid;
        public int Han;
        public int Fu;
        /// <summary>
        /// 和牌者从对手处总收分（不含本场棒/供托）。
        /// 荣和：放铳者支付（庄家 6×base，闲家 4×base）。
        /// 自摸：非庄家付 2×base，庄家付 4×base；庄家自摸每人付 4×base。
        /// </summary>
        public int Score;
        public List<string> Yaku = new List<string>();
        public int AkaCount;
        public string Error;

        /// <summary>役满倍数（0=非役满；1=役满；2=双倍役满…）</summary>
        public int YakumanMultiplier;
    }
}
