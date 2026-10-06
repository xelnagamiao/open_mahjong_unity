"""Three-player MCR keeps the standard fan table, calls and room settings."""
import asyncio
import json
import os
from collections import Counter
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .GuobiaoGameState import GuobiaoGameState
from .action_check import (
    check_action_after_cut, check_action_hand_action, check_action_jiagang,
    check_hepai, opening_win_fan, refresh_waiting_tiles,
)
from .init_tiles import init_guobiao_tiles
from .sanma import SUB_RULE, filter_tiles, standard_score_changes, cuohe_payments, round_wind, ron_action
from .wait_action import wait_action, select_tactical_initial_submission
from ..public.logic_common import player_index_next, next_current_num, get_index_relative_position
from ..public.next_game_round import next_game_round_guobiao_switchseat
from ..public.duplicate_wall import duplicate_match_complete, configure_duplicate_state
from ..public.ai.get_action import get_ai_action
from ...game_calculation.game_calculation_service import GameCalculationService


HAND = [11, 11, 11, 21, 22, 23, 31, 32, 33, 44, 44, 44, 45]


def sanma(*, flowers=True, sub_rule=SUB_RULE, count=None, **settings):
    count = count or (3 if sub_rule == SUB_RULE else 4)
    room = dict(player_list=list(range(101, 101 + count)), room_id=123456,
                round_timer=20, step_timer=5, game_round=1, tips=False,
                room_rule="guobiao", room_type="custom", sub_rule=sub_rule,
                use_flowers=flowers, allow_spectator=False, **settings)
    server = SimpleNamespace(user_id_to_connection={uid: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
        for uid in room["player_list"]},
        gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock()),
        room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock()))
    db = Mock()
    db.get_rank_data.return_value = None
    db.store_guobiao_game_record.return_value = None
    state = GuobiaoGameState(server, room, GameCalculationService(), db, "sanma-test")
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
        player.hand_tiles = HAND[:]
    state.tiles_list = [19, 29, 39]
    state.broadcast_result = AsyncMock()
    state.broadcast_refresh_player_tag_list = AsyncMock()
    return state


@pytest.mark.parametrize("flowers", [False, True])
@pytest.mark.parametrize("sub_rule,count,total", [(SUB_RULE, 3, 108), ("guobiao/standard", 4, 136)])
@pytest.mark.parametrize("seed", [1, 42, 20261005])
def test_wall_deal_and_flower_toggle(flowers, sub_rule, count, total, seed):
    g = sanma(flowers=flowers, sub_rule=sub_rule)
    g.master_seed = seed
    for p in g.player_list: p.hand_tiles = []
    init_guobiao_tiles(g)
    physical = g.tiles_list + [t for p in g.player_list for t in p.hand_tiles]
    assert len(physical) == total + (8 if flowers else 0)
    counts = Counter(physical)
    assert all(n == (1 if t > 50 else 4) for t, n in counts.items())
    assert [len(p.hand_tiles) for p in g.player_list] == [14] + [13] * (count - 1)
    assert not any(p.has_draw_slot for p in g.player_list)
    assert all((t in counts) == (count == 4) for t in range(12, 19))
    assert counts[11] == counts[19] == counts[44] == 4
    assert all((t in counts) == flowers for t in range(51, 59))


@pytest.mark.parametrize("count", [3, 4])
@pytest.mark.parametrize("winner", range(3))
@pytest.mark.parametrize("fan", [8, 16, 88, 111])
@pytest.mark.parametrize("tsumo", [False, True])
def test_standard_payments_use_actual_payers(count, winner, fan, tsumo):
    source = (winner + 1) % count
    changes = standard_score_changes(count, winner, fan, None if tsumo else source)
    assert len(changes) == count and sum(changes) == 0
    assert changes[winner] == ((count - 1) * (fan + 8) if tsumo else fan + (count - 1) * 8)
    assert changes[source] == -(fan + 8)
    for seat in set(range(count)) - {winner, source}:
        assert changes[seat] == (-(fan + 8) if tsumo else -8)


