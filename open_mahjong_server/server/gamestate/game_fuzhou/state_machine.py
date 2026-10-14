from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    REPLACEMENT = "waiting_buhua_action"
    OPENING = "waiting_action_qiangjin"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    KONG = "waiting_action_qianggang"
    SEA = "waiting_last_four"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.REPLACEMENT, P.OPENING, P.END},
    P.REPLACEMENT: {P.OPENING, P.TURN, P.SEA, P.END},
    P.OPENING: {P.TURN, P.END},
    P.TURN: {P.REPLACEMENT, P.RESPONSE, P.KONG, P.SEA, P.END},
    P.DISCARD_ONLY: {P.RESPONSE},
    P.RESPONSE: {P.REPLACEMENT, P.TURN, P.DISCARD_ONLY, P.SEA, P.END},
    P.KONG: {P.REPLACEMENT, P.TURN, P.SEA, P.END},
    P.SEA: {P.END},
    P.END: {P.READY}, P.READY: {P.START, P.FINISHED}, P.FINISHED: set(),
}


class StateMachine:
    def __init__(self):
        self.phase, self.version = P.START, 0

    def transition(self, target):
        target = P(target)
        if target == self.phase:
            return
        if target not in TRANSITIONS[self.phase]:
            raise RuntimeError(f"非法福州状态转换：{self.phase.value} -> {target.value}")
        self.phase, self.version = target, self.version + 1
