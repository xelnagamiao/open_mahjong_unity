import asyncio
from collections import Counter
import json
import os
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI

from .test_sanma import sanma
from .action_check import (check_action_after_cut, check_action_hand_action, check_action_jiagang,
                           check_action_buhua, refresh_waiting_tiles, opening_win_fan)
from .init_tiles import init_guobiao_tiles
from .four_jokers_runtime import evaluate_actions, prepare_hints
from .four_jokers_bot import choose_action, choose_cut
from .boardcast import _build_game_start_payload_for_viewer
from ...game_calculation import guobiao_sixteen as rule
from ...game_calculation.test_guobiao_sixteen import tiles

HAND = tiles("123m456p789s111z555z66z")


def state(**settings):
    g = sanma(sub_rule=rule.SUB_RULE, **settings)
    for p in g.player_list:
        p.hand_tiles = HAND[:-1]
    return g


@pytest.mark.parametrize("flowers", [False, True])
@pytest.mark.parametrize("seed", range(12))
def test_deal_and_conservation(flowers, seed):
    g = state(flowers=flowers)
    g.master_seed = seed
    for p in g.player_list:
        p.hand_tiles = []
    init_guobiao_tiles(g)
    assert [len(p.hand_tiles) for p in g.player_list] == [17, 16, 16, 16]
    all_tiles = g.tiles_list + sum((p.hand_tiles for p in g.player_list), [])
    assert Counter(all_tiles) == Counter({t: 4 for t in rule.TILES}) + Counter({t: 1 for t in range(51, 59) if flowers})
    assert len(g.tiles_list) == 71 + 8 * flowers
    assert not any(p.has_draw_slot for p in g.player_list)


@pytest.mark.parametrize("flowers", range(9))
@pytest.mark.parametrize("limit", [1, 8, 32, 64])
@pytest.mark.parametrize("wrong_win", [False, True])
def test_threshold_flowers_and_wrong_win(flowers, limit, wrong_win):
    g = state(hepai_limit=limit, open_cuohe=wrong_win)
    p = g.player_list[0]
    p.hand_tiles = HAND[:]
    p.huapai_list = list(range(51, 51 + flowers))
    refresh_waiting_tiles(g, 0, True)
    actions = check_action_hand_action(g, 0)[0]
    base, _ = rule.hepai_check(HAND, [], ["自摸", "场风东", "自风东"], 46)
    assert ("hu_self" in actions) == (base >= limit or wrong_win)
    if "hu_self" in actions:
        assert g.result_dict["hu_self"][0] == base + flowers


@pytest.mark.parametrize("source", range(4))
def test_ron_robbed_added_kong_and_worker(source):
    g = state()
    g.current_player_index = source
    g.player_list[source].discard_tiles = [46]
    async def run():
        actions = await evaluate_actions(g, check_action_after_cut, 46)
        for seat in range(4):
            if seat == source:
                assert actions[seat] == []
            else:
                assert any(a.startswith("hu_") for a in actions[seat])
        robbed = await evaluate_actions(g, check_action_jiagang, 46)
        assert sum(any(a.startswith("hu_") for a in choices) for choices in robbed.values()) == 3
        for value in g.result_dict.values():
            assert "抢杠和" in value[1]
    asyncio.run(run())


def test_fifth_meld_and_last_tile_restrictions():
    g = state()
    p = g.player_list[0]
    p.hand_tiles = tiles("2222p55z")
    p.combination_tiles = ["s12", "s25", "s38", "k41"]
    refresh_waiting_tiles(g, 0, True)
    assert "angang" in check_action_hand_action(g, 0)[0]
    g.tiles_list = []
    assert "angang" not in check_action_hand_action(g, 0)[0]
    other = g.player_list[1]
    other.hand_tiles = tiles("222p5z")
    other.combination_tiles = ["s12", "s25", "s38", "k41"]
    actions = check_action_after_cut(g, 22)[1]
    assert "peng" not in actions and "gang" not in actions
    # Five fixed exposed melds still allow a pair wait and 全求人.
    other.hand_tiles = [45]
    other.combination_tiles.append("k22")
    assert "hu_first" in check_action_after_cut(g, 45)[1]
    assert "全求人" in g.result_dict["hu_first"][1]


