import asyncio
from functools import wraps
from copy import deepcopy
from types import SimpleNamespace

import pytest

from ...game_calculation.sichuan.duplicate_rules import (
    SBR_DUPLICATE, XUELIU_DUPLICATE, TILES, imp, make_board, rule_profile,
    score_board, team_by_seat, validate_board,
)
from ..public.sichuan_duplicate import PairedMatch, new_match
from .duplicate_wall import SichuanDuplicateWall, can_draw, wall_exhausted, init_duplicate_board
from .SichuanGameState import SichuanPlayer


def run_async(fn):
    @wraps(fn)
    def run(*args, **kwargs):
        return asyncio.run(fn(*args, **kwargs))
    return run


@pytest.mark.parametrize("n,expected", [(0,0),(1,1),(2,1),(3,2),(5,2),(6,3),(8,3),(9,4),(11,4),(12,5),(15,5),(16,6),(19,6),(20,7),(25,7),(26,8),(31,8),(32,9),(39,9),(40,10),(10000,10)])
def test_imp_boundaries(n, expected):
    assert imp(n) == expected


@pytest.mark.parametrize("bad", [-1, 1.5, True, None, "2"])
def test_imp_rejects_invalid(bad):
    with pytest.raises(ValueError):
        imp(bad)


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("number", range(1, 9))
def test_boards_and_profiles(sub, number):
    b = make_board("shared", number)
    validate_board(b)
    assert b == make_board("shared", number)
    assert b != make_board("other", number)
    assert b["dealer"] == (number - 1) % 4
    assert rule_profile(sub)["supplement_draw"] == "front"
    assert team_by_seat("open") == [0,1,0,1]
    assert team_by_seat("closed") == [1,0,1,0]


@pytest.mark.parametrize("key,value", [("number",0),("dealer",True),("hands",[]),("walls",[[11]*14]*4)])
def test_invalid_boards(key, value):
    b = make_board(2, 1)
    b[key] = value
    with pytest.raises(ValueError):
        validate_board(b)


def state(bloodflow=False):
    players = [SichuanPlayer(i, str(i), [], 0) for i in range(4)]
    for i, p in enumerate(players):
        p.original_player_index = p.player_index = i
    s = SimpleNamespace(player_list=players, is_xueliu=bloodflow, current_player_index=0, dead_wall_count=0)
    init_duplicate_board(s, make_board(3, 1))
    return s


@pytest.mark.parametrize("bloodflow", [False, True])
@pytest.mark.parametrize("seat", range(4))
def test_independent_draws_and_standby(bloodflow, seat):
    s = state(bloodflow)
    expected = list(s.tiles_list.parts[seat])
    others = deepcopy(s.tiles_list.parts)
    for tile in expected:
        assert s.tiles_list.pop_for_player(s.player_list[seat]) == tile
    assert not can_draw(s, seat)
    assert not wall_exhausted(s)
    with pytest.raises(ValueError):
        s.tiles_list.pop_for_player(s.player_list[seat])
    assert all(s.tiles_list.parts[i] == others[i] for i in range(4) if i != seat)
    assert len(s.tiles_list) == sum(s.tiles_list.remaining_counts())


@pytest.mark.parametrize("seat", range(4))
def test_sbr_retired_partner_reactivates_and_tracks_source(seat):
    s = state()
    p, mate = s.player_list[seat], s.player_list[(seat+2)%4]
    while can_draw(s, seat):
        s.tiles_list.pop_for_player(p)
    mate.is_hu = True
    assert can_draw(s, seat)
    assert not can_draw(s, mate.player_index)
    expected = s.tiles_list.parts[mate.player_index][0]
    assert s.tiles_list.pop_for_player(p) == expected
    assert s.duplicate_last_draw["draw_from_seat"] == mate.original_player_index


def test_sbr_own_wall_has_priority():
    s = state()
    s.player_list[2].is_hu = True
    s.tiles_list.pop_for_player(s.player_list[0])
    assert s.duplicate_last_draw["draw_from_seat"] == 0


def test_xueliu_never_inherits_partner_wall():
    s = state(True)
    while can_draw(s, 0):
        s.tiles_list.pop_for_player(s.player_list[0])
    s.player_list[2].has_won = True
    assert not can_draw(s, 0)
    assert can_draw(s, 2)


