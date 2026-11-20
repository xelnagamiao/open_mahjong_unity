import json
from pathlib import Path

from server.gamestate.game_nanchang.NanchangGameState import NanchangGameState
from tests.local_hint_oracles.nanchang import for_hand


def test_shared_nanchang_previews_match_current_server_rules():
    fixtures = Path(__file__).resolve().parents[2] / "open_mahjong_unity/Assets/Tests/Fixtures/LocalHints"
    rows = json.loads((fixtures / "regional-parity.json").read_text(encoding="utf-8"))
    nanchang = [row for row in rows if row["rule"] == "nanchang"]
    assert nanchang
    for row in nanchang:
        context = row["context"]
        seat = context["index"]
        before = list(row["hand"])
        before.remove(row["win"])
        game = NanchangGameState(calculation_service=object())
        game.initialize_round()
        game.indicator = context["indicator"]
        player = game.player_list[seat]
        player.hand_tiles = before
        player.combination_tiles = row["melds"]
        player.missed_fan = context["missed_fan"]
        player.declared_jingdiao = context["declared_jingdiao"]
        if context["peida"]:
            player.tag_list.append("peida")

        waits = {detail["tile"]: detail for detail in for_hand(game, seat, before, context["discarded"])}
        assert sorted(waits) == row["waits"], row
        expected = dict(row["expected"])
        for field in ("ron_status", "ron_points"):
            expected[field] = {int(payer): value for payer, value in expected[field].items()}
        assert waits[row["win"]] == expected, row
