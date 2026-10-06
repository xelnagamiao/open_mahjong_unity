"""Fast MIL replies use the real router, broadcaster, collector and tile executor."""

import asyncio
from types import SimpleNamespace

import pytest

from ..gamestate_router import handle_gamestate_message
from ..game_taiwan import boardcast
from ..game_tuidao.test_state import make_state as tuidao_state
from ..game_guangdong.test_state import make_state as guangdong_state
from ..game_changchun.test_state import make_state as changchun_state
from ..game_hangzhou.test_flow import turn as hangzhou_turn
from ..game_hangzhou.actions import claim_actions as hangzhou_claims
from ..game_hangzhou.state_machine import Phase as HangzhouPhase
from ..game_guizhou.test_flow import configured_turn as guizhou_turn
from ..game_guizhou.actions import claim_actions as guizhou_claims
from ..game_guizhou.state_machine import Phase as GuizhouPhase
from ..verifier.host import build_db_manager, build_game_server
from .lifecycle import cancel_auxiliary_tasks


RULES = ("tuidao", "guizhou", "hangzhou", "guangdong", "changchun")
TACTICAL_RULES = RULES
FILLERS = [11, 12, 17, 19, 21, 23, 25, 27, 31, 33, 35, 37, 39]
TILE = 15


class ReplySocket:
    def __init__(self, callback=None):
        self.messages = []
        self.callback = callback

    async def send_json(self, payload):
        self.messages.append(payload)
        if self.callback is not None:
            await self.callback(payload)


def make_claim(rule, *, action="peng", index=1, tactical=False):
    if rule == "hangzhou":
        state = hangzhou_turn(round_timer=1, step_timer=0, tactical_call=tactical)
    elif rule == "guizhou":
        state = guizhou_turn()
        state.round_time, state.step_time = 1, 0
        state.tactical_call = tactical
    else:
        factory = {"tuidao": tuidao_state, "guangdong": guangdong_state,
                   "changchun": changchun_state}[rule]
        state = factory(round_timer=1, step_timer=0, tips=False, tactical_call=tactical)
    state.game_server = build_game_server(build_db_manager(), messages=[])
    state.game_server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
        player.hand_tiles = list(FILLERS)
        player.combination_tiles, player.combination_mask = [], []
        player.has_draw_slot = False
        player.remaining_time = 1
        connection = state.game_server.user_id_to_connection[player.user_id]
        connection.websocket = ReplySocket()
    prefix = [14, 16] if action == "chi_mid" else [TILE] * (3 if action == "gang" else 2)
    state.player_list[index].hand_tiles = prefix + FILLERS[:13 - len(prefix)]
    state.tiles_list = [19] * 40
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [TILE]
    state.player_list[0].discard_origin_tiles = [TILE]
    state.player_list[0].discard_riichi_flags = [False]
    state.tactical_pre_grace_delay = 0
    state.tactical_grace_seconds = 0.05
    open_claim(state, rule)
    assert action in state.action_dict[index], (rule, action, state.action_dict)
    return state


def open_claim(state, rule):
    if rule in ("hangzhou", "guizhou"):
        state.outbound_payloads.clear()
        state.outbound_send_cursor = 0
        phase = HangzhouPhase if rule == "hangzhou" else GuizhouPhase
        claims = hangzhou_claims if rule == "hangzhou" else guizhou_claims
        state.open_action_window(state._window(phase.RESPONSE, 0, TILE, claims(state, 0, TILE)))
    else:
        state.game_status = "waiting_action_after_cut"
        state.action_dict = state.check_discard_actions(TILE)
        state.prepare_action_window()


async def submit(state, index, action, tick):
    player = state.player_list[index]
    connection = state.game_server.user_id_to_connection[player.user_id]
    await handle_gamestate_message(state.game_server, f"conn-{player.user_id}",
        dict(type=f"gamestate/{state.room_rule}/send_action", gamestate_id=state.gamestate_id,
             action=action, action_tick=tick), connection.websocket)


async def publish(state, rule):
    if rule in ("hangzhou", "guizhou"):
        await state.flush_outbound_payloads()
    else:
        await boardcast.broadcast_ask_other_action(state)


async def resolve(state, rule):
    if rule == "hangzhou":
        await state.resolve_action_window(timeout=0.1)
    elif rule == "guizhou":
        responses = await state.wait_action(timeout=0.1)
        state.apply_action_results(state.live_pending_window, responses)
    else:
        await state.wait_action()


CASES = [(rule, action, index) for rule in RULES for action in ("peng", "gang", "pass")
         for index in (1, 2, 3)]
CASES += [(rule, "chi_mid", 1) for rule in ("tuidao", "hangzhou", "changchun")]


@pytest.mark.parametrize("rule,action,index", CASES)
@pytest.mark.parametrize("clicks", [1, 5])
def test_button_reply_during_broadcast_is_accepted_and_executed_once(rule, action, index, clicks):
    async def run():
        state = make_claim(rule, action=action, index=index)
        async def answer(payload):
            info = payload.get("ask_other_action_info")
            if info and info.get("action_list"):
                for _ in range(clicks):
                    await submit(state, index, action, info["action_tick"])
        state.game_server.user_id_to_connection[state.player_list[index].user_id].websocket.callback = answer
        try:
            await publish(state, rule)
            assert not state.action_queues[index].empty(), "The advertised reply was rejected at ingress"
            await asyncio.wait_for(resolve(state, rule), 1)
            melds = state.player_list[index].combination_tiles
            if action == "pass":
                assert melds == []
            else:
                assert len(melds) == 1, "The accepted reply was lost or applied more than once"
                assert state.current_player_index == index
                assert state.player_list[0].discard_tiles == []
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