def test_inaccessible_retired_walls_do_not_prevent_end():
    s = state()
    for p in s.player_list:
        p.is_hu = True
    assert len(s.tiles_list) > 0 and wall_exhausted(s)


@pytest.mark.parametrize("mode,expected", [("imp",[3,0]),("raw_difference",[8,0])])
def test_no_double_subtraction(mode, expected):
    result = score_board([18,-18],[-10,10], scoring=mode)
    assert result == {"raw_difference":[8,-8],"scores":expected}


def pair(sub=SBR_DUPLICATE, count=1):
    data = new_match("id", sub, {"open":"1","closed":"2"}, [make_board("seed", i+1) for i in range(count)], "seed")
    data["status"] = "running"
    saved = []
    return PairedMatch(data, lambda candidate, revision: saved.append((deepcopy(candidate), revision))), saved


@pytest.mark.parametrize('table,number,scores,adjustments',[
    ('other',1,[1,-1],[0,0]),('open',True,[1,-1],[0,0]),('open',0,[1,-1],[0,0]),
    ('open',1,[True,-1],[0,0]),('open',1,[1,-1],[0]),('open',1,[1,-1],[0,True]),
    ('open',1,None,[0,0]),('open',1,[1,1],[0,0]),('open',2,[1,-1],[0,0]),
])
@run_async
async def test_invalid_submission_never_mutates_pair(table,number,scores,adjustments):
    p,saved=pair();before=deepcopy(p.data)
    with pytest.raises(ValueError): await p.submit(table,number,scores,adjustments,{})
    assert p.data==before and saved==[]


def test_checkpoint_restores_personal_history_without_aliasing():
    from .DuplicateGameState import DuplicateMixin
    p,_=pair(count=4)
    checkpoint={'game_record':{'game_round':{}},'team_round_history':[{'score_changes':[4,-4]}],
                'scores':[1,-2,3,-2],'score_history':[['+1'],['-2'],['+3'],['-2']],
                'round_number_history':[[1],[1],[1],[1]]}
    p.data.update(completed_boards=1,checkpoints={'open':checkpoint})
    s=SimpleNamespace(duplicate_match=p,duplicate_table='open',player_list=state().player_list)
    DuplicateMixin._restore_match_checkpoint(s)
    assert [v.score for v in s.player_list]==checkpoint['scores']
    assert [v.score_history for v in s.player_list]==checkpoint['score_history']
    assert s.current_round==s.round_index==2 and s.dealer_index==1
    s.player_list[0].score_history.append('9')
    assert checkpoint['score_history'][0]==['+1']


def test_rule_validation_rejects_unknown_profile_table_and_scoring():
    for action in (lambda:rule_profile('invalid'),lambda:team_by_seat('bad'),
                   lambda:score_board([0,0],[0,0],scoring='bad'),
                   lambda:score_board([1,1],[0,0],scoring='imp')):
        with pytest.raises(ValueError): action()
    s=state()
    with pytest.raises(ValueError):s.tiles_list.pop(-1)
    before=len(s.tiles_list);s.tiles_list.pop();assert len(s.tiles_list)==before-1
    ordinary=SimpleNamespace(tiles_list=[11],dead_wall_count=0)
    assert can_draw(ordinary,0) and not wall_exhausted(ordinary)
    ordinary.tiles_list.clear();assert not can_draw(ordinary,0) and wall_exhausted(ordinary)


@run_async
async def test_pending_conflict_and_failed_interrupt_unblock_and_cancel():
    p,_=pair(count=4)
    with pytest.raises(ValueError):p.board(0)
    assert p.board(1)==p.data['boards'][0]
    pending=asyncio.create_task(p.submit('open',1,[2,-2],[0,0],{}));await asyncio.sleep(0)
    with pytest.raises(ValueError):await p.submit('open',1,[3,-3],[0,0],{})
    sleeper=asyncio.create_task(asyncio.Event().wait())
    p.states={'open':SimpleNamespace(game_task=sleeper)}
    revision=p.data['revision']
    def failure(*_):raise OSError('DB offline')
    p.persist=failure
    with pytest.raises(OSError):p.interrupt('failure')
    assert p.data['status']=='interrupted' and p.data['revision']==revision
    with pytest.raises(RuntimeError):await pending
    with pytest.raises(asyncio.CancelledError):await sleeper
    p.interrupt('again')
    with pytest.raises(ValueError):pair()[0].restart_unfinished_board()