@pytest.mark.parametrize("enabled", [False, True])
def test_opening_bonus_and_no_unique_wait(enabled):
    g = state(tian_di_ren_he=enabled)
    p = g.player_list[0]
    p.hand_tiles = HAND[:]
    refresh_waiting_tiles(g, 0, True)
    actions = check_action_hand_action(g, 0, is_first_action=True)[0]
    assert "hu_self" in actions
    labels = g.result_dict["hu_self"][1]
    assert ("天和" in labels) == enabled
    assert "单钓将" not in labels
    p.combination_tiles = ["G11"]
    assert opening_win_fan(g, 0, "handgot") is None


def test_manual_flower_replacement_snapshots_and_hint_identity():
    g = state()
    g.tips = True
    g.player_list[0].hand_tiles = HAND[:-1] + [51]
    assert check_action_buhua(g, 0)[0] == ["buhua", "pass"]
    assert "buhua" in check_action_hand_action(g, 0)[0]
    g.player_list[0].hand_tiles = HAND[:]
    async def run():
        await prepare_hints(g, 0)
        data = _build_game_start_payload_for_viewer(g, 0)
        assert data["sub_rule"] == rule.SUB_RULE
        assert data["four_jokers_hints"]["sub_rule"] == rule.SUB_RULE
        assert data["four_jokers_hints"]["discard_waits"][46] == [46]
        assert all(not row.get("hand_tiles") for row in data["players_info"][1:])
        assert await prepare_hints(g, 0) == data["four_jokers_hints"]
    asyncio.run(run())


def test_http_hint_matches_settlement_and_rejects_overlong_input():
    from ...webapi.calc import register_calc_routes
    app = FastAPI()
    register_calc_routes(app, SimpleNamespace(calculation_service=None))
    class Client:
        def post(self, path, json):
            import json as codec
            async def run():
                events = []
                async def receive():
                    return {"type": "http.request", "body": codec.dumps(json).encode(), "more_body": False}
                async def send(event):
                    events.append(event)
                await app(dict(type="http", method="POST", path=path, raw_path=path.encode(),
                               query_string=b"", headers=[(b"content-type", b"application/json")],
                               server=("test", 80), client=("test", 1), scheme="http", root_path="", http_version="1.1"), receive, send)
                status = next(e["status"] for e in events if e["type"] == "http.response.start")
                body = b"".join(e.get("body", b"") for e in events if e["type"] == "http.response.body")
                return SimpleNamespace(status_code=status, json=lambda: codec.loads(body))
            return asyncio.run(run())
    client = Client()
    body = dict(hand_tiles=HAND, tiles_combination=[], sub_rule=rule.SUB_RULE,
                ron_way=["点和"], self_way=["自摸"], get_tile=46, hepai_limit=8)
    response = client.post("/calc/gb/sixteen/hint", json=body)
    assert response.status_code == 200
    assert response.json() == rule.calculate_hint(HAND, [], ["点和"], ["自摸"], 46, 8)
    waits = client.post("/calc/gb/sixteen/waits", json=body).json()
    assert waits["sub_rule"] == rule.SUB_RULE
    assert waits["discard_waits"]["46"] == [46]
    assert client.post("/calc/gb/sixteen/hint", json=body | {"hand_tiles": HAND + [46]}).status_code == 422
    assert client.post("/calc/gb/sixteen/hint", json=body | {"sub_rule": "guobiao/bad"}).status_code == 422


def test_reject_incompatible_duplicate_wall_and_snapshot_size():
    from ...database.duplicate_walls import validate_duplicate_wall
    from ..public.guobiao_win_snapshot import make_snapshot
    with pytest.raises(ValueError):
        validate_duplicate_wall({}, dict(room_rule="guobiao", sub_rule=rule.SUB_RULE))
    arguments = dict(total_fan=30, fans=["三暗刻"], win_type="hu_self", winning_tile=46,
                     round_index=1, action_index=4)
    assert len(make_snapshot(HAND, [], [], sub_rule=rule.SUB_RULE, **arguments)["concealed_tiles"]) == 17
    with pytest.raises(ValueError):
        make_snapshot(HAND, [], [], sub_rule="guobiao/standard", **arguments)


