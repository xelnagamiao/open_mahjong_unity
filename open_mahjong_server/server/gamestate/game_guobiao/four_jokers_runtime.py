"""Keep exact four-joker scoring off the network/event-loop thread."""
from types import SimpleNamespace
from ...game_calculation import guobiao_sixteen as sixteen
from ...game_calculation import guobiao_four_jokers as rules
from ..public.ai.bot_executor import run_room_bot_cpu


def _calculate(snapshot, name, args, kwargs):
    from . import action_check
    from ...game_calculation.game_calculation_service import GameCalculationService
    snapshot.calculation_service = GameCalculationService()
    actions = getattr(action_check, name)(snapshot, *args, **kwargs)
    return actions, snapshot.result_dict, [p.waiting_tiles for p in snapshot.player_list]


async def evaluate_actions(state, check, *args, **kwargs):
    if not (rules.enabled(state) or sixteen.enabled(state)):
        return check(state, *args, **kwargs)
    # Only immutable copies cross into the worker. It cannot mutate a live
    # room, socket, clock, action queue or player while a request is pending.
    snapshot = SimpleNamespace(
        sub_rule=state.sub_rule, current_player_index=state.current_player_index,
        current_round=state.current_round, dealer_index=getattr(state, "dealer_index", 0),
        tiles_list=[0] * len(state.tiles_list), open_cuohe=state.open_cuohe,
        hepai_limit=state.hepai_limit, tian_di_ren_he=getattr(state, "tian_di_ren_he", False),
        joker_multi_wait=getattr(state, "joker_multi_wait", False),
        no_joker_bonus=getattr(state, "no_joker_bonus", "add8"),
        black_flower_progressive=getattr(state, "black_flower_progressive", True),
        tactical_call=getattr(state, "tactical_call", False), result_dict={},
        player_list=[SimpleNamespace(player_index=p.player_index,
            hand_tiles=list(p.hand_tiles), combination_tiles=list(p.combination_tiles),
            huapai_list=list(p.huapai_list), discard_tiles=list(p.discard_tiles),
            tag_list=list(p.tag_list), waiting_tiles=set(p.waiting_tiles)) for p in state.player_list])
    actions, results, waits = await run_room_bot_cpu(state, _calculate, snapshot, check.__name__, args, kwargs)
    state.result_dict.update(results)
    for player, waiting in zip(state.player_list, waits):
        player.waiting_tiles = waiting
    return actions
