"""Replay fixtures originate in the authoritative FSM, including its physical wall.

The exported checkpoints are consumed by the real Unity replay verifier. They
contain expected server state, not a second implementation of mahjong scoring.
"""
import json
from copy import deepcopy

import pytest

from .test_flow import make_state, open_round, assert_conservation
from .test_branches import table, opening_flow, act
from .bot import choose_action
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import PROFILES, ORPHANS


class ReplayAudit:
    def __init__(self,name,state,window):
        self.name,self.state,self.window=name,state,window
        self.checkpoints=[]
        self.capture()

    def capture(self):
        state=self.state
        assert_conservation(state)
        players=[]
        for p in state.player_list:
            hand=list(p.hand_tiles)
            wins=[w for w in state.deferred_hu_settlements if w['winner']==p.player_index]
            if wins and wins[0]['source']!='self_draw' and not wins[0].get('flower_win'):
                hand.append(wins[0]['tile'])
            score=p.score
            if not state.deferred_scores_applied:
                score+=sum(w['score_changes'][p.player_index] for w in state.deferred_hu_settlements)
            players.append(dict(index=p.player_index,score=score,hand=sorted(hand),
                river=list(p.discard_tiles),flowers=list(p.huapai_list),melds=list(p.combination_tiles),
                masks=deepcopy(p.combination_mask),horizontal=list(p.discard_riichi_flags),ready=p.declared_ready,hu=p.is_hu))
        ticks=state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks']
        row=dict(node=len(ticks),players=players,pulls=deepcopy(state.ledger.snapshot()))
        if not self.checkpoints or self.checkpoints[-1]!=row:
            self.checkpoints.append(row)

    def apply(self,**choices):
        self.window=act(self.state,self.window,**choices)
        self.capture()

    def finish(self):
        state=self.state
        for _ in range(1000):
            if state.machine.phase==P.END: break
            decisions={i:choose_action(state,i,a) for i,a in self.window['actions'].items() if a}
            self.window=state.apply_action_results(self.window,decisions)
            self.capture()
        else: raise AssertionError('Replay fixture failed to terminate')
        state.apply_deferred_score_changes()
        state.machine.transition(P.READY)
        state.finalize_round_recording()
        self.capture()
        return dict(name=self.name,record=deepcopy(state.game_record),checkpoints=self.checkpoints,
                    players=[dict(user_id=p.user_id,username=p.username,score=p.score,
                                  rank=i+1,original_player_index=p.original_player_index)
                             for i,p in enumerate(state.player_list)])


