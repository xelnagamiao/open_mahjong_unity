"""延时观战/牌谱：无座位 d/c 必须跟 start_player_index 或 reset，不能吃上一局残留指针。"""
from __future__ import annotations

from pathlib import Path

from server.gamestate.verifier.record_sim import (
    RecordSim,
    load_jsonc,
    resolve_acting_player,
    stringify_tick,
)

_PUBLIC = Path(__file__).resolve().parents[1] / "public"
_STATE_ROOT = Path(__file__).resolve().parents[1]
_EXAMPLE_RULES = (
    "guobiao",
    "qingque",
    "classical",
    "riichi",
    "changsha",
    "taiwan",
    "sichuan",
    "hongque",
    "zhongyong",
)
_RESET_ONLY = {
    "guobiao": "game_guobiao/GuobiaoGameState.py",
}
_NO_RESET = {
    "riichi": "game_riichi/RiichiGameState.py",
    "classical": "game_classical/ClassicalGameState.py",
    "qingque": "game_mmcr/QingqueGameState.py",
    "sichuan": "game_sichuan/SichuanGameState.py",
    "changsha": "game_changsha/ChangshaGameState.py",
    "taiwan": "game_taiwan/TaiwanGameState.py",
    "jiandan": "game_zhongyong/ZhongyongGameState.py",
    "hongque": "game_hongque/record.py",
}
_POINTER_BEFORE_HEADER = {
    "riichi": "game_riichi/RiichiGameState.py",
    "classical": "game_classical/ClassicalGameState.py",
    "qingque": "game_mmcr/QingqueGameState.py",
    "sichuan": "game_sichuan/SichuanGameState.py",
    "changsha": "game_changsha/ChangshaGameState.py",
    "taiwan": "game_taiwan/TaiwanGameState.py",
    "jiandan": "game_zhongyong/ZhongyongGameState.py",
    "zhongyong": "game_zhongyong/ZhongyongGameState.py",
    "hongque": "game_hongque/init_tiles.py",
    "guobiao": "game_guobiao/GuobiaoGameState.py",
}
_UNSEATED = {"d", "gd", "c"}
_OPENING_STOP = {
    "p", "cl", "cm", "cr", "g", "ag", "jg",
    "hu_self", "hu_first", "hu_second", "hu_third", "hu_riichi",
    "liuju", "shuhewei", "riichi", "ryuukyoku", "end", "gr", "blood",
    "jiuzhongjiupai",
}


def _mini_round(start_player_index: int, ticks: list, extra_ticks_prefix=None) -> dict:
    p0 = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]
    p1 = [25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36, 37, 38]
    p2 = [39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 51, 52, 53]
    p3 = [54, 55, 56, 57, 58, 59, 61, 62, 63, 64, 65, 66, 67]
    action_ticks = list(extra_ticks_prefix or []) + ticks
    return {
        "game_title": {
            "rule": "riichi",
            "sub_rule": "riichi/standard",
            "p0_uid": 101,
            "p1_uid": 102,
            "p2_uid": 103,
            "p3_uid": 104,
        },
        "game_round": {
            "round_index_1": {
                "round_index": 1,
                "current_round": 1,
                "seats": [0, 1, 2, 3],
                "dealer_index": 0,
                "start_player_index": start_player_index,
                "p0_tiles": list(p0),
                "p1_tiles": list(p1),
                "p2_tiles": list(p2),
                "p3_tiles": list(p3),
                "tiles_list": [71, 72, 73],
                "action_ticks": action_ticks,
            }
        },
    }


def _hand_lens(sim: RecordSim) -> list[int]:
    return [len(sim.players[i].tile_list) for i in range(4)]


def test_leftover_start_player_index_grows_wrong_hand_and_still_fills_river():
    """上一局指针残留时：摸加到别人、切牌可能删不掉，河却照进。对应「只摸不打、弃牌区仍更新」。"""
    sim = RecordSim(_mini_round(2, [["d", 71], ["c", 11, "F"]]))
    sim.load_round("round_index_1")
    assert sim.current_player_index == 2
    sim.apply_tick(stringify_tick(["d", 71]))
    assert _hand_lens(sim) == [13, 13, 14, 13]
    assert 71 in sim.players[2].tile_list
    sim.apply_tick(stringify_tick(["c", 11, "F"]))
    assert 11 not in sim.players[2].tile_list  # 11 本就不在 2 号手牌
    assert sim.players[2].discard_tiles == [11]
    assert _hand_lens(sim) == [13, 13, 14, 13]
    assert sim.players[0].discard_tiles == []
    assert 11 in sim.players[0].tile_list


