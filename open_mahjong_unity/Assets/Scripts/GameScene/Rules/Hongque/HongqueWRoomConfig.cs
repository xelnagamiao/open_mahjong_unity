using System;
using System.Collections.Generic;
using System.Linq;

public sealed class HongqueWRoomConfig : Jiandan_Create_RoomConfig {
    public bool Colourless = true;
    public int MinimumFan;
    public int MinimumPoints;

    internal static DetailedConfigDefinition Definition() => new DetailedConfigDefinition("hongque",
        new DetailedConfigPresentation("虹雀 W · 规则设置", "规则预设", "自定义", "无起和限制，每位玩家每局可补牌两次。", "规则来源", "虹雀 W v1.0"),
        new[] {
            new DetailedConfigOption("牌张", "hongque_colourless", "加入无色牌", new[]{"开启","关闭"}, new object[]{true,false}, true),
            NumberOption("hongque_min_fan", "起和番数", Enumerable.Range(0,101)),
            NumberOption("hongque_min_points", "起和点数", Enumerable.Range(0,101).Concat(new[]{150,200,300,500,1000,2000,5000,10000}))
        }, new[]{new DetailedConfigPreset("默认规则", "加入无色牌，不设起和番数或点数。", new Dictionary<string,object>())});
    private static DetailedConfigOption NumberOption(string key,string label,IEnumerable<int> values) {
        var options=values.ToArray();
        return new DetailedConfigOption("起和限制",key,label,options.Select(value => value == 0 ? "不限" : value.ToString()).ToArray(),options.Cast<object>().ToArray(),0);
    }
    public static int ReadInt(Dictionary<string,object> values,string key) => values != null && values.TryGetValue(key,out var value) ? Convert.ToInt32(value) : 0;
}
