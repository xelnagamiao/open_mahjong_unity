from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    REPLACEMENT = "waiting_buhua_action"
    HEAVEN = "waiting_heavenly_action"
    OPENING = "waiting_action_qiangjin"
    DEALER_GOLD = "waiting_dealer_qiangjin"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    KONG = "waiting_action_qianggang"
    SEA = "waiting_last_four"
    FLOWER_WIN = "waiting_flower_win"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.REPLACEMENT, P.HEAVEN, P.OPENING, P.END},
    P.REPLACEMENT: {P.HEAVEN, P.OPENING, P.TURN, P.SEA, P.FLOWER_WIN, P.END},
    P.HEAVEN: {P.OPENING, P.END},
    P.OPENING: {P.TURN, P.END},
    P.DEALER_GOLD: {P.RESPONSE, P.END},
    P.TURN: {P.REPLACEMENT, P.DEALER_GOLD, P.RESPONSE, P.KONG, P.SEA, P.FLOWER_WIN, P.END},
    P.DISCARD_ONLY: {P.RESPONSE},
    P.RESPONSE: {P.REPLACEMENT, P.TURN, P.DISCARD_ONLY, P.SEA, P.FLOWER_WIN, P.END},
    P.KONG: {P.REPLACEMENT, P.TURN, P.SEA, P.FLOWER_WIN, P.END},
    P.SEA: {P.FLOWER_WIN, P.END},
    P.FLOWER_WIN: {P.END},
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
