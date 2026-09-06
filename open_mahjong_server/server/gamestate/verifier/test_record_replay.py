"""Unity GameRecordManager 跳转推演的无头复刻：每条规则都要能逐步跑完牌谱。"""
from __future__ import annotations

import asyncio
import os
import sys
from pathlib import Path
from types import SimpleNamespace

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.public.game_record_manager import jsonable_game_record
from server.gamestate.verifier.record_sim import (
    RecordSim,
    load_jsonc,
    needs_ron_tile,
    parse_kan_mo_gang_flag,
    resolve_record_flags,
    stringify_tick,
)
from server.gamestate.verifier.record_sim.hu_hand import try_parse_hepai_tile
from server.gamestate.verifier.record_sim.meld_codec import build_angang_mask_from_removed

_PUBLIC = Path(__file__).resolve().parents[1] / "public"
_STATE_FILES = {
    "guobiao": "game_guobiao/GuobiaoGameState.py",
    "qingque": "game_mmcr/QingqueGameState.py",
    "classical": "game_classical/ClassicalGameState.py",
    "riichi": "game_riichi/RiichiGameState.py",
    "changsha": "game_changsha/ChangshaGameState.py",
    "taiwan": "game_taiwan/TaiwanGameState.py",
    "sichuan": "game_sichuan/SichuanGameState.py",
    "hongque": "game_hongque/record.py",
    "jiandan": "game_jiandan/JiandanGameState.py",
}


def _replay(record: dict) -> list[dict]:
    sim = RecordSim(record)
    rounds = record.get("game_round") or {}
    assert rounds, "牌谱没有局"
    last_frames = []
    for round_key, round_data in rounds.items():
        sequential = sim.apply_all(round_key)
        jumped = RecordSim(record).goto_action(round_key, len(round_data.get("action_ticks") or []))
        assert jumped["players"] == sequential[-1]["players"], f"{round_key} 跳转与逐步推演不一致"
        last_frames.append(sequential[-1])
    return last_frames


def _round(rule: str, sub_rule: str, p0, p1, p2, p3, tiles_list, ticks, **extra_title) -> dict:
    title = {
        "rule": rule,
        "sub_rule": sub_rule,
        "room_type": "custom",
        "commitment_hex": "00" * 32,
        "salt": "11" * 16,
        "max_round": 1,
        "open_cuohe": False,
        "tips": False,
        "is_player_set_random_seed": False,
        "p0_uid": 101,
        "p0_name": "A",
        "p1_uid": 102,
        "p1_name": "B",
        "p2_uid": 103,
        "p2_name": "C",
        "p3_uid": 104,
        "p3_name": "D",
    }
    title.update(extra_title)
    return {
        "game_title": title,
        "game_round": {
            "round_index_1": {
                "round_index": 1,
                "current_round": 1,
                "seats": [0, 1, 2, 3],
                "dealer_index": 0,
                "start_player_index": 0,
                "p0_tiles": list(p0),
                "p1_tiles": list(p1),
                "p2_tiles": list(p2),
                "p3_tiles": list(p3),
                "tiles_list": list(tiles_list),
                "action_ticks": ticks,
            }
        },
    }


def test_remember_local_present_in_every_rule_gamestate():
    root = Path(__file__).resolve().parents[1]
    missing = []
    for rule, rel in _STATE_FILES.items():
        text = (root / rel).read_text(encoding="utf-8")
        if "remember_local_record_detail" not in text:
            missing.append(rule)
    assert missing == [], f"这些规则终局未挂本地牌谱: {missing}"


def test_unity_script_translations_match_decoder_and_hand_rules():
    assert parse_kan_mo_gang_flag(["ag", "11", "T"]) is True
    assert parse_kan_mo_gang_flag(["jg", "37", "F"]) is False
    try:
        parse_kan_mo_gang_flag(["ag", "11"])
        raise AssertionError("旧两段暗杠应抛错")
    except ValueError:
        pass
    assert needs_ron_tile([11] * 13, "guobiao")
    assert not needs_ron_tile([11] * 14, "guobiao")
    assert needs_ron_tile([1001] * 11, "hongque")
    assert not needs_ron_tile([1001] * 12, "hongque")
    assert build_angang_mask_from_removed([11, 11, 11, 11], "changsha")[0] == 0
    assert build_angang_mask_from_removed([11, 11, 11, 11], "riichi")[0] == 2
    assert build_angang_mask_from_removed([11, 11, 11, 11], "sichuan")[2] == 0
    assert try_parse_hepai_tile(["hu_self", "0", "12", "[]", "[]", "0", "20", "21"], "classical") == 21
    assert try_parse_hepai_tile(["hu_first", "1", "8", "[]", "[]", "22"], "guobiao") == 22
    sichuan = resolve_record_flags({"rule": "sichuan", "sub_rule": "sichuan/standard"})
    assert sichuan.kong_replacement_from_front
    assert sichuan.infers_dingque_from_discards
    assert sichuan.peek_ankan
    hongque = resolve_record_flags({"rule": "hongque", "sub_rule": "hongque/v1.6"})
    assert hongque.jiagang_extends_last_meld
    classical = resolve_record_flags(rule="classical")
    assert classical.record_hu_tile_tick_index == 7


