using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直麻将和牌计算上下文。
    /// 与服务端 riichi_hepai_check.py 的 context 字段对齐，客户端本地计算时填入。
    /// </summary>
    public class RiichiHandContext {
        public bool IsTsumo;
        public bool IsRiichi;
        public bool IsDaburuRiichi;
        public bool IsIppatsu;
        public bool IsRinshan;
        public bool IsChankan;
        public bool IsHaitei;
        public bool IsHoutei;
        public bool IsTenhou;
        public bool IsChiihou;

        /// <summary>门风：41=东 42=南 43=西 44=北</summary>
        public int PlayerWind = RiichiTileUtil.East;

        /// <summary>场风：41=东 42=南</summary>
        public int RoundWind = RiichiTileUtil.East;

        public bool HasOpenTanyao = true;

        public List<int> DoraIndicators = new List<int>();
        public List<int> UraDoraIndicators = new List<int>();

        /// <summary>赤宝牌数量（null 时根据手牌自动推断）</summary>
        public int? AkaCount = null;
    }
}
