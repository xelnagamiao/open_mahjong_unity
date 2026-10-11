using System;
using System.Collections.Generic;
using System.Linq;

public static class GuizhouSettlementDisplayValidation {
    public static object RunAll() {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("贵州结算：" + name); checks++; }
        var transfers = new List<GuizhouTransfer>();
        for (int payer = 1; payer <= 3; payer++) {
            transfers.Add(new GuizhouTransfer { payer=payer, payee=0, points=3, category="hand", reason="自摸" });
            transfers.Add(new GuizhouTransfer { payer=payer, payee=0, points=3, category="kong", reason="暗杠", tile=31, count=1, unit_points=3 });
            transfers.Add(new GuizhouTransfer { payer=payer, payee=0, points=8, category="chicken", reason="金鸡", tile=31, count=4, unit_points=2 });
        }
        var info = new GuizhouInfo { ledger = new GuizhouLedger { indicator=39, chicken_tile=31, transfers=transfers.ToArray() } };
        var display = GuizhouSettlementDisplay.Build(info, 0);
        Check(display.HandScore == 9 && display.KongScore == 9 && display.ChickenScore == 24 && display.Total == 42, "收付分项合计");
        Check(display.Labels.Length == 2 && display.Labels.Any(s => s.EndsWith("暗杠一条*1")) && display.Labels.Any(s => s.EndsWith("金鸡一条*4")), "张数不乘三家");
        Check(display.Labels.All(s => GuizhouFanText.Value(s).EndsWith("分")), "复用番种对照表的分数单位");
        Check(display.TotalDisplay().FanText == "和牌得分 9分" && display.TotalDisplay().FuText == "杠+9分  捉鸡+24分", "汇总文案");
        Check(display.Indicator == 39 && display.ChickenTile == 31, "九条翻出一条鸡");
        transfers.Add(new GuizhouTransfer { payer=0,payee=1,points=2,category="chicken",reason="鸡牌",tile=28,count=2,unit_points=1 });
        info.ledger.transfers = transfers.ToArray();
        display = GuizhouSettlementDisplay.Build(info, 0);
        Check(display.Total == 40 && display.ChickenScore == 22 && display.Labels.Any(s => s.Contains("|-2|付鸡牌八筒*2")), "应付分不当作收入");
        Check(GuizhouSettlementDisplay.Build(info, 1).Total == -12, "切换和家按其收付读取");
        foreach (var t in transfers) { t.count = null; t.unit_points = null; }
        display = GuizhouSettlementDisplay.Build(info, 0);
        Check(display.Total == 40 && display.Labels.Any(s => s.EndsWith("金鸡一条*4")), "旧牌谱兼容");
        Check(GuizhouSettlementDisplay.Build(null, 0) == null, "缺少账本不伪造分数");
        return new { checks, passed=true };
    }
}
