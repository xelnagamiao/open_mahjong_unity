import asyncio
from ..game_hongzhong.timing import ActionClock as BaseClock, TimedActionQueue, seconds, Window
from ..public.lifecycle import start_owned_task

CLAIMS = frozenset(('waiting_action_after_cut', 'waiting_action_qianggang'))
PHASES = CLAIMS | {'waiting_hand_action'}


class ActionClock(BaseClock):
    def ensure(self):
        if self.state.game_status not in PHASES:
            return None
        key = self.key()
        if self.window is None or self.window.key != key:
            banks = {p.player_index: self.balances.setdefault(p.player_index, float(p.remaining_time))
                     for p in self.state.player_list}
            self.window = Window(key, self.now(), max(0, self.state.step_time), banks)
        return self.window

    def deadline(self, index):
        if self.state.game_status not in CLAIMS:
            return super().deadline(index)
        w = self.ensure()
        return w.delivered.get(index, w.created) + self.state.claim_response_seconds

    def project(self, player):
        if self.state.game_status not in CLAIMS:
            return super().project(player)
        w = self.ensure()
        elapsed = max(0, self.now() - w.delivered[player.player_index]) if player.player_index in w.delivered else 0
        return 0, seconds(self.state.claim_response_seconds - elapsed)

    def charge(self, index, received_at):
        if self.state.game_status in CLAIMS:
            self.ensure().resolved.add(index)
        else:
            super().charge(index, received_at)


async def collect_responses(state):
    if state.game_status not in CLAIMS:
        from ..game_hongzhong.timing import collect_responses as collect
        return await collect(state)
    clock, w = state.action_clock, state.action_clock.ensure()
    allowed = {i: list(a) for i, a in state.action_dict.items() if a}
    pending, responses = set(allowed) - w.resolved, {}
    state.waiting_players_list = sorted(pending)
    try:
        while pending:
            for index in tuple(pending):
                q = state.action_queues[index]
                while not q.empty():
                    candidate = q.get_nowait()
                    received = q.last_received_at
                    if (isinstance(candidate, dict) and candidate.get('action_type') in allowed[index]
                            and received < clock.deadline(index)):
                        responses[index] = dict(candidate)
                state.action_events[index].clear()
                if clock.now() >= clock.deadline(index):
                    clock.charge(index, clock.deadline(index))
                    pending.remove(index)
            state.waiting_players_list = sorted(pending)
            if not pending:
                break
            tasks = [start_owned_task(state, state.action_events[i].wait()) for i in pending]
            try:
                await asyncio.wait(tasks, timeout=max(0, min(clock.deadline(i) for i in pending) - clock.now()),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
    finally:
        state.waiting_players_list = []
    return responses, allowed