def test_example_jsonc_replays_without_throwing():
    paths = sorted(_PUBLIC.glob("game_record_example_*.jsonc"))
    assert paths, "缺少本地示例牌谱"
    names = {path.stem.replace("game_record_example_", "") for path in paths}
    expected = {
        "guobiao",
        "qingque",
        "classical",
        "riichi",
        "changsha",
        "taiwan",
        "sichuan",
        "hongque",
        "jiandan",
    }
    assert expected <= names, f"示例牌谱缺规则: {sorted(expected - names)}"
    for path in paths:
        record = load_jsonc(path)
        _replay(record)


def test_sichuan_example_infers_dingque_after_cut():
    record = load_jsonc(_PUBLIC / "game_record_example_sichuan.jsonc")
    sim = RecordSim(record)
    sim.load_round("round_index_1")
    assert sim.flags.infers_dingque_from_discards
    sim.apply_tick(stringify_tick(["c", 25, "F"]))
    assert sim.players[0].dingque_suit == 3


def test_hongque_example_bd_consumes_wall_not_from_front():
    record = load_jsonc(_PUBLIC / "game_record_example_hongque.jsonc")
    sim = RecordSim(record)
    sim.load_round("round_index_1")
    wall_before = list(sim.current_tiles_list)
    assert wall_before[0] == 1081
    for raw in record["game_round"]["round_index_1"]["action_ticks"]:
        tick = stringify_tick(raw)
        sim.apply_tick(tick)
        if tick[0] == "bd":
            break
    assert 1082 in sim.players[1].tile_list
    assert 1082 not in sim.current_tiles_list
    assert len(sim.current_tiles_list) == len(wall_before) - 1
    assert sim.current_tiles_list[0] == wall_before[0]


def test_sichuan_gd_draws_from_front():
    standard_13 = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]
    record = _round(
        "sichuan",
        "sichuan/standard",
        blood_battle="true",
        p0=[11, 11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 25],
        p1=list(standard_13),
        p2=list(standard_13),
        p3=list(standard_13),
        tiles_list=[31, 32, 33],
        ticks=[["ag", 11, "F", 11, 11, 11, 11], ["gd", 31]],
    )
    sim = RecordSim(record)
    sim.apply_all("round_index_1")
    assert sim.current_tiles_list == [32, 33]
    assert 31 in sim.players[0].tile_list


