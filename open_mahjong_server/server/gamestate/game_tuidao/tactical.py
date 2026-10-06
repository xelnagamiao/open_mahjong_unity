"""MIL claim upgrades using the shared tactical priority/recheck state machine."""
from ..game_taiwan.boardcast import broadcast_do_action, broadcast_ask_other_action
from ..public.tactical_claim import (init_tactical_round_state, clear_tactical_round_state,
    apply_tactical_claim_if_needed, is_decline_action, tactical_mark_player_force_passed,
    tactical_mark_player_passed_in_grace)
from ..public.game_record_manager import flush_unexecuted_claim_applications


async def broadcast_recheck(state, **kwargs):
    state._tuidao_recheck = True
    await broadcast_ask_other_action(state, **kwargs)


async def collect_claim_responses(state):
    init_tactical_round_state(state)
    try:
        responses, allowed = await state.action_clock_manager.collect(interrupt_on_claim=True)
        submitted = {index: data['action_type'] for index, data in responses.items()}
        for index, action in submitted.items():
            if action == 'force_pass':
                tactical_mark_player_force_passed(state, index)
        claims = [(index, data) for index, data in responses.items()
                  if not is_decline_action(data['action_type'])]
        if not claims:
            return responses, allowed
        index, data = claims[0]
        action, winner, result, _ = await apply_tactical_claim_if_needed(state, data['action_type'], index, data,
            broadcast_do_action=broadcast_do_action, broadcast_ask_other_action=broadcast_recheck,
            submitted_actions=submitted)
        tile = state.jiagang_tile if state.game_status == 'waiting_action_qianggang' else state.player_list[state.current_player_index].discard_tiles[-1]
        flush_unexecuted_claim_applications(state, tile, executed_player=winner, executed_action_type=action)
        for pid, choice in submitted.items():
            responses[pid] = {**responses.get(pid, {}), 'action_type': choice}
        responses[winner] = {**result, 'action_type': action}
        return responses, allowed
    finally:
        clear_tactical_round_state(state)
        state._tuidao_recheck = False
        state.waiting_players_list = []


async def collect_recheck(state, priority, submitted_actions):
    responses, _ = await state.action_clock_manager.collect(interrupt_on_claim=True)
    best = None
    for index, data in responses.items():
        action = data['action_type']
        if submitted_actions is not None:
            submitted_actions[index] = action
        if is_decline_action(action):
            if action == 'force_pass':
                tactical_mark_player_force_passed(state, index)
            tactical_mark_player_passed_in_grace(state, index)
            state.action_dict[index] = []
        elif state.action_priority.get(action, -1) > priority:
            candidate = (state.action_priority[action], action, index, data)
            if best is None or candidate[0] > best[0]:
                best = candidate
    if best is None and submitted_actions is not None:
        for index in state.action_dict:
            if state.action_dict[index]:
                submitted_actions[index] = 'pass'
    return best
