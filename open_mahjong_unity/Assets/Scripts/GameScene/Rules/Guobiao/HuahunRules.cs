using System.Collections.Generic;
using System.Linq;

internal static class HuahunRules {
    internal const string Description = "国标麻将花混规则是部分国标麻将资深玩家研发的娱乐玩法。该娱乐规则将麻将花牌中“春、夏、秋、冬”四朵红花取出用作赖子（花混），并相应提高了有赖子手牌的和番限制以及无赖子和牌时的分数奖励。该玩法进一步加快了牌局行进速度，突出了“抢和”理念，丰富了手牌策略变化，可以作为国标麻将竞技规则以外的一种娱乐补充。";
    internal const string RulebookText =
        "①起和限制，0赖子和1赖子8番起和，2赖子16番起和，3赖子32番起和，4赖子64番起和。\n\n" +
        "②无赖子和牌时结算时番数额外加八番或者直接番数翻倍（默认额外加八番）\n\n" +
        "③赖子只能在摸到手的时候摸切，不准手切，他家点出的癞子无法点和和牌（起手赖子不得打出）\n\n" +
        "④手中摸到四个赖子可以花杠，和牌时剩下手牌必须符合基本和牌型，花杠算88番。\n\n" +
        "⑤（四朵红花用作赖子的情况下）补的黑花在结算时一朵算4番，两朵8番，三朵16番，四朵32番\n\n" +
        "⑥四朵红花是春夏秋冬，四朵黑花是梅兰竹菊，自摸/点和癞子不算绝张，癞子能构成独听条件时构成独听（设置默认关闭癞子多面听）";
    internal static readonly string[] BonusLabels = { "无癞子+8分", "无癞子翻倍", "不额外加分" };
    internal static int BonusIndex(string value) => value == "none" ? 2 : value == "double" ? 1 : 0;
    internal static string BonusValue(int index) => index == 2 ? "none" : index == 1 ? "double" : "add8";
    internal static bool IsJoker(int tile) => tile >= 51 && tile <= 54;
    internal static bool CanFlowerKong(IReadOnlyList<int> hand) => hand != null && Enumerable.Range(51, 4).All(t => hand.Count(v => v == t) == 1);
    internal static int[] ReplayKongMask(string sub, IReadOnlyList<int> removed) {
        bool flower = CanFlowerKong(removed);
        return removed.SelectMany(t => new[] { flower ? 0 : 2, t }).ToArray();
    }
    internal static string FanValue(string name) {
        switch (name) {
            case "花杠": return "88番";
            case "无癞子加番": return "8番";
            case "黑花1朵": return "4番";
            case "黑花2朵": return "8番";
            case "黑花3朵": return "16番";
            case "黑花4朵": return "32番";
        }
        if (name != null && name.StartsWith("无癞子翻倍*") && int.TryParse(name.Substring("无癞子翻倍*".Length), out int fan)) return fan + "番";
        return null;
    }
}
