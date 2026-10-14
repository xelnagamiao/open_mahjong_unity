"""Eight client action streams exercise both real table state machines."""
import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_duplicate import run_async
from .DuplicateGameState import SbrDuplicateGameState, XueliuDuplicateGameState
from .test_action_window import submit
from .xueliu_bot import choose_opening, choose_hand, choose_claim
from ..public.ai.smart_bot_logic import count_visible_tiles
from ..public.hand_slot_utils import infer_bot_cut_class, has_draw_slot
from ..public.sichuan_duplicate import PairedMatch, new_match
from ..verifier.host import build_db_manager, build_game_server, build_room_data, CaptureSocket, install_lab_runtime, restore_lab_runtime, real_sleep
from ...game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, make_board, team_by_seat


@pytest.mark.parametrize("sub,cls", [(SBR_DUPLICATE,SbrDuplicateGameState),(XUELIU_DUPLICATE,XueliuDuplicateGameState)])
@pytest.mark.parametrize("seed", [1807, 2409])
@run_async
async def test_eight_clients_complete_paired_game(sub, cls, seed):
    messages = []
    db = build_db_manager()
    db.store_sichuan_game_record.return_value = None
    server = build_game_server(db, messages=messages)
    data = new_match("test-pair",sub,{"open":"991001","closed":"991002"},[make_board(seed,1)],str(seed))
    data["status"] = "running"
    pair = PairedMatch(data, lambda *_: None)
    server.gamestate_manager.sichuan_duplicate_matches = {"test-pair":pair}
    states = []
    for table in ("open","closed"):
        room = build_room_data(seed=0, room_id=data["room_ids"][table])
        ids = list(range(101,105)) if table == "open" else list(range(105,109))
        room.update(room_rule="sichuan",sub_rule=sub,room_type="events",player_list=ids,
                    player_settings={uid:{"username":f"P{uid}"} for uid in ids},
                    team_by_seat=team_by_seat(table),duplicate_match_id="test-pair",
                    duplicate_table=table,duplicate_key="test-duplicate-key",duplicate_game_id=table+"game-id")
        state = cls(server,room,server.calculation_service,db,table)
        state._ready_phase = AsyncMock()
        states.append(state)
        server.gamestate_manager.gamestate_id_to_game_state[table] = state
        for uid in ids:
            conn = SimpleNamespace(user_id=uid,username=f"P{uid}",websocket=CaptureSocket(uid,messages),current_room_id=room["room_id"],Connect_id=f"conn-{uid}")
            server.players[f"conn-{uid}"] = conn
            server.user_id_to_connection[uid] = conn
            server.gamestate_manager.user_id_to_game_state[uid] = state
    install_lab_runtime(turbo=True)
    tasks = [asyncio.create_task(s.game_loop_sichuan()) for s in states]
    seen = set()
    async def drive():
        while not all(t.done() for t in tasks):
            for s in states:
                for seat in list(s.waiting_players_list):
                    actions = list(s.action_dict.get(seat,[]))
                    key = (s.gamestate_id,s.current_round,s.server_action_tick,s.game_status,seat,tuple(actions))
                    if not actions or key in seen:
                        continue
                    seen.add(key)
                    p = s.player_list[seat]
                    fields = {}
                    action = next((a for a in actions if a.startswith('hu')),None)
                    if action is None:
                        if 'dingque' in actions:
                            action = 'dingque'; fields['target_tile'] = min((1,2,3), key=lambda suit: sum(t//10==suit for t in p.hand_tiles))
                        elif 'xueliu_exchange_three' in actions:
                            action = 'xueliu_exchange_three'; fields['selected_tiles'] = list(choose_opening(p.hand_tiles,True))
                        elif 'ready' in actions:
                            action = 'ready'
                        elif 'cut' in actions:
                            action,tile,index = choose_hand(p.hand_tiles,p.combination_tiles,count_visible_tiles(s),actions,4,p.dingque_suit,p.post_hu_lock)
                            if action == 'cut':
                                if s.duplicate_table == 'closed' and not p.post_hu_lock:
                                    index = (s.server_action_tick + seat) % len(p.hand_tiles)
                                    tile = p.hand_tiles[index]
                                fields = dict(TileId=tile,cutClass=infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)))
                            else:
                                fields['target_tile'] = tile
                        elif s.game_status == 'waiting_action_after_cut':
                            tile = s.player_list[s.current_player_index].discard_tiles[-1]
                            action = choose_claim(p.hand_tiles,p.combination_tiles,count_visible_tiles(s),actions,tile,4,p.dingque_suit)
                        else:
                            action = 'pass'
                    await submit(s,seat,action,**fields)
            await real_sleep(0)
        await asyncio.gather(*tasks)
    try:
        await asyncio.wait_for(drive(), 45)
        assert pair.completed, [(s.game_status,s.current_round) for s in states]
        assert all(s.team_result['scores'] == dict(enumerate(pair.data['scores'])) for s in states)
        assert pair.data['completed_boards'] >= 1
        for s in states:
            # Reconstruct exactly the same final record using only the durable
            # last-board checkpoint, as after a crash before either save hook.
            from .DuplicateGameState import DuplicateMixin
            snapshot=json.loads(json.dumps(pair.data,default=str))
            checkpoint=snapshot['checkpoints'][s.duplicate_table]
            recovered=SimpleNamespace(duplicate_match=PairedMatch(snapshot,lambda *_:None),duplicate_table=s.duplicate_table,
                player_list=[SimpleNamespace(**p,record_counter=SimpleNamespace(rank_result=0)) for p in checkpoint['players']])
            DuplicateMixin._finish_team_match(recovered)
            assert json.loads(json.dumps(recovered.game_record,default=str))==json.loads(json.dumps(s.game_record,default=str))
            assert s.game_record['game_title']['duplicate_profile']['sub_rule'] == sub
            assert all(tick[2] == 'dw' for r in s.game_record['game_round'].values() for tick in r['action_ticks'] if tick[0] in ('d','gd'))
            from ..verifier.record_sim.goto_action import RecordSim
            replay = RecordSim(s.game_record)
            for key in s.game_record['game_round']:
                frames = replay.apply_all(key)
                for at in (0, len(frames)//2, len(frames)-1):
                    assert replay.goto_action(key, at) == frames[at]
        live = [m['message'] for m in messages if m.get('type','').endswith('game_start')]
        assert live
        assert all('duplicate_key' not in str(m) and 'duplicate_boards' not in str(m) for m in live)
        ends = [m['message']['game_end_info'] for m in messages if m.get('type','').endswith('game_end')]
        assert len(ends) == 8, 'all eight clients must receive the serialized final result'
        assert all('master_seed' not in e and 'salt' not in e and 'record_detail' not in e for e in ends)
        assert all(e['duplicate_match']['status'] == 'completed' for e in ends)
        for end in ends:
            for key in ('small_scores', 'imp_scores'):
                assert end['duplicate_match'][key] == pair.data[key]
            for player in end['player_final_data'].values():
                team = player['team_id']
                assert player['team_small_score'] == pair.data['small_scores'][team]
                assert player['team_imp_score'] == pair.data['imp_scores'][team]
    finally:
        for t in tasks:
            if not t.done(): t.cancel()
        await asyncio.gather(*tasks,return_exceptions=True)
        for s in states:
            await s.cleanup_game_state()
        restore_lab_runtime()
