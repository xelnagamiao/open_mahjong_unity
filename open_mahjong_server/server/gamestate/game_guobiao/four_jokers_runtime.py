"""Keep exact Guobiao / four-joker / sixteen action scoring off the main event loop via CalcExecutor."""
from types import SimpleNamespace
from ...game_calculation import guobiao_sixteen as sixteen
from ...game_calculation import guobiao_four_jokers as rules
from ...game_calculation.calc_executor import run_room_calc_cpu


def _calculate(snapshot, name, args, kwargs):
    from server.gamestate.game_guobiao import action_check
    from server.game_calculation.game_calculation_service import GameCalculationService
    snapshot.calculation_service = GameCalculationService()
    actions = getattr(action_check, name)(snapshot, *args, **kwargs)
    return actions, snapshot.result_dict, [p.waiting_tiles for p in snapshot.player_list]


async def evaluate_actions(state, check, *args, **kwargs):
    if not (rules.enabled(state) or sixteen.enabled(state)):
        # Standard / lanshi / xiaolin / kshen Guobiao: route tingpai via unified
        # AsyncCalculationService (L1 hand-dirty cache + CalcExecutor) before action checks.
        if check.__name__ == "refresh_waiting_tiles":
            return await _async_refresh_waiting_tiles(state, *args, **kwargs)
        if check.__name__ == "check_action_after_cut":
            for item in state.player_list:
                if item.player_index != state.current_player_index:
                    await _async_refresh_waiting_tiles(state, item.player_index)
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
    actions, results, waits = await run_room_calc_cpu(state, _calculate, snapshot, check.__name__, args, kwargs)
    state.result_dict.update(results)
    for player, waiting in zip(state.player_list, waits):
        player.waiting_tiles = waiting
    return actions


async def _async_refresh_waiting_tiles(state, player_index, is_first_action=False):
    from . import blood_battle
    from .sanma import filter_tiles
    from ...game_calculation.async_calculation_service import AsyncCalculationService

    player_item = state.player_list[player_index]
    if blood_battle.enabled(state) and getattr(player_item, "is_hu", False):
        player_item.waiting_tiles = set()
        player_item._waiting_cache_sig = None
        return
    hand_tiles = player_item.hand_tiles[:-1] if is_first_action else player_item.hand_tiles
    sub_rule = getattr(state, "sub_rule", "")
    rule_key = "guobiao/lanshi" if sub_rule == "guobiao/lanshi" else "guobiao"
    async_svc = getattr(getattr(state, "game_server", None), "async_calculation_service", None)
    if async_svc is None:
        async_svc = AsyncCalculationService(getattr(state, "calculation_service", None))
    await async_svc.refresh_player_waiting_tiles(
        state,
        player_item,
        rule_key,
        hand_tiles,
        player_item.combination_tiles,
        tile_filter=lambda w: set(filter_tiles(w, sub_rule)),
    )