def test_correct_start_player_index_draw_cut_stays_on_dealer():
    sim = RecordSim(_mini_round(0, [["d", 71], ["c", 11, "F"]]))
    sim.load_round("round_index_1")
    sim.apply_tick(stringify_tick(["d", 71]))
    assert _hand_lens(sim) == [14, 13, 13, 13]
    sim.apply_tick(stringify_tick(["c", 11, "F"]))
    assert _hand_lens(sim) == [13, 13, 13, 13]
    assert sim.players[0].discard_tiles == [11]
    assert 11 not in sim.players[0].tile_list
    assert 71 in sim.players[0].tile_list


def test_reset_tick_overrides_leftover_start_player_index():
    sim = RecordSim(_mini_round(2, [["d", 71], ["c", 11, "F"]], extra_ticks_prefix=[["reset", 0]]))
    sim.apply_all("round_index_1")
    assert _hand_lens(sim) == [13, 13, 13, 13]
    assert sim.players[0].discard_tiles == [11]
    assert sim.players[2].discard_tiles == []
    assert sim.current_player_index == 1


def test_example_records_unseated_draw_cut_follow_pointer():
    paths = sorted(_PUBLIC.glob("game_record_example_*.jsonc"))
    names = {path.stem.replace("game_record_example_", "") for path in paths}
    assert set(_EXAMPLE_RULES) <= names, f"示例牌谱缺规则: {sorted(set(_EXAMPLE_RULES) - names)}"
    for path in paths:
        record = load_jsonc(path)
        rule = path.stem.replace("game_record_example_", "")
        for round_key, round_data in (record.get("game_round") or {}).items():
            start = int(round_data.get("start_player_index") or 0)
            sim = RecordSim(record)
            sim.load_round(round_key)
            assert sim.current_player_index == start, f"{rule} {round_key} 开局指针"
            seen_unseated = False
            for raw in round_data.get("action_ticks") or []:
                tick = stringify_tick(raw)
                if not tick:
                    continue
                action = tick[0]
                # 示例 jsonc 后半常是解码夹具，鸣牌/和牌不必构成合法牌局。
                if action in _OPENING_STOP:
                    break
                if action == "reset":
                    sim.apply_tick(tick)
                    continue
                acting = resolve_acting_player(tick, action, sim.current_player_index)
                before = list(_hand_lens(sim))
                if action in _UNSEATED:
                    if not seen_unseated:
                        assert acting == sim.current_player_index, (
                            f"{rule} {round_key} 首个无座位 {action} 应落在指针 {sim.current_player_index}，实际 {acting}"
                        )
                        seen_unseated = True
                    if action == "c":
                        cut_tile = int(tick[1])
                        assert cut_tile in sim.players[acting].tile_list, (
                            f"{rule} {round_key} 切牌 {cut_tile} 不在座位 {acting} 手牌 {sim.players[acting].tile_list}"
                        )
                sim.apply_tick(tick)
                after = _hand_lens(sim)
                if action in ("d", "gd", "bd"):
                    assert after[acting] == before[acting] + 1, f"{rule} {round_key} {action} 手牌未+1"
                    for seat, (old, new) in enumerate(zip(before, after)):
                        if seat != acting:
                            assert new == old, f"{rule} {round_key} {action} 误改座位 {seat}"
                elif action == "c":
                    assert after[acting] == before[acting] - 1, f"{rule} {round_key} 切牌手牌未-1"
                    assert sim.players[acting].discard_tiles[-1] == int(tick[1])


def test_only_guobiao_stores_reset():
    guobiao = (_STATE_ROOT / _RESET_ONLY["guobiao"]).read_text(encoding="utf-8")
    assert "player_action_record_reset" in guobiao
    for rule, rel in _NO_RESET.items():
        text = (_STATE_ROOT / rel).read_text(encoding="utf-8")
        assert "player_action_record_reset" not in text, f"{rule} 不应把 reset 写入牌谱"


def test_rules_set_pointer_before_round_header():
    for rule, rel in _POINTER_BEFORE_HEADER.items():
        text = (_STATE_ROOT / rel).read_text(encoding="utf-8")
        if rule == "hongque":
            assign_at = text.find("game_state.current_player_index = game_state.dealer_index")
            header_at = text.find("snapshot_round_header(game_state)")
            assert 0 <= assign_at < header_at
            continue
        if rule in {"jiandan", "zhongyong"}:
            assert "self.current_player_index = self.dealer_index" in text
            recording = (_STATE_ROOT / "game_zhongyong/recording.py").read_text(encoding="utf-8")
            assert "init_game_round(self)" in recording
            continue
        header_at = text.find("init_game_round(")
        window = text[max(0, header_at - 500) : header_at]
        assert "current_player_index" in window, f"{rule} 局头快照前未设置 current_player_index"
