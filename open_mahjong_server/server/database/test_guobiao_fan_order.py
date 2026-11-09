"""国标番种的展示顺序要与当前 32 番番表一致。"""
import json
from pathlib import Path

from server.database.statistics_fans import fan_names


def test_three_kongs_is_between_other_32_fan_entries():
    names = list(fan_names("guobiao/standard"))
    assert names[names.index("sangang") - 1:names.index("sangang") + 2] == [
        "yisesibugao", "sangang", "hunyaojiu"]
    assert names.count("sangang") == 1


def test_generated_web_and_unity_keep_three_kongs_order():
    root = Path(__file__).resolve().parents[3]
    web = json.loads((root / "open_mahjong_web/shared/statisticsFans.json").read_text(encoding="utf8"))
    for key in ("guobiao/standard", "guobiao/sanma"):
        names = list(web[key])
        assert names[names.index("sangang") - 1] == "yisesibugao"
    unity = (root / "open_mahjong_unity/Assets/Scripts/UI/PlayerInfo/PlayerInfoRuleCatalog.cs").read_text(encoding="utf8")
    assert '{"yisesibugao", "一色四步高"}, {"sangang", "三杠"}, {"hunyaojiu", "混幺九"}' in unity
