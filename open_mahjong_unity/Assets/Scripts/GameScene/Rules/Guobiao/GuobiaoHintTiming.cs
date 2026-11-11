using System.Collections.Generic;

/// <summary>已发生的和牌时机快照；不把下一次摸牌/弃牌预测成天地人和或杠开。</summary>
public sealed class GuobiaoHintTiming {
    public int DrawTile;
    public int RonTile;
    public string DrawOpening;
    public string RonOpening;
    public bool LastDraw;
    public bool LastDiscard;
    public bool KongDraw;
    public bool RobbingKong;
    public bool InitialDraw;

    public string CacheKey => $"{DrawTile},{RonTile},{DrawOpening},{RonOpening},{LastDraw},{LastDiscard},{KongDraw},{RobbingKong},{InitialDraw}";

    public static bool SupportsOpening(string subRule) => subRule == "guobiao/standard"
        || subRule == "guobiao/sanma" || subRule == "guobiao/blood_battle"
        || subRule == "guobiao/transparent" || subRule == "guobiao/four_jokers"
        || subRule == "guobiao/huahun" || subRule == "guobiao/sixteen";

    public static GuobiaoHintTiming Create(string subRule, bool enabled, int self, int dealer,
        int selfDiscards, int totalDiscards, int dealerDiscards, bool anyMeld,
        int drawTile, int ronTile, int discarder, bool kongDraw, bool robbingKong,
        bool lastDraw, bool lastDiscard, bool? initialDrawWindow = null) {
        var timing = new GuobiaoHintTiming {
            DrawTile = drawTile, RonTile = ronTile, KongDraw = kongDraw && drawTile > 0,
            RobbingKong = robbingKong && ronTile > 0,
            LastDraw = lastDraw && drawTile > 0, LastDiscard = lastDiscard && ronTile > 0 && !robbingKong,
            InitialDraw = drawTile > 0 && self == dealer && totalDiscards == 0 && !anyMeld && !kongDraw
                && (initialDrawWindow ?? true),
        };
        if (!enabled || !SupportsOpening(subRule) || dealer < 0 || anyMeld || kongDraw || robbingKong) return timing;
        if (drawTile > 0) {
            if (self == dealer && totalDiscards == 0) timing.DrawOpening = "天和";
            else if (self != dealer && selfDiscards == 0) timing.DrawOpening = "人和";
        }
        if (ronTile > 0 && self != dealer && discarder == dealer && dealerDiscards == 1 && totalDiscards == 1)
            timing.RonOpening = "地和";
        return timing;
    }

    public void AddTo(List<string> way, int tile, bool selfDraw) {
        tile = WashizuTile.Face(tile);
        if (selfDraw ? tile != WashizuTile.Face(DrawTile) : tile != WashizuTile.Face(RonTile)) return;
        // 庄家起手没有一张有别于其它起手牌的等待牌；服务端无论是否启用天和都剔除独听。
        if (selfDraw && InitialDraw) {
            way.Remove("和单张");
            way.Remove("和绝张");
        }
        string opening = selfDraw ? DrawOpening : RonOpening;
        if (!string.IsNullOrEmpty(opening)) way.Add(opening);
        if (selfDraw) {
            if (KongDraw) way.Add("杠上开花");
            if (LastDraw) way.Add("last_deal");
        } else {
            if (RobbingKong) way.Add("抢杠和");
            if (LastDiscard) way.Add("last_cut");
        }
    }
}
