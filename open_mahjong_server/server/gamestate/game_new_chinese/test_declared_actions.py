import asyncio
import json
from types import SimpleNamespace

import pytest
from websockets.asyncio.client import connect
from websockets.asyncio.server import serve

from ...game_calculation.new_chinese import rules
from ..gamestate_router import handle_gamestate_message
from .actions import claim_actions, kong_tiles, legal_cuts
from .bot import choose
from .state_machine import Phase as P
from .test_new_chinese import move, start, table


@pytest.mark.parametrize("legacy", [False, True])
def test_declared_discard_uses_physical_draw_slot_over_websocket(legacy):
    async def run():
        s = table()
        p = s.player_list[0]
        p.declared, s.ready_order = True, [0]
        held = [11,11,14,14,22,22,25,25,33,33,36,36,41]
        p.hand_tiles, p.has_draw_slot = [*held, 14], True
        w = start(s, s.turn(0))
        ask = s.build_pending_action_payload(0)["ask_hand_action_info"]
        assert legal_cuts(s, 0) == {14} and ask["forced_cut_tiles"] == [14]
        assert set(ask["forbidden_cut_tiles"]) == set(held) - {14}
        assert ask["riichi_candidate_cuts"] == {} and "riichi_cut" not in w["actions"][0]
        server = SimpleNamespace(players={1: SimpleNamespace(user_id=p.user_id)},
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s))

        async def receive(connection):
            async def send_json(packet):
                await connection.send(json.dumps(packet))
            async for message in connection:
                await handle_gamestate_message(server, 1, json.loads(message), SimpleNamespace(send_json=send_json))
                if not s.action_queues[0].empty():
                    await s.resolve_action_window()
                    await send_json(next(p for p in s.outbound_payloads if p.get("do_action_info")))

        async with serve(receive, "127.0.0.1", 0) as listener:
            port = listener.sockets[0].getsockname()[1]
            async with connect(f"ws://127.0.0.1:{port}") as socket:
                packet = dict(type="gamestate/new_chinese/cut_tile", gamestate_id=s.gamestate_id,
                              action_tick=s.server_action_tick, cutIndex=2)
                for identity in (dict(TileId=14, cutClass=False), dict(TileId=11, cutClass=True), dict(TileId=11)):
                    await socket.send(json.dumps({**packet, **identity}))
                    assert json.loads(await socket.recv())["success"] is False
                    assert s.action_queues[0].empty() and p.hand_tiles == [*held, 14]
                await socket.send(json.dumps({**packet, "TileId": 14, **({} if legacy else {"cutClass": True})}))
                cut = json.loads(await socket.recv())["do_action_info"]
                assert p.hand_tiles == held and p.discard_tiles == [14]
                assert cut["action_list"] == ["cut"] and cut["cut_class"] and cut["cut_tile_index"] == len(held)
    asyncio.run(asyncio.wait_for(run(), 5))


def test_first_declaration_can_hand_cut_then_timeout_and_bots_only_discard_draw():
    s = table()
    p = s.player_list[0]
    p.hand_tiles = [11,11,14,14,22,22,25,25,33,33,36,36,47,41]
    p.has_draw_slot = True
    s.supplements = [47]
    w = start(s, s.turn(0))
    assert "riichi_cut" in w["actions"][0] and 47 in s.candidate_cuts
    move(s, w, {0: dict(action_type="riichi_cut", TileId=47, cutIndex=12, cutClass=False)})
    assert p.declared and p.riichi_flags == [True] and 41 in p.hand_tiles
    s.live_pending_window["deadline"] = s._timing_now()
    w = move(s, s.live_pending_window, {})
    if w["status"] == P.BAO:
        w = move(s, w, {})
    assert w["status"] == P.RESPONSE
    p.hand_tiles.append(11)
    p.has_draw_slot = True
    w = start(s, s.turn(0))
    for bot in (0, 1):
        p.user_id = bot
        decision = choose(s, 0, w["actions"][0])
        assert decision["action_type"] == "cut" and decision["cutClass"] and decision["cutIndex"] == 13
    timeout = s._build_timeout_action(0)
    assert timeout["TileId"] == 11 and timeout["cutClass"] and timeout["cutIndex"] == 13
    assert not any(a.startswith("chi") or a == "peng" for a in claim_actions(s, 1, 11)[0])


EQUAL = [11,12,13,13,13,19,19,19,41,41]
SHRINK = [13,13,13,14,15,18,18,42,42,42]
EXPAND = [14,15,15,15,18,18,18,19,19,19]


@pytest.mark.parametrize("kind, tile, remaining, draw, before, after", [
    ("gang", 16, EQUAL, None, {13,41}, {13,41}),
    ("angang", 16, EQUAL, 16, {13,41}, {13,41}),
    ("jiagang", 16, EQUAL, 16, {13,41}, {13,41}),
    ("gang", 16, SHRINK, None, {13,16,18}, {13,18}),
    ("angang", 16, SHRINK, 16, {13,16,18}, {13,18}),
    ("jiagang", 16, SHRINK, 16, {13,16,18}, {13,18}),
    ("angang", 12, EXPAND, 18, {13}, {13,14,16}),
    ("jiagang", 12, EXPAND, 18, {13}, {13,14,16}),
    ("angang", 16, SHRINK, 42, {18,42}, {13,18}),
    ("jiagang", 14, [12,13,15,15,15,18,18,19,19,19], 18, {17,18}, {11}),
    ("gang", 41, [11,11,14,14,22,22,25,25,33,33], None, {41}, set()),
])
def test_declared_kong_compares_actual_wait_sets(kind, tile, remaining, draw, before, after):
    s = table()
    p = s.player_list[1]
    p.declared, s.ready_order = True, [1]
    p.hand_tiles = remaining + [tile] * {"gang": 3, "angang": 4, "jiagang": 1}[kind]
    if kind == "jiagang":
        p.combination_tiles, p.combination_mask = [f"k{tile}"], [[1,tile,0,tile,0,tile]]
    p.has_draw_slot = kind != "gang"
    if p.has_draw_slot:
        p.hand_tiles.remove(draw)
        p.hand_tiles.sort()
        p.hand_tiles.append(draw)
    assert rules.waits(p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles, p.combination_tiles) == before
    code = ("G" if kind == "angang" else "g") + str(tile)
    assert rules.waits(remaining, [code]) == after
    allowed = bool(after) and after <= before
    s.supplements = [47]
    if kind == "gang":
        s.player_list[0].hand_tiles[-1] = tile
        w = start(s, s.turn(0))
        w = move(s, w, {0: dict(action_type="cut", TileId=tile, cutClass=True)})
        decision = dict(action_type=kind)
    else:
        w = start(s, s.turn(1))
        ask = s.build_pending_action_payload(1)["ask_hand_action_info"]
        assert (tile in ask["kong_candidates"][kind]) == allowed
        assert (tile in kong_tiles(s, 1, kind)) == allowed
        decision = dict(action_type=kind, target_tile=tile)
    if not allowed:
        with pytest.raises(ValueError):
            s.validate_response(1, decision, w["actions"].get(1, []))
        assert s.supplements == [47]
        return
    w = move(s, w, {1: decision})
    if w["status"] == P.ROB_KONG:
        w = move(s, w, {})
    assert w["status"] == P.TURN and w["player"] == 1
    assert p.combination_tiles == [code] and p.hand_tiles == [*sorted(remaining), 47]
    assert legal_cuts(s, 1) == {47}
    move(s, w, {1: dict(action_type="cut", TileId=47, cutClass=True)})
    assert rules.waits(p.hand_tiles, p.combination_tiles) == after
