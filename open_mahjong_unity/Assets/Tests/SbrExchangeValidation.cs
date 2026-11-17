using System;
using System.Collections.Generic;
using System.Linq;

public static class SbrExchangeValidation {
    public static int Run() {
        int checks = 0;
        void Check(bool passed, string name) {
            if (!passed) throw new InvalidOperationException(name);
            checks++;
        }
        var hand = new List<int> {11,12,13,14,15,16,21,22,23,26,27,28,25,25};
        var q = new WaitHintQuery {SubRule = SichuanLobby.SbrExchange, HandWithWin = hand,
            Melds = new List<string>(), HepaiTile = 25, ExcludedSuit = 3};
        void Hint(string kind, string text) {
            var actual = RuleTips.DescribeWaitingTile(RuleRegistry.Resolve("sichuan", q.SubRule), q);
            Check(actual != null && actual.Kind == kind && actual.Label == text, text + " / " + actual?.Label);
        }
        Hint(WaitTileHint.KindRon, "点和1点\n自摸2点");
        q.HepaiLimit = 1;
        Hint(WaitTileHint.KindTsumoOnly, "仅自摸\n2点");
        q.HepaiLimit = 2;
        Hint(WaitTileHint.KindNone, "未起和");
        q.ExcludedSuit = 1;
        Hint(WaitTileHint.KindNone, "未满足");
        q.ExcludedSuit = 3; q.HepaiLimit = 0; q.SbrPassedFan = 0;
        Hint(WaitTileHint.KindTsumoOnly, "仅自摸\n2点");
        q.SbrPassedFan = -1;
        q.SbrWindow = new SbrExchangeWindow { tile=25, draw=true, heaven=true };
        Hint(WaitTileHint.KindRon, "点和1点\n自摸32点");
        q.SbrWindow = new SbrExchangeWindow { tile=25, earth=true };
        Hint(WaitTileHint.KindRon, "点和16点\n自摸2点");
        q.SbrWindow.draw = true;
        Hint(WaitTileHint.KindRon, "点和1点\n自摸32点");
        q.SbrWindow = new SbrExchangeWindow { tile=25, kong=true, robbing=true };
        Hint(WaitTileHint.KindRon, "点和4点\n自摸2点");
        q.SbrWindow = new SbrExchangeWindow { tile=25, draw=true, kong=true, last=true };
        Hint(WaitTileHint.KindRon, "点和1点\n自摸8点");
        q.SbrWindow.tile = 11;
        Hint(WaitTileHint.KindRon, "点和1点\n自摸2点");
        var waits = RuleTips.ComputeWaiting("sichuan", q.SubRule, new TingpaiQuery {
            Hand = hand.Take(13).ToList(), Melds=q.Melds, ExcludedSuit=3
        });
        Check(waits.Contains(25), "exact wait");
        Check(SichuanLobby.SubRules.Any(rule => rule.Key == q.SubRule), "lobby");
        Check(!SichuanLobby.IsXueliu(q.SubRule), "retired winners");
        Check(SichuanFanText.FanTotal(q.SubRule, new[] {"天和","杠","杠","清一色"}) == 9, "uncapped fan");
        Check(PlayerInfoRuleCatalog.FanNames(q.SubRule).Count == 14, "13 fans and zero fan hand");
        var rd = new Round {sbrPassedFans = new Dictionary<int, SortedDictionary<int, int>> {
            {1, new SortedDictionary<int, int> {{2,0}, {4,2}, {6,-1}}}
        }};
        Check(SbrExchangeTips.RecordedPassedFan(rd,1,1) == -1 && SbrExchangeTips.RecordedPassedFan(rd,1,3) == 0, "pass timing");
        Check(SbrExchangeTips.RecordedPassedFan(rd,1,4) == 2 && SbrExchangeTips.RecordedPassedFan(rd,1,6) == -1, "pass reset");
        Check(SbrExchangeTips.RecordedPassedFan(rd,0,4) == -1 && SbrExchangeTips.PassedFan(new[] {"offline","shunhe_3"}) == 3, "private caps");
        var manifest = RuleRegistry.Resolve("sichuan", q.SubRule);
        Check(Uri.UnescapeDataString(manifest.RulebookPath(q.SubRule, null)) == "/rulebook/sichuan?sub_rule=" + q.SubRule, "specific rulebook");
        var room = manifest.BuildRoom(new RoomCreationInput {
            Rule="sichuan", SubRule=q.SubRule, RoomName="血战换三张", GameRound=1, StepTimer=5, RoundTimer=20, BloodBattle=false
        });
        Check(room.IsValid && room.Configuration is Sichuan_Create_RoomConfig cfg && cfg.BloodBattle && cfg.HepaiLimit==0, "room builder");
        q.SbrWindow = null; q.Melds = new List<string>(); q.HepaiTile=17;
        q.HandWithWin = new List<int> {11,11,12,12,13,13,14,14,15,15,16,16,17,17};
        var seven = RuleTips.DescribeWaitingTile(manifest,q);
        Check(seven.Label == "点和16点\n自摸32点", "pure seven pairs");
        q.Melds=new List<string> {"G11","g13","k15","k17"};
        q.HandWithWin=new List<int> {19,19}; q.HepaiTile=19;
        Check(RuleTips.DescribeWaitingTile(manifest,q).Label == "32点", "kong, pure, all pungs and single wait cap");
        return checks;
    }
}
