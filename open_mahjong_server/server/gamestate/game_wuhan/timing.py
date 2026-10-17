import asyncio
from ..game_hongzhong.timing import ActionClock as BaseClock, TimedActionQueue, Window
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


async def collect_responses(state):
    if state.game_status not in CLAIMS:
        from ..game_hongzhong.timing import collect_responses as collect
        return await collect(state)
    clock, w = state.action_clock, state.action_clock.ensure()
    allowed = {i: list(a) for i, a in state.action_dict.items() if a}
    pending, responses = set(allowed) - w.resolved, {}
    received_at = {}
    state.waiting_players_list = sorted(pending)

    def finish(index):
        # Waiting for another seat is not this player's thinking time. A revised
        # response uses its own arrival time, even if collection finishes late.
        clock.charge(index, received_at.get(index, clock.deadline(index)))
        state.action_dict[index] = []
        pending.remove(index)

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
                        received_at[index] = received
                state.action_events[index].clear()
                if clock.now() >= clock.deadline(index):
                    finish(index)
            # Revisions remain possible while another seat is deciding. Once
            # everyone has answered, resolve immediately instead of waiting out
            # the entire room bank (25 seconds with the default configuration).
            if pending.issubset(responses):
                for index in tuple(pending):
                    finish(index)
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
