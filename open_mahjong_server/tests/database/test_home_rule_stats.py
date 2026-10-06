"""Rule identities, custom-scene persistence, and isolated replay restore contracts."""

from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import Mock
import pytest

from server.database.rule_identity import canonical_game_record
from server.database.scene_stats import derive_game_type, record_game_metrics, should_record_scene_metrics
from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.database.get_rule_stats import canonical_mode, get_custom_rule_history, validate_selection


def replay(rule="zhongyong", sub_rule="zhongyong/nanque", ticks=None, seats=None):
    return {"game_title": {"rule":rule,"sub_rule":sub_rule,"room_type":"custom"},
            "game_round": {"round_index_1": {"seats": seats or [0,1,2,3], "start_player_index":0,
                "action_ticks": ticks or [["reset",0],["hu_self",0,8,["平和"],[0,0,0,0]],
                    ["blood","settle_hu","hu_self",0,8,["平和"],[24,-8,-8,-8]], ["end"]]}}}


def test_legacy_alias_is_canonicalized_without_mutating_source_and_guangdong_profiles_stay_distinct():
    source = replay("jiandan", "jiandan/standard")
    before = deepcopy(source)
    fixed = canonical_game_record(source)
    assert source == before
    assert fixed["game_title"]["rule"] == "zhongyong"
    assert fixed["game_title"]["sub_rule"] == "zhongyong/nanque"
    for profile in ("guangdong/mil2023", "guangdong/tuidao_mil2024"):
        assert canonical_game_record(replay("guangdong", profile))["game_title"]["sub_rule"] == profile
    for profile in ("shanghai/qiaoma", "shanghai/qinghunpeng"):
        assert canonical_game_record(replay("shanghai", profile))["game_title"]["sub_rule"] == profile
    assert canonical_game_record(replay("shanghai",None))["game_title"]["sub_rule"] == "shanghai/qiaoma"


def test_custom_metrics_and_three_player_modes_are_enabled():
    assert should_record_scene_metrics("custom", None)
    assert not should_record_scene_metrics("events", None, None)
    assert not should_record_scene_metrics("match", "unknown")
    for mode in ("1/4_sanma", "1/4_sanma_rank", "1/3", "1/3_rank"):
        assert derive_game_type(mode) == "dongfeng"
    assert derive_game_type("3/4_rank") == "xifeng"


def test_nanque_deferred_win_counts_once_and_preserves_settlement_score():
    record = replay(ticks=[["reset",0],["c",11,"F"],["reset",1],
        ["hu_first",1,16,["平和"],[0,0,0,0],11,0,0,1],
        ["blood","settle_hu","hu_first",1,16,["平和"],[-16,16,0,0]], ["end"]])
    winner = analyze_shared_record_for_player(record, 1)
    payer = analyze_shared_record_for_player(record, 0)
    assert (winner["win_count"], winner["self_draw_count"], winner["total_fan_score"]) == (1,0,16)
    assert winner["total_round_score"] == 16
    assert winner["total_win_turn"] == 1
    assert (payer["deal_in_count"],payer["total_fangchong_score"],payer["total_round_score"]) == (1,16,-16)


def test_nanque_opening_draw_stays_with_dealer_and_invalid_seats_are_ignored():
    record = replay(ticks=[["reset",0],["d",11],["c",11,"F"],
        ["hu_self",0,8,["平和"],[24,-8,-8,-8]],["end"]])
    assert analyze_shared_record_for_player(record,0)["total_win_turn"] == 2
    record["game_round"]["round_index_1"]["seats"] = [0,0,1,2]
    assert analyze_shared_record_for_player(record,0)["total_rounds"] == 0


@pytest.mark.parametrize("rule,profile", [("guangdong","guangdong/mil2023"),("guangdong","guangdong/tuidao_mil2024"),
    ("shanghai","shanghai/qiaoma"),("shanghai","shanghai/qinghunpeng")])