@pytest.mark.parametrize("count,sub", [(3, SUB_RULE), (4, "guobiao/standard")])
def test_all_rounds_rotate_reset_and_finish_without_extra_seat(count, sub):
    g = sanma(sub_rule=sub); g.max_round = 4
    dealers = Counter()
    for round_no in range(1, 4 * count + 1):
        assert g.current_round == round_no
        assert round_wind(round_no, count) == "东南西北"[(round_no - 1) // count]
        assert len(g.player_list) == count and set(g.action_dict) == set(range(count))
        assert [p.player_index for p in g.player_list] == list(range(count))
        assert duplicate_match_complete(g) is (round_no == 4 * count)
        dealers[g.player_list[0].original_player_index] += 1
        if round_no < 4 * count:
            for p in g.player_list:
                p.discard_tiles = [21]; p.huapai_list = [51]; p.combination_tiles = ["k44"]
                p.combination_mask = ["mask"]; p.tag_list = ["peida"]
            next_game_round_guobiao_switchseat(g)
            assert all(not p.hand_tiles and not p.discard_tiles and not p.huapai_list
                       and not p.combination_tiles and not p.combination_mask
                       and "peida" not in p.tag_list for p in g.player_list)
    assert set(dealers.values()) == {4}


@pytest.mark.parametrize("source", range(3))
def test_turn_wrap_chow_and_claim_order(source):
    g = sanma(); g.current_player_index = source
    for p in g.player_list: p.hand_tiles = [22, 23, 24, 25, 26]
    actions = check_action_after_cut(g, 21)
    next_seat = (source + 1) % 3
    assert set(actions) == {0, 1, 2} and actions[source] == []
    assert [i for i in actions if "chi_right" in actions[i]] == [next_seat]
    assert g.resolve_hepai_player_index("hu_first") == next_seat
    assert g.resolve_hepai_player_index("hu_second") == (source + 2) % 3
    assert ron_action(next_seat, source, 3) == "hu_first"
    assert ron_action((source + 2) % 3, source, 3) == "hu_second"
    assert ron_action(source, source, 3) == "hu_self"
    assert get_index_relative_position(source, next_seat, 3) == "right"
    assert get_index_relative_position(source, (source + 2) % 3, 3) == "left"
    player_index_next(g)
    assert g.current_player_index == next_seat == next_current_num(source, 3)


@pytest.mark.parametrize("seat", range(3))
@pytest.mark.parametrize("kind", ["peng", "gang", "angang", "jiagang", "buhua"])
def test_standard_calls_are_available_for_every_seat(seat, kind):
    g = sanma(); g.current_player_index = (seat + 2) % 3
    p = g.player_list[seat]
    if kind in ("peng", "gang"):
        p.hand_tiles = [44] * (2 if kind == "peng" else 3)
        assert kind in check_action_after_cut(g, 44)[seat]
    else:
        g.current_player_index = seat
        p.hand_tiles = [44] * (4 if kind == "angang" else 1) + [21]
        if kind == "jiagang": p.combination_tiles = ["k44"]
        if kind == "buhua": p.hand_tiles += [51]
        assert kind in check_action_hand_action(g, seat)[seat]
    assert "nuki" not in check_action_hand_action(g, seat)[seat]


def test_waiting_tiles_remove_only_unavailable_manzu_and_keep_north():
    g = sanma(); g.player_list[1].hand_tiles = [21, 24, 27, 32, 35, 38, 41, 42, 43, 44, 45, 46, 47]
    standard = g.calculation_service.GB_tingpai_check(g.player_list[1].hand_tiles, [])
    assert set(standard) == {13, 16, 19}
    refresh_waiting_tiles(g, 1)
    assert g.player_list[1].waiting_tiles == {19}
    assert filter_tiles([11, 12, 18, 19, 44, 58], SUB_RULE) == [11, 19, 44, 58]
    assert filter_tiles([11, 12, 18, 19], "guobiao/standard") == [11, 12, 18, 19]


@pytest.mark.parametrize("round_no", range(1, 13))
@pytest.mark.parametrize("seat", range(3))
@pytest.mark.parametrize("way", ["dianhe", "qianggang", "handgot"])
def test_fan_table_matches_four_player_mcr_in_same_wind(round_no, seat, way):
    results = []
    for sub in (SUB_RULE, "guobiao/standard"):
        g = sanma(sub_rule=sub)
        g.current_round = round_no if sub == SUB_RULE else (round_no - 1) // 3 * 4 + 1
        g.current_player_index = seat if way == "handgot" else (seat + 2) % 3
        g.player_list[seat].waiting_tiles = {45}
        if way == "handgot": g.player_list[seat].hand_tiles.append(45)
        actions = {i: [] for i in range(len(g.player_list))}
        check_hepai(g, actions, 45, seat, way)
        results.append(next(iter(g.result_dict.values())))
        assert any(a.startswith("hu_") for a in actions[seat])
    assert results[0] == results[1]


@pytest.mark.parametrize("score,flowers,allow,expected", [(7, 0, False, False), (8, 0, False, True),
    (8, 1, False, False), (9, 1, False, True), (7, 0, True, True)])
def test_eight_fan_gate_excludes_flowers_and_preserves_open_cuohe(score, flowers, allow, expected):
    g = sanma(open_cuohe=allow)
    g.calculation_service.GB_hepai_check = Mock(return_value=(score, ["测试番"]))
    g.player_list[1].huapai_list = [51] * flowers
    actions = {i: [] for i in range(3)}
    check_hepai(g, actions, 45, 1, "dianhe")
    assert ("hu_first" in actions[1]) is expected


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("seat", range(3))
def test_optional_opening_wins_keep_guobiao_conditions(enabled, seat):
    g = sanma(tian_di_ren_he=enabled); g.current_player_index = seat
    assert opening_win_fan(g, seat, "handgot") == (("天和" if seat == 0 else "人和") if enabled else None)
    g.current_player_index = 0; g.player_list[0].discard_tiles = [21]
    assert opening_win_fan(g, seat, "dianhe") == ("地和" if enabled and seat != 0 else None)
    g.player_list[2].combination_tiles = ["a44"]
    assert opening_win_fan(g, seat, "handgot") is None


@pytest.mark.parametrize("count", [3, 4])
@pytest.mark.parametrize("penalty", [0, 1])
def test_cuohe_payment_setting_preserves_fixed_penalty(count, penalty):
    assert cuohe_payments(count, penalty) == ((40, 0) if penalty else (10 * (count - 1), 10))


@pytest.mark.parametrize("seat", range(3))
@pytest.mark.parametrize("opening", [False, True])
@pytest.mark.parametrize("flowers", [False, True])
def test_reconnect_preserves_three_seats_settings_hidden_hands_and_pending_ask(seat, opening, flowers):
    async def run():
        from ..public.random_seed_manager import setup_random_seed_system
        g = sanma(flowers=flowers, tian_di_ren_he=opening)
        g.master_seed, g.salt, g.commitment, g.isPlayerSetRandomSeed = setup_random_seed_system(42)
        g.current_player_index = seat; g.game_status = "waiting_hand_action"
        g.action_dict = {i: ["cut"] if i == seat else [] for i in range(3)}
        g.waiting_players_list = [seat]
        p = g.player_list[seat]; p.tag_list = ["offline"]
        await g.player_reconnect(p.user_id)
        calls = g.game_server.user_id_to_connection[p.user_id].websocket.send_json.await_args_list
        messages = [call.args[0] for call in calls]
        info = next(message["game_info"] for message in messages if message["type"].endswith("game_start"))
        assert info["sub_rule"] == SUB_RULE and info["tian_di_ren_he"] is opening
        assert info["use_flowers"] is flowers and len(info["players_info"]) == 3
        assert "offline" not in p.tag_list
        for player in info["players_info"]:
            assert bool(player.get("hand_tiles")) is (player["player_index"] == seat)
        ask = next(message["ask_hand_action_info"] for message in messages
                   if message["type"].endswith("broadcast_hand_action"))
        assert ask["player_index"] == seat and ask["action_list"] == ["cut"]
    asyncio.run(run())


@pytest.mark.parametrize("source", range(3))
@pytest.mark.parametrize("qianggang", [False, True])
def test_higher_priority_ron_wins_when_both_players_respond(source, qianggang):
    async def run():
        g = sanma(); g.current_player_index = source
        g.player_list[source].discard_tiles = [45]
        g.jiagang_tile = 45
        g.game_status = "waiting_action_qianggang" if qianggang else "waiting_action_after_cut"
        near, far = (source + 1) % 3, (source + 2) % 3
        g.action_dict = {source: [], near: ["hu_first", "pass"], far: ["hu_second", "pass"]}
        task = asyncio.create_task(wait_action(g)); await asyncio.sleep(0)
        for seat, action in ((far, "hu_second"), (near, "hu_first")):
            await get_ai_action(g, seat, action, False, None, None, None)
        await asyncio.wait_for(task, 2)
        assert g.hu_class == "hu_first" and g.resolve_hepai_player_index(g.hu_class) == near
    asyncio.run(run())


def test_tactical_ties_use_three_seat_order_and_unsupported_duplicate_is_rejected():
    g = sanma(); g.current_player_index = 2
    submissions = [(1, {"action_type": "peng"}), (0, {"action_type": "peng"})]
    assert select_tactical_initial_submission(g, submissions) is submissions[1]
    assert select_tactical_initial_submission(g, []) is None
    assert select_tactical_initial_submission(g, [(1, {"action_type": "pass"})])[0] == 1
    with pytest.raises(ValueError, match="三人国标"):
        configure_duplicate_state(g, dict(room_rule="guobiao", sub_rule=SUB_RULE, duplicate_key="test"))
    from ...database.duplicate_walls import validate_duplicate_wall
    with pytest.raises(ValueError, match="三人国标"):
        validate_duplicate_wall({}, dict(room_rule="guobiao", sub_rule=SUB_RULE))


@pytest.mark.parametrize("winner", range(3))
def test_real_loop_self_draw_settlement_for_every_seat(winner):
    async def run():
        g = sanma(flowers=False); g.current_round = 3; g.room_random_seed = 42
        final_result = AsyncMock()
        def deal(state):
            pool = Counter({t: 4 for t in filter_tiles(list(range(11, 20)) + list(range(21, 30))
                + list(range(31, 40)) + list(range(41, 48)), SUB_RULE)})
            win_hand = HAND + ([45] if winner == 0 else [])
            pool.subtract(win_hand)
            available = list(pool.elements())
            for p in state.player_list:
                # game_loop shuffles entry order before the first hand; logical seats drive this scenario.
                p.hand_tiles = win_hand[:] if p.player_index == winner else [available.pop(0) for _ in range(14 if p.player_index == 0 else 13)]
                p.discard_tiles = []; p.huapai_list = []; p.has_draw_slot = False
            if winner:
                available.remove(45); available.insert(winner - 1, 45)
            state.tiles_list = available
        original_sleep = asyncio.sleep
        async def instant(_): await original_sleep(0)
        async def automated_wait():
            task = asyncio.create_task(wait_action(g)); await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p = g.player_list[seat]; choices = g.action_dict[seat]
                action = "hu_self" if seat == winner and "hu_self" in choices else "cut" if "cut" in choices else "ready" if "ready" in choices else "pass"
                tile = p.hand_tiles[-1] if action == "cut" else None
                await get_ai_action(g, seat, action, bool(p.has_draw_slot and tile == p.hand_tiles[-1]), tile, None, None)
            await task
        g.wait_action = automated_wait
        with patch("server.gamestate.game_guobiao.GuobiaoGameState.init_guobiao_tiles", deal), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.broadcast_result", final_result), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.asyncio.sleep", instant), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.run_synced_hu_ready_phase", new=AsyncMock()), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.vote_checkpoint", new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_chinese(), 4)
        payload = final_result.await_args.kwargs
        assert payload["hepai_player_index"] == winner and payload["hu_class"] == "hu_self"
        expected = standard_score_changes(3, winner, payload["hu_score"])
        assert [p.score for p in sorted(g.player_list, key=lambda p: p.player_index)] == expected
        assert list(payload["score_changes"].values()) == expected and sum(expected) == 0
        g.db_manager.store_guobiao_game_stats.assert_not_called()
        g.db_manager.store_guobiao_fan_stats.assert_not_called()
    asyncio.run(run())