TACTICAL_CASES = ("upgrade", "pass", "opening_force_pass", "pre_submitted",
                  "stale_then_valid", "late_opening_force_pass", "force_pass_during_application")


@pytest.mark.parametrize("rule", TACTICAL_RULES)
@pytest.mark.parametrize("choice", TACTICAL_CASES)
@pytest.mark.parametrize("clicks", [1, 5])
def test_tactical_recheck_and_late_decline_through_router(rule, choice, clicks):
    async def run():
        lower, higher = (3, 1) if rule == "guangdong" else (1, 2)
        initial_action = "peng" if rule in ("guangdong", "guizhou") else "chi_mid"
        upgrade = "hu_first" if rule == "guangdong" else "hu" if rule == "guizhou" else "peng"
        state = make_claim(rule, action=initial_action, index=lower, tactical=True)
        state.player_list[higher].hand_tiles = (
            [11, 11, 11, 22, 22, 22, 33, 33, 33, 41, 41, 41, TILE]
            if rule == "guangdong" else
            [11, 11, 11, 22, 22, 22, 33, 33, 33, 38, 38, 38, TILE]
            if rule == "guizhou" else [TILE, TILE] + FILLERS[:11])
        open_claim(state, rule)
        assert upgrade in state.action_dict[higher]
        opening_tick = None
        answered = set()
        rechecks = []
        accepted_rechecks = []
        rejected_stale_claims = []
        deferred_packets = []

        async def answer(index, payload):
            nonlocal opening_tick
            info = payload.get("ask_other_action_info")
            if info and info.get("action_list"):
                key = (index, info["action_tick"])
                if key in answered:
                    return
                answered.add(key)
                if not info.get("is_tactical_recheck"):
                    opening_tick = info["action_tick"]
                    action = initial_action if index == lower else (
                        upgrade if choice == "pre_submitted" else
                        "force_pass" if choice == "opening_force_pass" else "pass")
                    for _ in range(clicks):
                        await submit(state, index, action, opening_tick)
                elif index == higher:
                    rechecks.append(info)
                    if choice == "stale_then_valid":
                        # A real inbound packet runs independently of the
                        # outbound FIFO. Guizhou resends through that same FIFO.
                        deferred_packets.append(asyncio.create_task(submit(state, index, upgrade, opening_tick)))
                        await asyncio.sleep(0)
                        rejected_stale_claims.append(state.action_queues[index].empty())
                    action = ("force_pass" if choice == "late_opening_force_pass" else
                              "pass" if choice == "pass" else upgrade)
                    tick = opening_tick if choice == "late_opening_force_pass" else info["action_tick"]
                    for _ in range(clicks):
                        await submit(state, index, action, tick)
                    accepted_rechecks.append(not state.action_queues[index].empty())
            do_info = payload.get("do_action_info")
            if (index == higher and choice == "force_pass_during_application"
                    and do_info and do_info.get("is_claim") and not rechecks):
                for _ in range(clicks):
                    await submit(state, index, "force_pass", opening_tick)

        for index in (lower, higher):
            socket = state.game_server.user_id_to_connection[state.player_list[index].user_id].websocket
            socket.callback = lambda payload, index=index: answer(index, payload)
        try:
            await publish(state, rule)
            await asyncio.wait_for(resolve(state, rule), 1)
            declined = choice in ("pass", "opening_force_pass", "late_opening_force_pass",
                                  "force_pass_during_application")
            if declined:
                assert len(state.player_list[lower].combination_tiles) == 1
                assert not state.player_list[higher].combination_tiles
                if rule == "guangdong":
                    assert not state.pending_winners
            elif rule == "guangdong":
                assert [winner["index"] for winner in state.pending_winners] == [higher]
                assert not state.player_list[lower].combination_tiles
            elif rule == "guizhou":
                assert state.game_status == "END" and state.round_settlement is not None
                assert not state.player_list[lower].combination_tiles
            else:
                assert len(state.player_list[higher].combination_tiles) == 1
                assert not state.player_list[lower].combination_tiles
            if choice in ("opening_force_pass", "force_pass_during_application", "pre_submitted"):
                assert rechecks == []
            else:
                assert len(rechecks) == 1
                assert accepted_rechecks == [True], "Recheck reply was silently rejected"
            if choice == "stale_then_valid":
                assert rejected_stale_claims == [True]
        finally:
            await cancel_auxiliary_tasks(state)
            await asyncio.gather(*deferred_packets, return_exceptions=True)
    asyncio.run(run())


@pytest.mark.parametrize("bad_tick", [None, True, "old", -1, "future"])
@pytest.mark.parametrize("pending", [False, True])
def test_hangzhou_force_pass_cannot_escape_tick_or_seat_authority(bad_tick, pending):
    async def run():
        state = make_claim("hangzhou", tactical=True)
        opening = state.server_action_tick
        state.server_action_tick += 2
        if not pending:
            state.waiting_players_list = []
            state.action_dict = {index: [] for index in range(4)}
        tick = opening + 3 if bad_tick == "future" else opening - 1 if bad_tick == -1 else bad_tick
        try:
            await submit(state, 1, "force_pass", tick)
            assert state._tactical_force_passed_players == set()
            assert state.action_queues[1].empty()
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("pending", [False, True])
def test_hangzhou_force_pass_during_pause_stays_rejected(pending):
    async def run():
        state = make_claim("hangzhou", tactical=True)
        if not pending:
            state.waiting_players_list = []
            state.action_dict = {index: [] for index in range(4)}
        state.vote_manager = SimpleNamespace(phase="paused")
        try:
            await submit(state, 1, "force_pass", state.server_action_tick)
            assert state._tactical_force_passed_players == set()
            assert state.action_queues[1].empty()
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