@pytest.mark.parametrize("flowers,circles,seed", [(True, 1, 811), (False, 1, 42), (True, 4, 20261014)])
def test_complete_matches_and_replay(flowers, circles, seed, caplog):
    from .wait_action import wait_action
    from ..public.ai.get_action import get_ai_action
    from ..public.hand_slot_utils import infer_bot_cut_class
    async def run():
        g = state(flowers=flowers)
        g.max_round = circles
        g.master_seed = seed
        for p in g.player_list:
            p.hand_tiles = []
        original_sleep, counts = asyncio.sleep, Counter()
        async def instant(_):
            await original_sleep(0)
        async def automated_wait():
            task = asyncio.create_task(wait_action(g))
            await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p = g.player_list[seat]
                choices = g.action_dict[seat]
                source = g.player_list[g.current_player_index].discard_tiles
                visible = Counter(t for other in g.player_list for t in other.discard_tiles)
                action, tile = ("ready", None) if "ready" in choices else choose_action(
                    p.hand_tiles, p.combination_tiles, choices, visible, source[-1] if source else None, sub_rule=rule.SUB_RULE)
                counts[action] += 1
                position = max((i for i, t in enumerate(p.hand_tiles) if t == tile), default=-1)
                await get_ai_action(g, seat, action,
                    infer_bot_cut_class(p.hand_tiles, tile, position, draw_slot=p.has_draw_slot) if action == "cut" else None,
                    tile if action == "cut" else None, position if action == "cut" else None, tile if action != "cut" else None)
            await task
            assert sum(p.score for p in g.player_list) == 0
        g.wait_action = automated_wait
        with patch("server.gamestate.game_guobiao.GuobiaoGameState.asyncio.sleep", instant), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.run_synced_hu_ready_phase", new=AsyncMock()), \
             patch("server.gamestate.game_guobiao.GuobiaoGameState.vote_checkpoint", new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_chinese(), 240)
        assert len(g.game_record["game_round"]) == 4 * circles
        assert sum(counts[a] for a in ("hu_self", "hu_first", "hu_second", "hu_third")) > 0
        assert bool(counts["buhua"]) == flowers
        assert not any(r.levelname == "ERROR" for r in caplog.records)
        return g, counts
    g, counts = asyncio.run(run())
    from ..verifier.record_sim.goto_action import RecordSim, stringify_tick
    from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick
    sim, totals, frames = RecordSim(g.game_record), [0] * 4, {}
    for key, data in g.game_record["game_round"].items():
        physical = data["tiles_list"] + sum((data[f"p{i}_tiles"] for i in range(4)), [])
        assert len(physical) == 136 + 8 * flowers
        assert [len(data[f"p{i}_tiles"]) for i in range(4)] == [17, 16, 16, 16]
        sim.load_round(key)
        for p in sim.players.values():
            p.score = totals[p.original_player_index]
        frames[key], changes = [sim.snapshot()], None
        for raw in data["action_ticks"]:
            tick = stringify_tick(raw)
            sim.apply_tick(tick)
            changes = accumulate_score_changes_from_tick(changes, tick, data["seats"])
            frames[key].append(sim.snapshot())
        for i in range(4):
            totals[i] += changes[i] if changes else 0
    assert totals == [p.score for p in sorted(g.player_list, key=lambda p: p.original_player_index)]
    directory = os.environ.get("GUOBIAO_SIXTEEN_FIXTURES_DIR")
    if directory:
        out = Path(directory)
        out.mkdir(parents=True, exist_ok=True)
        name = f"sixteen-{int(flowers)}-{circles}-{seed}"
        for suffix, data in [("", g.game_record), ("-frames", frames), ("-actions", dict(counts))]:
            (out / (name + suffix + ".json")).write_text(json.dumps(data, ensure_ascii=False, default=str), encoding="utf8")
