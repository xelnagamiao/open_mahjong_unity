"""Bot identities must keep their behavior across independently implemented rules."""
import asyncio
import importlib
from types import SimpleNamespace as NS

import pytest

from .auto_cut_ai import auto_cut_action
from .smart_bot_logic import calc_shanten, find_best_cut


REGIONAL = ("hongkong", "filipino", "vietnamese", "guizhou", "hangzhou", "yixing",
            "wenzhou", "nanchang", "shangrao", "nanjing", "fuzhou", "malaysia")


def table(uid=0, actions=("hu_self", "cut")):
    p = NS(user_id=uid, username="bot", hand_tiles=[21,22,23,24,25,26,27,28,29,21,21,22,22,23],
           has_draw_slot=True, declared_ready=False, ready_pending=False, last_draw_last_wall=False,
           forbidden_discards=set(), declared_jingdiao=False, golden_middle=False, youjin=0,
           tag_list=[], combination_tiles=[], discard_tiles=[], kuikae_forbidden_tiles=set(), dingque_suit=0)
    return NS(player_list=[p], action_dict={0:list(actions)}, opening_queue=None,
              legal_discard_tiles=lambda _:set(p.hand_tiles), is_cai_locked=lambda _:False,
              server_action_tick=1, waiting_players_list=[0], current_player_index=0,
              game_status="waiting_hand_action", bot_speed="instant", room_rule="test",
              action_queues={0:asyncio.Queue()}, action_events={0:asyncio.Event()})


def regional_choice(rule, state):
    if rule == "malaysia":
        from ...game_malaysia.runtime import Runtime
        return Runtime.automatic_action(state, 0, bot=True)
    choose = importlib.import_module(f"server.gamestate.game_{rule}.bot").choose_action
    return choose(state, 0) if rule in ("filipino", "vietnamese") else choose(state, 0, state.action_dict[0])


@pytest.mark.parametrize("rule", REGIONAL)
def test_auto_bot_discards_draw_even_when_win_and_kongs_are_offered(rule):
    s = table(actions=("hu_self", "angang", "jiagang", "cut"))
    assert regional_choice(rule, s) == dict(action_type="cut", TileId=23, cutIndex=13, cutClass=True)
    assert s.player_list[0].hand_tiles[-1] == 23


@pytest.mark.parametrize("rule", REGIONAL)
def test_auto_bot_declines_ron_and_all_claims(rule):
    s = table(actions=("hu", "hu_special", "peng", "gang", "chi_left", "vn_claim", "pass"))
    assert regional_choice(rule, s) == dict(action_type="pass")


@pytest.mark.parametrize("rule", REGIONAL)
def test_efficiency_bot_still_accepts_legal_win(rule):
    assert regional_choice(rule, table(2)) == dict(action_type="hu_self")


@pytest.mark.parametrize("rule", ("hongkong", "filipino", "vietnamese", "yixing", "nanjing", "fuzhou", "malaysia"))
def test_auto_bot_replaces_flower(rule):
    assert regional_choice(rule, table(actions=("buhua",))) == dict(action_type="buhua")


def test_auto_bot_uses_legal_fallback_when_draw_is_forbidden():
    s = table()
    s.legal_discard_tiles = lambda _: {21,22}
    assert regional_choice("hongkong", s) == dict(action_type="cut", TileId=22, cutIndex=12, cutClass=False)


@pytest.mark.parametrize("actions,expected", [
    (("buhua",), "buhua"), (("nuki", "cut"), "nuki"), (("cc_draw",), "cc_draw"),
    (("hu_self", "cc_pass"), "cc_pass"), (("sy_reveal", "sy_hide"), "sy_hide"),
    (("hu_self", "sy_pass"), "sy_pass"), (("hu_self", "cut"), "cut"),
])
def test_legacy_auto_bot_submits_normal_actions_without_timeout(actions, expected):
    async def run():
        s = table(actions=actions)
        await auto_cut_action(s, 0, list(actions), s.game_status)
        result = s.action_queues[0].get_nowait()
        assert result["action_type"] == expected
        if expected == "cut":
            assert (result["TileId"], result["cutIndex"], result["cutClass"]) == (23,13,True)
    asyncio.run(run())


def test_lajihe_flower_payment_continues_without_ending_hand():
    async def run():
        s = table(actions=("lajihe_flower_end", "lajihe_flower_continue"))
        s.game_status = "waiting_flower_choice"
        await auto_cut_action(s, 0, s.action_dict[0], s.game_status)
        assert s.action_queues[0].get_nowait()["action_type"] == "lajihe_flower_continue"
    asyncio.run(run())


def test_auto_bot_does_not_duplicate_a_continuation_queued_by_rule_hook():
    async def run():
        s = table(actions=("sy_reveal", "sy_hide"))
        s.action_queues[0].put_nowait({"action_type": "sy_hide", "_action_tick": 1})
        await auto_cut_action(s, 0, s.action_dict[0], s.game_status)
        assert s.action_queues[0].qsize() == 1
    asyncio.run(run())


@pytest.mark.parametrize("melds", range(5))
def test_sixteen_tile_efficiency_retains_sequences_and_pair(melds):
    complete_sets = [11,12,13,21,22,23,31,32,33,24,25,26]
    hand = complete_sets[3*melds:] + [41,41,39,17,18]
    tile, _ = find_best_cut(hand, melds, [0]*34)
    assert tile == 39
    hand.remove(tile)
    assert calc_shanten(hand, melds) == 0
    assert calc_shanten(hand + [19], melds) == -1


def test_efficiency_uses_structural_fallback_when_external_shanten_is_unavailable(monkeypatch):
    from . import smart_bot_logic
    monkeypatch.setattr(smart_bot_logic, "Shanten", None)
    hand = [11,12,13,21,22,23,31,32,33,41,41,39,17,18]
    assert find_best_cut(hand, 0, [0]*34)[0] == 39
    hand.remove(39)
    assert calc_shanten(hand, 0) == 0
