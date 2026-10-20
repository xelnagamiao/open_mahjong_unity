import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .smart_bot_ai import smart_bot_action
from .test_bot_modes import table


async def inline_cpu(state, fn, *args, **kwargs):
    return fn(*args, **kwargs)


def test_shenyang_normal_cut_uses_efficiency_and_queue_protocol(monkeypatch):
    from ...game_shenyang import bot
    from ...game_shenyang.test_state import make_state, hand
    async def run():
        s = make_state()
        p = hand(s,0,[11,12,13,21,22,23,31,32,34,45,45,47,17,18],drawn=18)
        p.user_id = 2
        s.game_status = "waiting_hand_action"
        s.action_dict = {0:["cut"]}
        s.waiting_players_list = [0]
        await bot.shenyang_bot_action(s,0,["cut"],s.game_status)
        data = s.action_queues[0].get_nowait()
        assert data["TileId"] == 47 and not data["cutClass"]
        assert data["_action_tick"] == s.server_action_tick
    monkeypatch.setattr(bot,"run_room_bot_cpu",inline_cpu)
    asyncio.run(run())


def test_shenyang_claim_can_advance_to_a_legal_wait():
    from ...game_shenyang.bot import choose_claim
    hand = [11,11,21,22,23,31,32,33,45,45,47,17,18]
    assert choose_claim(hand,[],[0]*34,["peng","pass"],11) == "peng"


def test_dingque_restricts_discard_choices_but_preserves_full_hand_evaluation():
    from .smart_bot_ai import _choose_hand_action
    hand = [12,12,13,13,15,17,17,21,22,24,28,31,34,36]
    assert _choose_hand_action(hand, [], 0, [0]*34, 1, ["cut"]) == ("cut", 15)


def test_jiujiang_ready_discard_avoids_an_exhausted_wait(monkeypatch):
    from ...game_jiujiang import bot
    from ...game_jiujiang.test_state import make_state, drawn
    async def run():
        s = make_state()
        s.bot_speed = "instant"
        p = s.player_list[0]
        p.user_id = 2
        p.discard_count = 0
        p.first_discard_done = False
        drawn(p, [11,12,13,21,22,23,31,32,33,17,18,19,25,27])
        s.player_list[1].discard_tiles = [27,27,27]
        s.game_status = "waiting_hand_action"
        s.action_dict = s.check_hand_actions(0)
        s.waiting_players_list = [0]
        assert p.riichi_candidate_cuts == {25: [27], 27: [25]}
        await bot.jiujiang_bot_action(s, 0, s.action_dict[0], s.game_status)
        data = s.action_queues[0].get_nowait()
        assert (data["action_type"], data["TileId"]) == ("riichi_cut", 27)
    monkeypatch.setattr(bot, "run_room_bot_cpu", inline_cpu)
    asyncio.run(run())


@pytest.mark.parametrize("uid,expected", [(0,18),(2,39)])
def test_exchange_six_modes_choose_different_discards_through_stage(uid,expected,monkeypatch):
    from ...game_changsha.test_exchange_six import make_state
    from . import bot_executor
    async def run():
        s = make_state(bot_seats=(0,))
        p = s.player_list[0]
        p.user_id = uid
        p.hand_tiles = [11,12,13,21,22,23,31,32,34,25,25,39,17,18]
        result = await s.exchange_six.run_stage("hand",{0:["cut"]},lambda _: {"action_type":"cut","TileId":18})
        assert result[0]["TileId"] == expected
    monkeypatch.setattr(bot_executor,"run_room_bot_cpu",inline_cpu)
    asyncio.run(run())


@pytest.mark.parametrize("phase,actions,expected", [
    ("hand",["hu_self","cut"],"cut"), ("claims",["hu_first","peng","pass"],"pass"),
    ("four_joy",["changsha_four_joy","pass"],"pass"),
    ("kong_self_win",["hu_self","pass"],"pass"), ("sea_self_win",["hu_self","pass"],"pass"),
])
def test_exchange_six_auto_bot_declines_all_winning_paths(phase,actions,expected):
    from ...game_changsha.test_exchange_six import make_state
    async def run():
        s = make_state(bot_seats=(0,))
        s.player_list[0].user_id = 0
        result = await s.exchange_six.run_stage(phase,{0:actions},lambda _: {"action_type":actions[0]})
        assert result[0]["action_type"] == expected
    asyncio.run(run())


def test_exchange_six_efficiency_claim_has_explicit_target():
    from ...game_changsha.exchange_six_bot import choose_action
    hand = [11,11,21,22,23,31,32,33,25,25,39,17,18]
    assert choose_action("claims",hand,[],[0]*34,["peng","pass"],{"peng":[11]}) == dict(action_type="peng",target_tile=11)


def test_shanghai_efficiency_uses_a_legal_concealed_kong(monkeypatch):
    from ...game_shanghai import bot
    from ...game_shanghai.test_shanghai_state import make_state
    async def run():
        s = make_state()
        s.bot_speed = "instant"
        p = s.player_list[0]
        p.user_id = 2
        p.hand_tiles = [11]*4+[21,22,23,31,32,33,17,18,19,44]
        p.has_draw_slot = True
        s.game_status = "waiting_hand_action"
        s.action_dict = {0:["angang","cut"]}
        s.waiting_players_list = [0]
        await bot.shanghai_smart_bot_action(s,0,s.action_dict[0],s.game_status)
        data = s.action_queues[0].get_nowait()
        assert (data["action_type"],data["target_tile"]) == ("angang",11)
    monkeypatch.setattr(bot,"run_room_bot_cpu",inline_cpu)
    asyncio.run(run())


@pytest.mark.parametrize("status,action", [("waiting_initial_hu","initial_hu"),("waiting_sea_bottom","sea_bottom")])
def test_changsha_efficiency_uses_offered_special_opportunity(status,action):
    async def run():
        s = table(2,(action,"pass"))
        s.game_status = status
        await smart_bot_action(s,0,s.action_dict[0],status)
        assert s.action_queues[0].get_nowait()["action_type"] == action
    asyncio.run(run())


@pytest.mark.parametrize("rule", ["shanghai","shanxi"])
def test_auto_bot_can_execute_discard_even_if_ready_and_winning(rule):
    if rule == "shanghai":
        from ...game_shanghai.test_shanghai_state import make_state
        s = make_state()
        p = s.player_list[0]
        p.hand_tiles = [11,12,13,21,22,23,31,32,33,17,18,19,44,44]
        p.huapai_list = [51,52,53,54]
        p.declared_ready = p.ready_locked = True
        p.locked_waits = {44}
        for other in s.player_list:
            other.opening_flowers_done = True
    else:
        from ...game_shanxi.test_state import make_state, BASE, ready
        s = make_state()
        p = s.player_list[0]
        ready(p,BASE+[47,47])
    p.user_id = 0
    p.has_draw_slot = True
    p.last_drawn_tile = p.hand_tiles[-1]
    s.current_player_index = 0
    assert s.score_candidate(0,"self_draw")
    assert "cut" in s.check_hand_actions(0)[0]
    with patch("server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",new=AsyncMock()):
        asyncio.run(s.execute_cut(0,{"TileId":p.last_drawn_tile,"cutClass":True,"cutIndex":len(p.hand_tiles)-1}))
    assert p.discard_tiles and not s.pending_winners
