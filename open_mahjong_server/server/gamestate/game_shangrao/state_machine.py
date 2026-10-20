from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    TURN = "waiting_hand_action"
    AFTER_MELD = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.TURN}, P.TURN: {P.RESPONSE, P.TURN, P.END},
    P.AFTER_MELD: {P.RESPONSE, P.TURN, P.END},
    P.RESPONSE: {P.TURN, P.AFTER_MELD, P.END},
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
            raise RuntimeError(f"非法上饶状态转换：{self.phase.value} -> {target.value}")
        self.phase = target
        self.version += 1