def replay_fixtures():
    result=[]
    for profile in PROFILES:
        for seed in (1,241):
            state=make_state(profile,flowers=profile==PROFILES[0],seed=seed)
            result.append(ReplayAudit(profile.split('/')[-1]+f'-seed{seed}',state,open_round(state)).finish())

    for profile in PROFILES:
        hand=[11]*4+[21,22,23,31,32,33,41,41,45,45]
        if profile==PROFILES[2]: hand+=[24,25,26]
        state,window=table(profile,hands={0:hand},tail=47)
        state.record_opening_complete()
        audit=ReplayAudit(profile.split('/')[-1]+'-concealed-kong',state,window)
        audit.apply(**{'0':dict(action_type='angang',target_tile=11)})
        audit.apply()
        result.append(audit.finish())

    state,window=table(hands={0:[47]*4+[12,13,14,21,22,23,31,32,33,45],
                              1:sorted(ORPHANS-{47})+[11]})
    state.record_opening_complete()
    audit=ReplayAudit('qingzhang-rob-concealed',state,window)
    audit.apply(**{'0':dict(action_type='angang',target_tile=47)})
    audit.apply(**{'1':'hu'})
    result.append(audit.finish())

    # Establish the original pung through a genuine discard/claim, then draw the
    # fourth copy after a full table cycle. This fixture needs no invented meld header.
    state,window=table(PROFILES[1],hands={
        0:[17,18,19,27,28,29,37,38,39,42,42,43,43,15],
        1:[15,15,12,13,14,21,22,23,31,32,33,41,44],
        2:[13,14,21,22,23,31,32,33,46,46,46,41,41],
        3:[11,11,11,12,12,16,16,16,24,25,26,47,47]})
    for tile in [44,45,46,15]: state.tiles_list.remove(tile)
    state.tiles_list[:0]=[44,45,46,15]
    # Update the recorded untouched wall before the first action.
    state.game_record['game_round']['round_index_1']['tiles_list']=list(state.tiles_list)
    state.record_opening_complete()
    audit=ReplayAudit('new13-rob-added',state,window)
    audit.apply(**{'0':dict(action_type='cut',TileId=15)})
    audit.apply(**{'1':'peng'})
    audit.apply(**{'1':dict(action_type='cut',TileId=44)})
    audit.apply()
    for seat,tile in [(2,44),(3,45),(0,46)]:
        audit.apply(**{str(seat):dict(action_type='cut',TileId=tile)})
        audit.apply()
    audit.apply(**{'1':dict(action_type='jiagang',target_tile=15)})
    audit.apply(**{'2':'hu'})
    result.append(audit.finish())

    for profile in PROFILES[1:]:
        hand=[11,12,13,21,22,23,31,32,33,45,45,45,41,41]
        if profile==PROFILES[2]: hand += [24,25,26]
        state,window=table(profile,hands={0:hand})
        state.record_opening_complete()
        audit=ReplayAudit(profile.split('/')[-1]+'-ready',state,window)
        audit.apply(**{'0':'riichi'})
        audit.apply(**{'0':dict(action_type='cut',TileId=41)})
        result.append(audit.finish())

    for profile in (PROFILES[0],PROFILES[2]):
        for count in (7,8):
            for decline in (False,True):
                n=14 if profile==PROFILES[0] else 16
                initial={0:list(range(51,51+count))+[11,12,13,21,22,23,31,32,33][:n-count]}
                if count==7 and profile==PROFILES[2]:
                    initial[1]=[58,14,15,16,24,25,26,34,35,36,41,41,42,42,43,43]
                state,window=opening_flow(profile,initial,tail=[45,44,43,42,41,39,38])
                audit=ReplayAudit(f'{profile.split("/")[-1]}-flowers{count}-{"pass" if decline else "win"}',state,window)
                audit.apply(**{'0':'pass' if decline else 'hu' if profile==PROFILES[2] and count==7 else 'hu_self'})
                result.append(audit.finish())
    return result


@pytest.fixture(scope='module')
def fixtures():
    return replay_fixtures()


def test_replay_retains_ready_marker_without_riichi_deposit(fixtures):
    for fixture in fixtures:
        ticks=fixture['record']['game_round']['round_index_1']['action_ticks']
        assert not any(t[0]=='riichi' for t in ticks)
        if fixture['name'].endswith('-ready'):
            assert any(t[:2]==['hongkong','ready'] for t in ticks)
            assert any(t[0]=='c' and len(t)>3 and t[3]=='H' for t in ticks)


def test_replay_scores_reconcile_without_counting_absolute_checkpoint_twice(fixtures):
    for fixture in fixtures:
        rd=fixture['record']['game_round']['round_index_1']
        score=list(rd['hongkong']['start_scores'])
        for tick in rd['action_ticks']:
            delta=tick[4] if tick[0].startswith('hu_') else tick[2] if tick[:2]==['hongkong','score'] else [0]*4
            score=[a+b for a,b in zip(score,delta)]
            if tick[:2]==['hongkong','state']:
                assert score==tick[3],fixture['name']
        assert score==[p['score'] for p in fixture['checkpoints'][-1]['players']]


def test_robbed_kong_records_source_without_committing_the_meld(fixtures):
    for fixture in fixtures:
        if '-rob-' not in fixture['name']: continue
        ticks=fixture['record']['game_round']['round_index_1']['action_ticks']
        assert not any(t[0] in ('ag','jg') for t in ticks)
        sources=[t for t in ticks if t[:2]==['hongkong','win_source']]
        assert len(sources)==1 and sources[0][4]=='rob_kong' and sources[0][7]


if __name__=='__main__':
    import sys
    from pathlib import Path
    fixtures=replay_fixtures()
    Path(sys.argv[1]).write_text(json.dumps(fixtures,ensure_ascii=False,default=str),encoding='utf-8')
    print(f'Exported {len(fixtures)} authoritative FSM replay fixtures')
