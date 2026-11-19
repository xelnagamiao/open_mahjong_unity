from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..public import tactical_claim as tactical
from ..public.game_record_manager import flush_unexecuted_claim_applications


class DoublesClaimMixin:
    async def _resolve_tactical(self, responses, allowed, *, rob_kong):
        resolve = (TaiwanGameState.resolve_rob_kong_responses if rob_kong
                   else TaiwanGameState.resolve_discard_responses)
        choices = {i: data["action_type"] for i, data in responses.items()
                   if data.get("action_type") in allowed.get(i, ())}
        claims = {i: a for i, a in choices.items() if not tactical.is_decline_action(a)}
        async def finish():
            playable = {i: values for i, values in allowed.items()
                        if not self.player_list[i].is_hu and "peida" not in self.player_list[i].tag_list}
            responses = {i: {"action_type": a} for i, a in choices.items() if a in playable.get(i, ())}
            return await resolve(self, responses, playable)

        for i, action in choices.items():
            if action == "force_pass":
                tactical.tactical_mark_player_force_passed(self, i)
        if not self.tactical_call or not claims:
            tactical.clear_tactical_round_state(self)
            return await finish()

        def rank(i, action):
            return (self.action_priority[action], -((i - self.current_player_index) % 4))

        first = max(claims, key=lambda i: rank(i, claims[i]))
        leading = claims[first]
        tile = self.jiagang_tile if rob_kong else self.player_list[self.current_player_index].discard_tiles[-1]
        windows = {}
        for i, actions in allowed.items():
            if choices.get(i) == "force_pass" or tactical.is_hu_claim_action(choices.get(i)):
                continue
            upgrades = [a for a in actions if tactical.is_hu_claim_action(a)
                        or not tactical.is_decline_action(a) and rank(i, a) > rank(first, leading)]
            if upgrades:
                windows[i] = upgrades + ["pass", "force_pass"]
        try:
            if windows:
                await self._broadcast_claim(self, action_list=[leading], action_player=first,
                                            cut_tile=tile, is_claim=True)
                self.action_dict = {i: windows.get(i, []) for i in range(4)}
                await self.broadcast_tactical_recheck(self, remaining_time_override=self.tactical_grace_seconds,
                                                      is_tactical_recheck=True)
                amended, _ = await self.collect_action_responses()
                for i, data in amended.items():
                    action = data["action_type"]
                    if action in windows.get(i, ()) and not tactical.is_decline_action(action):
                        choices[i] = action
                final_claims = {i: a for i, a in choices.items() if not tactical.is_decline_action(a)}
                final = max(final_claims, key=lambda i: rank(i, final_claims[i]))
                retained = first == final or tactical.is_hu_claim_action(leading)
                flush_unexecuted_claim_applications(self, tile, executed_player=first if retained else final,
                                                    executed_action_type=leading if retained else final_claims[final])
            return await finish()
        finally:
            self._guangdong_tactical_recheck = False
            tactical.clear_tactical_round_state(self)