def test_consistent_minigames_for_all_rules():
    standard_13 = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]
    records = {
        "guobiao": _round(
            "guobiao",
            "guobiao/standard",
            p0=standard_13 + [25],
            p1=list(standard_13),
            p2=[32, 32, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22],
            p3=list(standard_13),
            tiles_list=[32, 33, 34],
            ticks=[
                ["c", 25, "F"],
                ["d", 32],
                ["c", 32, "T"],
                ["p", 32, 2, 32, 32],
                ["c", 22, "F"],
                ["hu_first", 3, 8, ["平和"], [-8, -8, -8, 24], 22],
                ["end"],
            ],
        ),
        "changsha": _round(
            "changsha",
            "changsha/classic_double_bird",
            p0=[11, 11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 25],
            p1=list(standard_13),
            p2=list(standard_13),
            p3=list(standard_13),
            tiles_list=[31, 32, 33],
            ticks=[
                ["ag", 11, "F", 11, 11, 11, 11],
                ["gd", 31],
                ["c", 31, "T"],
                ["liuju"],
                ["end"],
            ],
        ),
        "taiwan": _round(
            "taiwan",
            "taiwan/16",
            p0=[11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 51],
            p1=list(standard_13),
            p2=list(standard_13),
            p3=list(standard_13),
            tiles_list=[32, 33, 34] + [0] * 16,
            ticks=[
                ["bh", 51, 0, "F"],
                ["bd", 31, 0],
                ["state", "ready", 0, "earthly", "F"],
                ["c", 31, "T"],
                ["d", 32],
                ["c", 32, "T"],
                ["hu_self", 1, 5, ["平胡"], [-5, 15, -5, -5], 32],
                ["end"],
            ],
        ),
        "sichuan": _round(
            "sichuan",
            "sichuan/standard",
            blood_battle="true",
            p0=standard_13 + [25],
            p1=list(standard_13),
            p2=list(standard_13),
            p3=list(standard_13),
            tiles_list=[31, 32, 33],
            ticks=[
                ["c", 25, "F"],
                ["d", 31],
                ["c", 31, "T"],
                ["gr", "gs", 0, 0, -2, 2],
                ["liuju", "settle_hu", "hu_first", 3, 2, ["平胡"], [2, -2, 0, 0], 1],
                ["liuju", "final", [2, 0, 2, 2]],
                ["end"],
            ],
        ),
        "hongque": _round(
            "hongque",
            "hongque/v1.6",
            p0=[1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1011, 1012, 1021],
            p1=[1021, 1021, 1021, 1022, 1023, 1024, 1025, 1026, 1027, 1028, 1029],
            p2=[1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049, 1051, 1052],
            p3=[1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1069, 1071, 1072],
            tiles_list=[1081, 1082, 1083],
            ticks=[
                ["c", 1021, "F"],
                ["p", 1021, 1, 1021, 1021],
                ["jg", 1021, "F"],
                ["bd", 1082, 1],
                ["c", 1082, "T"],
                ["hu_first", 2, 8, ["清顺"], [0, 0, 8, 0], 1082],
                ["end"],
            ],
        ),
        "riichi": _round(
            "riichi",
            "riichi/standard",
            starting_score=25000,
            p0=standard_13 + [25],
            p1=list(standard_13),
            p2=list(standard_13),
            p3=list(standard_13),
            tiles_list=[31, 32, 33, 34, 35, 36],
            ticks=[
                ["riichi", 0, 0],
                ["c", 25, "F", "H"],
                ["d", 31],
                ["c", 31, "T"],
                ["hu_riichi", 1, "hu_first", 3, 40, ["立直"], [5200, -5200, 0, 0], [61], [], 0, 0, 0],
                ["end"],
            ],
        ),
        "jiandan": _round(
            "jiandan",
            "jiandan/standard",
            p0=standard_13 + [25],
            p1=list(standard_13),
            p2=list(standard_13),
            p3=list(standard_13),
            tiles_list=[31, 32, 33],
            ticks=[["c", 25, "F"], ["d", 31], ["c", 31, "T"], ["liuju"], ["end"]],
        ),
    }
    hongque_last = None
    sichuan_last = None
    taiwan_last = None
    changsha_last = None
    for rule, record in records.items():
        last = _replay(record)[0]
        if rule == "hongque":
            hongque_last = last
        elif rule == "sichuan":
            sichuan_last = last
        elif rule == "taiwan":
            taiwan_last = last
        elif rule == "changsha":
            changsha_last = last

    assert 1021 not in hongque_last["players"]["0"]["tile_list"]
    assert [3, 1021] == hongque_last["players"]["1"]["combination_masks"][0][-2:]
    assert 1082 in hongque_last["players"]["2"]["tile_list"]
    assert sichuan_last["players"]["0"]["score"] == 2
    assert sichuan_last["players"]["1"]["score"] == 0
    assert 51 in taiwan_last["players"]["0"]["huapai_list"]
    assert 31 in taiwan_last["players"]["0"]["discard_tiles"]
    assert changsha_last["players"]["0"]["combination_tiles"][0].startswith("G")
    assert changsha_last["players"]["0"]["combination_masks"][0][0] == 0


def test_taiwan_bd_consumes_wall_from_back():
    record = _round(
        "taiwan",
        "taiwan/16",
        p0=[11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 51],
        p1=[11] * 13,
        p2=[12] * 13,
        p3=[13] * 13,
        tiles_list=[32, 33, 99],
        ticks=[["bh", 51, 0, "F"], ["bd", 31, 0]],
    )
    sim = RecordSim(record)
    sim.apply_all("round_index_1")
    assert sim.current_tiles_list == [32, 33]
    assert 31 in sim.players[0].tile_list


def test_hongque_live_record_replays_and_local_detail():
    asyncio.run(_hongque_live())


