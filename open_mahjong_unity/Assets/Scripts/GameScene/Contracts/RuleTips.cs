using System.Collections.Generic;
using UnityEngine;

/// <summary>听牌查询：手牌（不含将要打出的牌）+ 副露字串 + 房间细则。由 RuleManifest.Tingpai 消费。</summary>
public sealed class TingpaiQuery {
    public List<int> Hand;
    public List<string> Melds;
    public Dictionary<string, object> DetailedConfig;
    /// <summary>不可和的花色（四川定缺：1 万 / 2 饼 / 3 条；0 表示无）。</summary>
    public int ExcludedSuit;
}

/// <summary>
/// 单张和牌张的提示查询。对局与牌谱共用：<see cref="Record"/> 为 null 时是对局（族可直接读自己的 GameState / TableMirror），
/// 否则是牌谱/延时观战，族只能从 Record 与本对象取数据。
/// </summary>
public sealed class WaitHintQuery {
    public int HepaiTile;
    /// <summary>手牌 + 和牌张。</summary>
    public List<int> HandWithWin;
    public List<string> Melds;
    public List<int[]> MeldMasks;
    /// <summary>通用和牌条件（花牌/场风/自风/和单张）。</summary>
    public List<string> WayToHepai;
    /// <summary>和绝张等按桌面可见张数得出的单张条件。</summary>
    public List<string> SingleTileWay;
    /// <summary>WayToHepai + SingleTileWay + "点和"。</summary>
    public List<string> MergedWay;
    public int HuapaiCount;
    public string SubRule;
    public int HepaiLimit;
    public int SelfIndex;
    public int CurrentRound;
    public List<int> SelfFlowers;
    public Dictionary<string, object> DetailedConfig;
    public int ExcludedSuit;
    public RecordTipsContext Record;

    /// <summary>点和条件换成自摸（把 "点和" 换为 "自摸"）。</summary>
    public List<string> BuildZimoWay() {
        var list = new List<string>(WayToHepai);
        list.AddRange(SingleTileWay);
        list.Add("自摸");
        return list;
    }
}

/// <summary>一张和牌张在提示栏里的一行：标签 + 着色类别。</summary>
public sealed class WaitTileHint {
    public const string KindRon = "dianhe";
    public const string KindTsumoOnly = "zimo";
    public const string KindNone = "wuyi";

    public string Label;
    public string Kind = KindRon;

    public static WaitTileHint Ron(string label) => new WaitTileHint { Label = label, Kind = KindRon };
    public static WaitTileHint TsumoOnly(string label) => new WaitTileHint { Label = label, Kind = KindTsumoOnly };
    public static WaitTileHint None(string label) => new WaitTileHint { Label = label, Kind = KindNone };
}

/// <summary>通过 RuleManifest 钩子做听牌/提示计算的入口；核心 UI 只调这里，不认识任何族。</summary>
public static class RuleTips {
    /// <summary>按清单算听牌；清单为空或未声明 Tingpai 时返回空集并记警告。</summary>
    public static HashSet<int> ComputeWaiting(RuleManifest manifest, TingpaiQuery query) {
        if (manifest?.Tingpai == null) {
            Debug.LogWarning($"规则未提供听牌计算: {manifest?.RuleId ?? "(null)"}");
            return new HashSet<int>();
        }
        try {
            return manifest.Tingpai(query) ?? new HashSet<int>();
        } catch (System.Exception e) {
            Debug.LogError($"计算听牌列表时出错({manifest.RuleId}): {e.Message}");
            return new HashSet<int>();
        }
    }

    /// <summary>按 room_rule / sub_rule 解析清单后算听牌（牌谱路径）。</summary>
    public static HashSet<int> ComputeWaiting(string roomRule, string subRule, TingpaiQuery query) {
        RuleRegistry.TryResolve(roomRule, subRule, out RuleManifest manifest);
        return ComputeWaiting(manifest, query);
    }

    /// <summary>描述一张和牌张；清单未声明时返回 null（UI 只摆牌不写标签）。</summary>
    public static WaitTileHint DescribeWaitingTile(RuleManifest manifest, WaitHintQuery query) {
        if (manifest?.DescribeWaitingTile == null) {
            Debug.LogWarning($"规则未提供和牌张提示: {manifest?.RuleId ?? "(null)"}");
            return null;
        }
        try {
            return manifest.DescribeWaitingTile(query);
        } catch (System.Exception e) {
            Debug.LogError($"计算和牌张提示时出错({manifest.RuleId}): {e.Message}");
            return null;
        }
    }
}
