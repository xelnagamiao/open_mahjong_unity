"""Explicit phases, including one-at-a-time flowers and optional last draw."""

from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    REPLACEMENT = "waiting_buhua_action"
    LAST_DRAW = "waiting_last_tile"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    KONG = "waiting_action_qianggang"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.REPLACEMENT, P.TURN},
    P.REPLACEMENT: {P.TURN, P.DISCARD_ONLY, P.END},
    P.LAST_DRAW: {P.REPLACEMENT, P.TURN, P.END},
    P.TURN: {P.RESPONSE, P.KONG, P.REPLACEMENT, P.END},
    P.DISCARD_ONLY: {P.RESPONSE},
    P.RESPONSE: {P.LAST_DRAW, P.REPLACEMENT, P.TURN, P.DISCARD_ONLY, P.END},
    P.KONG: {P.REPLACEMENT, P.TURN, P.END},
    P.END: {P.READY},
    P.READY: {P.START, P.FINISHED},
    P.FINISHED: set(),
}


class StateMachine:
    def __init__(self):
        self.phase = P.START
        self.version = 0

    def transition(self, target):
        target = P(target)
        if target == self.phase:
            return
        if target not in TRANSITIONS[self.phase]:
            raise RuntimeError(f"非法宜兴麻将状态转换：{self.phase.value} -> {target.value}")
        self.phase = target
        self.version += 1