async def _hongque_live() -> None:
    from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
    from server.gamestate.game_hongque.record import persist_and_remember, code_to_id

    room = {
        "room_id": "895001",
        "game_round": 1,
        "random_seed": 7,
        "player_list": [101, 102, 103, 104],
        "player_settings": {uid: {"username": f"P{uid}"} for uid in (101, 102, 103, 104)},
    }
    state = HongqueGameState(None, room, gamestate_id="record-live")
    await state._start_round()
    round_data = state.game_record["game_round"]["round_index_1"]
    assert len(round_data["p0_tiles"]) == 12
    assert len(round_data["p1_tiles"]) == 11
    assert round_data["tiles_list"][0] == code_to_id(state.wall[-1])

    for _ in range(6):
        player = state.players[state.current_player_index]
        tile = player.drawn_tile or player.hand[-1]
        await state.submit_action(player.user_id, "discard", tile=tile, action_tick=state.action_tick)
        if state.phase == "claim":
            for index in list(state.claim_options):
                await state.submit_action(
                    state.players[index].user_id, "pass", action_tick=state.action_tick
                )
        if state.phase == "round_end":
            break

    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert any(tick[0] == "c" for tick in ticks)
    record = jsonable_game_record(state.game_record)
    frames = RecordSim(record).apply_all("round_index_1")
    jumped = RecordSim(record).goto_action("round_index_1", len(ticks))
    assert jumped["players"] == frames[-1]["players"]
    for frame in frames:
        for player in frame["players"].values():
            assert all(tile > 10 for tile in player["tile_list"])

    class FakeDB:
        def store_hongque_game_record(self, *_args, **_kwargs):
            return "AbCdEfGh12"

    state.db_manager = FakeDB()
    persist_and_remember(state)
    detail = state._local_record_detail
    assert detail is not None
    assert detail.game_id == "AbCdEfGh12"
    assert detail.rule == "hongque"


def test_hongque_bots_still_get_local_record_and_skip_store():
    from server.database.hongque.store_hongque import store_hongque_game_record
    from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
    from server.gamestate.game_hongque.record import persist_and_remember

    room = {"room_id": "1", "game_round": 1, "player_list": [2, 3, 4, 5]}
    state = HongqueGameState(None, room, gamestate_id="bots")
    asyncio.run(state._start_round())
    persist_and_remember(state)
    assert state._local_record_detail is not None
    bots = [SimpleNamespace(user_id=uid, username=f"bot{uid}") for uid in (2, 3, 4, 5)]
    assert store_hongque_game_record(None, state.game_record, bots, "custom", "1/4") is None


def test_all_rule_stores_skip_bots():
    bots = [
        SimpleNamespace(
            user_id=uid,
            username=f"bot{uid}",
            score=0,
            original_player_index=i,
            record_counter=SimpleNamespace(rank_result=i + 1),
        )
        for i, uid in enumerate((2, 3, 4, 5))
    ]
    dummy = {"game_title": {"rule": "x", "sub_rule": "x"}, "game_round": {}}
    from server.database.guobiao.store_guobiao import store_guobiao_game_record
    from server.database.qingque.store_qingque import store_qingque_game_record
    from server.database.classical.store_classical import store_classical_game_record
    from server.database.riichi.store_riichi import store_riichi_game_record
    from server.database.changsha.store_changsha import store_changsha_game_record
    from server.database.taiwan.store_taiwan import store_taiwan_game_record
    from server.database.sichuan.store_sichuan import store_sichuan_game_record
    from server.database.jiandan.store_jiandan import store_jiandan_game_record
    from server.database.hongque.store_hongque import store_hongque_game_record

    for store in (
        store_guobiao_game_record,
        store_qingque_game_record,
        store_classical_game_record,
        store_riichi_game_record,
        store_changsha_game_record,
        store_taiwan_game_record,
        store_sichuan_game_record,
        store_jiandan_game_record,
        store_hongque_game_record,
    ):
        assert store(None, dummy, bots, "custom", "1/4") is None


def test_sichuan_score_changes_accumulate_gang_and_settle_hu():
    from server.gamestate.verifier.record_sim.decoder import accumulate_score_changes_from_tick

    seats = [0, 1, 2, 3]
    acc = None
    acc = accumulate_score_changes_from_tick(acc, ["g", 31, 0, "gs", 3, -1, -1, -1], seats)
    acc = accumulate_score_changes_from_tick(
        acc, ["hu_first", 0, 2, ["平胡"], [0, 0, 0, 0], 31], seats
    )
    acc = accumulate_score_changes_from_tick(
        acc, ["liuju", "settle_hu", "hu_first", 0, 2, ["平胡"], [2, -2, 0, 0], 1], seats
    )
    # liuju final 是绝对分，不能当 delta 累加
    acc = accumulate_score_changes_from_tick(acc, ["liuju", "final", [2, 0, 2, 2]], seats)
    assert acc == [5, -3, -1, -1]