@pytest.mark.parametrize("flowers", [False, True])
@pytest.mark.parametrize("circles", [1, 2, 4])
@pytest.mark.parametrize("seed", [1, 20261005])
def test_complete_matches_record_and_replay(flowers, circles, seed, caplog):
    async def run():
        g = sanma(flowers=flowers); g.max_round = circles; g.room_random_seed = seed
        # Socket and database sinks are mocked; decisions enter the real queues and full game loop.
        for p in g.player_list: p.hand_tiles = []
        original_sleep = asyncio.sleep
        counts = Counter()
        async def instant(_): await original_sleep(0)
        async def automated_wait():
            task = asyncio.create_task(wait_action(g)); await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p = g.player_list[seat]; choices = g.action_dict[seat]
                tile = target = None
                action = next((a for a in ["hu_self", "hu_first", "hu_second", "buhua", "ready"] if a in choices), None)
                if action is None and "cut" in choices:
                    action = "cut"; tile = p.hand_tiles[-1]
                if action is None: action = "pass"
                counts[action] += 1
                await get_ai_action(g, seat, action, p.has_draw_slot and tile == p.hand_tiles[-1], tile, None, target)
            await task
            assert g.current_player_index in range(3) and set(g.action_dict) == {0, 1, 2}
            assert sum(p.score for p in g.player_list) == 0
        g.wait_action = automated_wait
        with patch("server.gamestate.game_guobiao.GuobiaoGameState.asyncio.sleep", instant), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.run_synced_hu_ready_phase", new=AsyncMock()), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.vote_checkpoint", new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_chinese(), 35)
        assert len(g.game_record["game_round"]) == 3 * circles
        assert counts["cut"] > 50 and bool(counts["buhua"]) == flowers
        assert g.game_record["game_title"]["player_count"] == 3
        assert all(r["action_ticks"][-1] == ["end"] for r in g.game_record["game_round"].values())
        assert not any(r.levelname == "ERROR" for r in caplog.records)
        return g
    g = asyncio.run(run())
    from ..verifier.record_sim.goto_action import RecordSim, stringify_tick
    from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick
    sim = RecordSim(g.game_record); totals = [0, 0, 0]; frames = {}
    for key, data in g.game_record["game_round"].items():
        assert len(data["seats"]) == 3 and "p3_tiles" not in data
        physical = data["tiles_list"] + sum((data[f"p{i}_tiles"] for i in range(3)), [])
        assert len(physical) == (116 if flowers else 108)
        assert not any(12 <= t <= 18 for t in physical)
        sim.load_round(key)
        for p in sim.players.values(): p.score = totals[p.original_player_index]
        frames[key] = [sim.snapshot()]; changes = None
        for raw in data["action_ticks"]:
            tick = stringify_tick(raw); sim.apply_tick(tick)
            changes = accumulate_score_changes_from_tick(changes, tick, data["seats"])
            frame = sim.snapshot(); frames[key].append(frame)
            assert set(frame["players"]) == {"0", "1", "2"}
            assert frame["current_player_index"] in range(3)
        for i in range(3): totals[i] += changes[i] if changes else 0
    assert totals == [p.score for p in sorted(g.player_list, key=lambda p: p.original_player_index)]
    directory = os.environ.get("GUOBIAO_SANMA_FIXTURES_DIR")
    if directory:
        target = Path(directory); target.mkdir(parents=True, exist_ok=True)
        name = f"gb3-{int(flowers)}-{circles}-{seed}"
        (target / (name + ".json")).write_text(json.dumps(g.game_record, ensure_ascii=False, default=str), encoding="utf-8")
        (target / (name + "-frames.json")).write_text(json.dumps(frames, ensure_ascii=False), encoding="utf-8")