def test_regional_custom_writes_keep_profile_and_actual_player_counters(rule,profile):
    cursor, conn = Mock(), Mock()
    conn.cursor.return_value = cursor
    db = SimpleNamespace(_get_connection=lambda: conn, _put_connection=Mock())
    player = SimpleNamespace(user_id=10000101,username="测试",score=32,original_player_index=0,
        record_counter=SimpleNamespace(zimo_times=2,dianhe_times=1,fangchong_times=1,rank_result=1,
            win_score=12,win_turn=16,fangchong_score=4,fulu_times=1,cuohe_times=0,round_score_total=32))
    record_game_metrics(db, "regional-stored", replay(rule,profile), [player],
        dict(rule=rule,sub_rule=profile,room_type="custom",match_type="1/4"))
    values = cursor.execute.call_args.args[1]
    assert values[3:8] == (rule,profile,"custom",None,None)
    assert values[13:16] == (3,2,1)
    assert values[25] == 32
    conn.commit.assert_called_once()


def test_three_player_seat_rotation_and_zero_payment_hu_are_still_wins():
    record = replay("guobiao","guobiao/sanma",seats=[2,0,1],ticks=[
        ["reset",2],["p",11,2],["c",12,"F"],["gd",13,2],
        ["hu_self",2,8,["平和"],[0,0,0]], ["end"]])
    stats = analyze_shared_record_for_player(record,0)
    assert (stats["total_rounds"],stats["win_count"],stats["self_draw_count"],stats["fulu_round_count"]) == (1,1,1,1)
    assert stats["total_win_turn"] == 1
    unfinished = deepcopy(record)
    unfinished["game_round"]["round_index_1"]["action_ticks"].pop()
    assert analyze_shared_record_for_player(unfinished,0)["total_rounds"] == 0


def test_rule_query_rejects_mixed_variants_and_keeps_legacy_three_player_modes():
    assert validate_selection(dict(rule="guangdong_tuidao",source_rule="guangdong",
        sub_rule="guangdong/tuidao_mil2024",player_count=4,room_type="custom")) == "guangdong_tuidao"
    for changes in (dict(source_rule="zhongyong"),dict(sub_rule="guangdong/mil2023"),
                    dict(player_count=3),dict(room_type="match")):
        with pytest.raises(ValueError):
            validate_selection(dict(rule="guangdong_tuidao",**changes))
    assert canonical_mode("1/3",3) == "1/4_sanma"
    assert canonical_mode("2/4_sanma_rank",3) == "2/4_sanma"
    assert canonical_mode("2/4",4) == "2/4"
    assert validate_selection(dict(rule="shanghai_qinghunpeng",source_rule="shanghai",
        sub_rule="shanghai/qinghunpeng",player_count=4)) == "shanghai_qinghunpeng"
    with pytest.raises(ValueError):
        validate_selection(dict(rule="shanghai",sub_rule="shanghai/qinghunpeng"))


def test_generic_rule_route_echoes_request_id_and_separates_response_rule(monkeypatch):
    import asyncio
    from unittest.mock import AsyncMock
    from server.database import data_router

    reader = AsyncMock(return_value=[dict(rule="guangdong_tuidao",mode="1/4",total_games=2,
        total_rounds=8,total_round_score=16,win_count=1)])
    monkeypatch.setattr(data_router,"_run_record_read",reader)
    socket = SimpleNamespace(send_json=AsyncMock())
    server = SimpleNamespace(db_manager=object(),players={"connected":SimpleNamespace(user_id=10000101)})
    request = dict(type="data/get_rule_stats",userid=10000102,rule="guangdong_tuidao",
        source_rule="guangdong",sub_rule="guangdong/tuidao_mil2024",player_count=4,
        room_type="custom",data_request_id="selected-tuidao")
    asyncio.run(data_router.handle_data_message(server,"connected",request,socket))
    response = socket.send_json.call_args.args[0]
    assert response["success"] and response["data_request_id"] == "selected-tuidao"
    assert response["rule_stats"]["rule"] == "guangdong_tuidao"
    assert response["rule_stats"]["history_stats"][0]["total_round_score"] == 16
    assert reader.call_args.args == (get_custom_rule_history,server.db_manager,10000102,"guangdong_tuidao")
    asyncio.run(data_router.handle_data_message(server,"connected",dict(request,player_count=3),socket))
    assert not socket.send_json.call_args.args[0]["success"]
    assert reader.call_count == 1