@run_async
async def test_engine_start_gate_stays_closed_and_propagates_failure():
    from .DuplicateGameState import DuplicateMixin
    from unittest.mock import AsyncMock
    p,_=pair();p.data['status']='interrupted';p.start_gate.set()
    s=SimpleNamespace(duplicate_match=p,game_loop_sichuan=AsyncMock(side_effect=ValueError('loop failed')))
    await DuplicateMixin.run_game_loop(s);s.game_loop_sichuan.assert_not_awaited()
    p.data['status']='running'
    with pytest.raises(ValueError):await DuplicateMixin.run_game_loop(s)
    assert p.data['status']=='interrupted'
    s.duplicate_table='open';s.player_list=state().player_list
    for player in s.player_list:player.team_id=0
    with pytest.raises(ValueError):DuplicateMixin._shuffle_players(s,None)
    p.data['completed_boards']=1
    with pytest.raises(ValueError):DuplicateMixin._restore_match_checkpoint(s)


@run_async
@pytest.mark.parametrize("first", ["open", "closed"])
@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
async def test_barrier_privacy_and_idempotency(first, sub):
    p, saved = pair(sub)
    second = "closed" if first == "open" else "open"
    task = asyncio.create_task(p.submit(first,1,[20,-20],[0,0],{"test":1}))
    await asyncio.sleep(0)
    assert not task.done()
    assert "scores" not in p.public_info() and "boards" not in p.public_info()
    await p.submit(second,1,[-8,8],[0,0],{"test":2})
    await task
    assert p.completed
    assert p.data["scores"] == ([5,0] if sub == SBR_DUPLICATE else [12,0])
    revisions = len(saved)
    await p.submit(first,1,[20,-20],[0,0],{"test":1})
    assert len(saved) == revisions
    with pytest.raises(ValueError, match="冲突"):
        await p.submit(first,1,[21,-21],[0,0],{"test":1})


@run_async
async def test_xueliu_tie_adds_same_board_for_both_tables():
    p, _ = pair(XUELIU_DUPLICATE)
    await asyncio.gather(p.submit("open",1,[20,-20],[0,0],{}), p.submit("closed",1,[-20,20],[0,0],{}))
    assert not p.completed and len(p.data["boards"]) == 2
    assert p.board(2) == make_board("seed",2)
    await asyncio.gather(p.submit("open",2,[2,-2],[0,0],{}), p.submit("closed",2,[0,0],[0,0],{}))
    assert p.completed and p.data["scores"] == [2,0]


@run_async
async def test_sbr_tie_is_draw_and_penalty_is_pair_adjustment():
    p, _ = pair()
    await asyncio.gather(p.submit("open",1,[-12,0],[0,12],{}), p.submit("closed",1,[12,-12],[0,0],{}))
    assert p.completed and p.data["scores"] == [0,0]
    assert p.data["results"][0]["tables"]["open"]["adjustments"] == [0,12]


@run_async
async def test_imp_converts_each_board_before_sum():
    p, _ = pair(count=2)
    for n in (1,2):
        await asyncio.gather(p.submit("open",n,[3,-3],[0,0],{}), p.submit("closed",n,[0,0],[0,0],{}))
    assert p.data["scores"] == [4,0]  # IMP(3)+IMP(3), not IMP(6)
    assert sum(imp(x) for x in [3,3]) == 4 and imp(6) == 3


@run_async
async def test_failed_persist_does_not_publish_or_release():
    p, _ = pair()
    def fail(*_):
        raise OSError("database unavailable")
    p.persist = fail
    with pytest.raises(OSError):
        await p.submit("open",1,[1,-1],[0,0],{})
    assert p.data["pending"] == {} and p.data["revision"] == 0


@run_async
async def test_interruption_releases_waiter_without_zero_result():
    p, _ = pair()
    task = asyncio.create_task(p.submit("open",1,[1,-1],[0,0],{}))
    await asyncio.sleep(0)
    p.interrupt("test")
    with pytest.raises(RuntimeError):
        await task
    assert not p.completed and not p.data["results"]
