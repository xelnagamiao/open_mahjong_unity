"""Recorded actions must reconstruct the authoritative state, not just parse."""
import json
from copy import deepcopy
from pathlib import Path
import pytest
from .test_flow import state, act
from .state_machine import Phase as P
from ..verifier.record_sim.goto_action import RecordSim


def expected(s):
    return dict(wall=list(s.tiles_list),players=[dict(
        hand=list(p.hand_tiles)+list(p.won_tiles), flowers=list(p.huapai_list),
        melds=list(p.combination_tiles), masks=deepcopy(p.combination_mask),
        river=list(p.discard_tiles), draw_slot=p.has_draw_slot,
        score=p.score+(0 if s.deferred_scores_applied else s.round_changes[i]))
        for i,p in enumerate(s.player_list)])


def fixture(seed=0, hands=1):
    s=state(70001+seed);s.start_game_recording();frames={};initial={}
    for hand in range(hands):
        s.initialize_round();s.start_round_recording()
        key=f'round_index_{s.round_index}';frames[key]=[];initial[key]=expected(s)
        s.open_action_window(s.opening_window())
        for step in range(1000):
            if s.machine.phase==P.END:break
            act(s)
            frames[key].append(dict(node=len(s.game_record['game_round'][key]['action_ticks']),**expected(s)))
        assert s.machine.phase==P.END
        s.finalize_round_recording()
        frames[key].append(dict(node=len(s.game_record['game_round'][key]['action_ticks']),**expected(s)))
        if s.match_finishing:break
        s.machine.transition(P.READY);s.advance_round_after_ready()
    s.finalize_game_recording()
    return dict(record=s.game_record,frames=frames,initial=initial)


def check_frame(sim,frame):
    assert sim.current_tiles_list==frame['wall']
    for i,expected in enumerate(frame['players']):
        actual=sim.players[i]
        assert sorted(actual.tile_list)==sorted(expected['hand']), (i,actual.tile_list,expected['hand'])
        assert actual.combination_tiles==expected['melds']
        assert sorted(actual.huapai_list)==sorted(expected['flowers'])
        assert actual.discard_tiles==expected['river']
        assert actual.score==expected['score']
        # Physical tiles are invariant; layout signs are viewer-relative.
        assert [sorted(m[1::2]) for m in actual.combination_masks]==[sorted(m[1::2]) for m in expected['masks']]


@pytest.mark.parametrize('seed',range(5))
def test_server_record_roundtrip_and_random_seek(seed):
    data=fixture(seed,4)
    sim=RecordSim(data['record'])
    for key,frames in data['frames'].items():
        all_frames=sim.apply_all(key)
        for frame in frames:
            got=sim.goto_action(key,frame['node'])
            assert got==all_frames[frame['node']]
            check_frame(sim,frame)


def export(destination):
    destination=Path(destination);destination.mkdir(parents=True,exist_ok=True)
    for seed in range(3):
        data=fixture(seed,4)
        (destination/f'vietnamese-{seed}.json').write_text(json.dumps(data,ensure_ascii=False,default=str),encoding='utf-8')
    return data
