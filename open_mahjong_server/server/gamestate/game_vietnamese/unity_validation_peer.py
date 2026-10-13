"""Private native-client regression peer; never uses shared services or a database.

Run with --output DIR, then launch a Development player with
--vietnamese-interaction-audit ws://127.0.0.1:PORT DIR/client.
ready.json contains the ephemeral port. Only this local test connection is accepted.
"""
import argparse
import asyncio
import json
from pathlib import Path
from types import SimpleNamespace

import websockets

from .get_action import handle_action
from .state_machine import Phase as P
from .test_flow import act, state, turn
from ...game_calculation.vietnamese.test_rules import NORMAL
from ...game_calculation.vietnamese.tiles import ANY


async def exercise(ws, report):
    class Socket:
        def __init__(self): self.packets = []
        async def send_json(self, packet): self.packets.append(packet)

    async def exchange(name, s, packets, operation, expected=None, **options):
        await ws.send(json.dumps(dict(name=name, packets=packets, operation=operation, **options), default=str))
        ack, message = None, None
        while ack is None or expected is not None and message is None:
            data = json.loads(await asyncio.wait_for(ws.recv(), 25))
            if 'audit_case' in data:
                assert data['audit_case'] == name and not data['errors'], data
                ack = data
            elif not data.get('type','').startswith('gamestate/vietnamese/'):
                report.setdefault('background_message_types', []).append(data.get('type'))
            else:
                assert message is None, ('duplicate client action', name, data)
                message = data
                action = 'cut' if data.get('type','').endswith('/cut_tile') else data.get('action')
                assert action == expected, (name, expected, data)
        report['cases'].append(dict(name=name, client=ack, request=message))
        if message is None: return None
        assert message['gamestate_id'] == s.gamestate_id
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s),
            players={'native': SimpleNamespace(user_id=s.player_list[0].user_id)})
        reply = Socket()
        await handle_action(server, 'native', message, reply)
        if operation == 'invalid':
            assert reply.packets[0]['type'] == 'tips'
            assert reply.packets[-1]['type'].endswith('/broadcast_hand_action')
            return reply.packets[1:]
        assert not reply.packets, (name, reply.packets)
        assert not s.action_queues[0].empty(), ('request not accepted', name)
        response = s.action_queues[0].get_nowait()
        # The live server normally collects these responses in wait_action.
        # Here only the tested seat is controlled by the native UI.
        responses = {i: dict(action_type='pass') for i,a in s.live_pending_window['actions'].items() if a}
        responses[0] = response
        s.outbound_payloads.clear()
        act(s, responses)
        return response

    def packets(s, fresh=True):
        if fresh:
            s.room_id = 987651
            s.step_time = 30
            s.round_time = 60
            for player in s.player_list: player.remaining_time = 60
            return [s.build_game_start_payload(0), s.build_pending_action_payload(0)]
        return [p for p in s.outbound_payloads if '_pause' in p or p.get('player_index') == 0]

    # Exercise actual auto-flower choice, then incremental replacement messages
    # and a manually rearranged hand using Unity's sibling-index request format.
    s = turn([21,22,23,31,32,33,41,41,41,45,45,51,52,ANY])
    s.tiles_list[-2:] = [19,11]
    await exchange('auto-flower-keeps-joker', s, packets(s), 'wait', 'buhua', auto_flower=True)
    assert ANY in s.player_list[0].hand_tiles and s.player_list[0].huapai_list == [51,52]
    p = s.player_list[0]
    await exchange('sorted-hand-after-flowers', s, packets(s, False), 'hand', 'cut',
        hand=list(p.hand_tiles), buttons=['vn_replace'], tile=11)
    assert p.discard_tiles == [11] and ANY in p.hand_tiles

    s = turn(list(NORMAL[:-1]) + [51])
    s.tiles_list[-1] = 29
    await exchange('replace-drawn-flower', s, packets(s), 'wait', 'buhua', auto_flower=True)
    response = await exchange('draw-slot-after-replacement', s, packets(s, False), 'draw', 'cut', hand=list(s.player_list[0].hand_tiles))
    assert response['cutClass'] is True and response['cutIndex'] >= 0

    s = state();s.initialize_round()
    s.player_list[0].hand_tiles = list(NORMAL[:-1]) + [ANY]
    s.opening_queue = [0];s.open_action_window(s.opening_window())
    await exchange('opening-keep-joker', s, packets(s), 'pass', 'pass', buttons=['vn_replace','pass'])
    assert ANY in s.player_list[0].hand_tiles and not s.player_list[0].huapai_list

    s = turn(list(NORMAL[:-1]) + [ANY])
    await exchange('explicit-replace-joker', s, packets(s), 'choice', 'vn_replace', word='vn_replace', buttons=['vn_replace'])
    assert ANY in s.player_list[0].huapai_list and ANY not in s.player_list[0].hand_tiles

    s = turn(list(NORMAL[:-1]) + [ANY])
    recovery = await exchange('reject-invalid-choice', s, packets(s), 'invalid', 'vn_replace')
    await exchange('rejected-choice-restores-buttons', s, recovery, 'check', buttons=['vn_replace'])

    def claim_state(no_win=False):
        s = turn(NORMAL)
        s.player_list[3].hand_tiles = list(NORMAL)
        s.player_list[3].has_draw_slot = True
        s.player_list[3].last_drawn_tile = 25
        s.player_list[0].hand_tiles = [23,24,25,25,25,26,27,11,12,13,41,41,ANY]
        if no_win:
            s.player_list[0].hand_tiles[-1] = 19
        s.player_list[0].has_draw_slot = False
        s.current_player_index = 3
        s.open_action_window(s.discard(3, dict(TileId=25, cutClass=True)))
        return s
    s = claim_state()
    await exchange('separate-chi-peng-gang', s, packets(s), 'choice', 'vn_claim',
        word='vn_peng', buttons=['vn_chi','vn_peng','vn_gang','pass'])
    assert s.player_list[0].combination_tiles == ['k25']
    s = claim_state(no_win=True)
    assert 'hu' not in s.live_pending_window['actions'][0]
    await exchange('platform-auto-pass-meld-filters', s, packets(s), 'wait', 'pass', auto_pass=True)
    assert not s.player_list[0].combination_tiles

    await ws.send(json.dumps(dict(done=True)))


async def main(output):
    output.mkdir(parents=True, exist_ok=True)
    finished = asyncio.Event()
    report = dict(passed=False, cases=[], errors=[])
    async def connected(ws, *_):
        try:
            await exercise(ws, report)
            report['passed'] = True
        except Exception as exc:
            import traceback
            report['errors'].append(traceback.format_exc())
        finally:
            (output/'peer.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
            finished.set()
    async with websockets.serve(connected,'127.0.0.1',0) as server:
        port = server.sockets[0].getsockname()[1]
        (output/'ready.json').write_text(json.dumps(dict(port=port)), encoding='utf-8')
        await asyncio.wait_for(finished.wait(),300)
    return 0 if report['passed'] else 2


if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True)
    args=parser.parse_args()
    raise SystemExit(asyncio.run(main(Path(args.output))))
