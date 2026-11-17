"""Loopback acceptance test for four independently compiled Windows players."""
import argparse
import asyncio
import json
import sys
import time
import traceback
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import websockets

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "open_mahjong_server"))
from server.gamestate.game_guangdong.test_doubles import state
from server.gamestate.game_guangdong.test_doubles_match import choose_action, deal_board, deal_co_win_board
from server.gamestate.gamestate_router import handle_gamestate_message
from server.gamestate.public.game_record_manager import build_local_record_detail
from server.database.subrule_fans import collect_subrule_fans

USERS = list(range(11000001, 11000005))
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output-dir", required=True, type=Path)
parser.add_argument("--co-wins", action="store_true", help="Exercise simultaneous wins on boards two and five")
args = parser.parse_args()
out = args.output_dir.resolve()
out.mkdir(parents=True, exist_ok=True)


def save(name, data):
    (out / name).write_text(json.dumps(data, ensure_ascii=False, indent=2, default=str), encoding="utf-8")


async def main():
    gs = state(round_timer=240, step_timer=30)
    gs.init_tiles = lambda: (deal_co_win_board if args.co_wins else deal_board)(gs)
    for p, uid in zip(gs.player_list, USERS):
        p.user_id, p.username, p.remaining_time = uid, f"双打测试{uid - 11000000}", 240
    server = gs.game_server
    server.user_id_to_connection.clear()
    server.gamestate_manager.get_spectator_list = list
    server.players = {str(uid): SimpleNamespace(user_id=uid) for uid in USERS}
    server.gamestate_manager.get_game_state_by_gamestate_id = lambda gid: gs if gid == gs.gamestate_id else None
    ready, inspected, sent = set(), set(), set()
    replayed = asyncio.Event()
    rounds, submissions, errors = [], [], []
    reconnected, reconnect_requested = False, False
    ruling = None

    async def await_condition(predicate, timeout, desc):
        deadline = asyncio.get_running_loop().time() + timeout
        while not predicate():
            if errors:
                raise RuntimeError(errors[0])
            if asyncio.get_running_loop().time() > deadline:
                raise TimeoutError(desc)
            await asyncio.sleep(.04)

    async def broadcast_qa(kind, **fields):
        await asyncio.gather(*(c.websocket.send_qa(kind, **fields) for c in server.user_id_to_connection.values()))

    async def inspect(label):
        token = f"{gs.current_round}-{len(gs.win_events)}-{label}"
        await broadcast_qa("inspect", label=label, token=token, scores=[p.score for p in gs.player_list])
        await await_condition(lambda: all((token, uid) in inspected for uid in USERS), 45, f"inspection {token} pending {inspected}")

    async def plan(uid, info):
        nonlocal reconnect_requested, ruling
        tick = info["action_tick"]
        p = next(p for p in gs.player_list if p.user_id == uid)
        await await_condition(lambda: p.player_index in gs.waiting_players_list or gs.server_action_tick != tick, 15, "action window")
        key = (gs.round_index, tick, uid)
        if gs.server_action_tick != tick or key in sent:
            return
        adapter = server.user_id_to_connection[uid].websocket
        if gs.current_round == 1 and gs.win_events and not reconnect_requested and "cut" in info["action_list"]:
            reconnect_requested = True
            await adapter.send_qa("reconnect")
            return
        sent.add(key)
        if gs.current_round == 4 and ruling is None and "cut" in info["action_list"]:
            original = gs.player_list[3].original_player_index
            ruling = await gs.adjudicate(gs.round_index, original, "false_win")
        move = choose_action(gs, p, info["action_list"], args.co_wins)
        await adapter.send_qa("action", tick=tick, action=move["action_type"], tile=move.get("TileId", 0), drawn=move.get("cutClass", False))
        save("progress.json", dict(round=gs.current_round, status=gs.game_status, tick=tick, seat=p.player_index, planned=move, actions=len(submissions), reconnect=reconnected))

    def schedule(coro):
        async def watch():
            try:
                await coro
            except Exception:
                errors.append(traceback.format_exc())
        asyncio.create_task(watch())

    class Adapter:
        def __init__(self, ws, uid):
            self.ws, self.uid = ws, uid
        async def send_qa(self, kind, **fields):
            await self.ws.send(json.dumps(dict(type="pong", qa="qa/"+kind, client_ts=int(time.time()*1000), **fields), ensure_ascii=False))
        async def send_json(self, payload):
            await self.ws.send(json.dumps(payload, ensure_ascii=False, default=str))
            info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
            if info and info.get("action_list"):
                schedule(plan(self.uid, info))

    async def handle(ws):
        nonlocal reconnected
        uid = int(ws.request.path.strip("/").split("/")[0])
        if uid not in USERS:
            await ws.close(1008)
            return
        adapter = Adapter(ws, uid)
        server.user_id_to_connection[uid] = SimpleNamespace(websocket=adapter)
        try:
            async for raw in ws:
                msg = json.loads(raw)
                kind = msg.get("type", "")
                if kind == "qa/ready":
                    ready.add(uid)
                elif kind == "qa/reconnected":
                    await gs.player_reconnect(uid)
                    reconnected = True
                elif kind == "qa/inspected":
                    label = msg["label"]
                    inspected.add((msg.get("token") or f"{gs.current_round}-{len(gs.win_events)}-{label}", uid))
                elif kind == "qa/replayed":
                    replayed.set()
                elif kind == "ping":
                    await ws.send(json.dumps({**msg, "type":"pong"}))
                elif kind.startswith("gamestate/"):
                    submissions.append(dict(uid=uid, **msg))
                    await handle_gamestate_message(server, str(uid), msg, adapter)
        except websockets.ConnectionClosed:
            pass
        except Exception:
            errors.append(traceback.format_exc())

    announce, settle = gs._announce_win, gs._settle_hand
    async def capture_win(item, multi):
        await announce(item, multi)
        if gs.current_round <= 3 and len(gs.win_events) == 1:
            await inspect("chicken" if item["detail"]["chicken"] else "mid")
    async def capture_board(before):
        result = await settle(before)
        rounds.append(dict(index=gs.round_index, scores=[p.score for p in gs.player_list], reason=gs.draw_reason))
        save("boards.json", rounds)
        await inspect("board-"+str(gs.current_round))
        return result
    async def presentation_ready(fans):
        await asyncio.sleep(.25)
    async def cpu(_state, fn, *args):
        return fn(*args)
    gs._announce_win, gs._settle_hand, gs.run_hu_result_ready_phase = capture_win, capture_board, presentation_ready

    async with websockets.serve(handle, "127.0.0.1", 0, ping_interval=None) as listener:
        (out/"endpoint.txt").write_text("ws://127.0.0.1:"+str(listener.sockets[0].getsockname()[1]), encoding="ascii")
        await await_condition(lambda: len(ready)==4, 180, f"four clients starting, see player logs")
        with patch("server.gamestate.game_guangdong.GuangdongGameState.run_room_bot_cpu",cpu):
            await asyncio.wait_for(gs.game_loop_chinese(), 720)
        assert len(rounds) == 8 and reconnected
        for r in rounds:
            expected = "three_winners" if args.co_wins and r["index"] in (2, 5) else "team_finished"
            assert r["reason"] == expected
        assert ruling and ruling["penalty"] > 0
        fans=[collect_subrule_fans(gs.game_record,i,gs.sub_rule) for i in range(4)]
        assert sum(row.get("鸡和",0) for row in fans)==8
        detail=build_local_record_detail(gs,"gd2v2Qa","2/4").model_dump(mode="json",exclude_none=True)
        save("record-detail.json",detail)
        save("submissions.json",submissions)
        await server.user_id_to_connection[USERS[0]].websocket.send_qa("replay",detail=detail,rounds=rounds)
        await asyncio.wait_for(replayed.wait(),75)
        await broadcast_qa("done")
        await asyncio.sleep(2)
        save("server-result.json",dict(status="completed",boards=rounds,fan_counts=fans,actions=len(submissions),reconnected=reconnected,ruling=ruling))

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception:
        save("server-result.json",dict(status="failed",exception=traceback.format_exc()))
        raise
