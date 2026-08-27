"""Authoritative lifecycle state machine for Hongque games.

``phase`` is retained as a wire-compatibility view.  Server code transitions by
``HongqueStatus`` so invalid lifecycle jumps fail at their source instead of
silently leaving claim timers and turn ownership out of sync.

状态名与国标/青雀对齐：``deal_card`` 是无人鸣牌后的历时摸牌，
``onlycut_after_action`` 是亮牌后的手牌窗口（切 / 加杠 / 补；补牌后才能和）。
"""
from __future__ import annotations

from enum import Enum


class HongqueStatus(str, Enum):
    STARTING = "waiting"
    DEAL_CARD = "deal_card"
    WAITING_HAND_ACTION = "waiting_hand_action"
    ONLYCUT_AFTER_ACTION = "onlycut_after_action"
    RESOLVING_DISCARD = "resolving_discard"
    WAITING_ACTION_AFTER_CUT = "waiting_action_after_cut"
    WAITING_READY = "waiting_ready"
    END = "END"


PHASE_BY_STATUS = {
    HongqueStatus.STARTING: "starting",
    HongqueStatus.DEAL_CARD: "resolving",
    HongqueStatus.WAITING_HAND_ACTION: "turn",
    HongqueStatus.ONLYCUT_AFTER_ACTION: "turn",
    HongqueStatus.RESOLVING_DISCARD: "resolving",
    HongqueStatus.WAITING_ACTION_AFTER_CUT: "claim",
    HongqueStatus.WAITING_READY: "round_end",
    HongqueStatus.END: "game_end",
}
# 同一 phase 可能对应多个 status；快照/测试的 force_phase 回到常规等待态。
STATUS_BY_PHASE = {
    "starting": HongqueStatus.STARTING,
    "resolving": HongqueStatus.RESOLVING_DISCARD,
    "turn": HongqueStatus.WAITING_HAND_ACTION,
    "claim": HongqueStatus.WAITING_ACTION_AFTER_CUT,
    "round_end": HongqueStatus.WAITING_READY,
    "game_end": HongqueStatus.END,
}

ALLOWED_TRANSITIONS = {
    HongqueStatus.STARTING: {
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.DEAL_CARD: {
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.WAITING_HAND_ACTION: {
        HongqueStatus.RESOLVING_DISCARD,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.ONLYCUT_AFTER_ACTION: {
        HongqueStatus.RESOLVING_DISCARD,
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.RESOLVING_DISCARD: {
        HongqueStatus.WAITING_ACTION_AFTER_CUT,
        HongqueStatus.DEAL_CARD,
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.WAITING_ACTION_AFTER_CUT: {
        HongqueStatus.ONLYCUT_AFTER_ACTION,
        HongqueStatus.DEAL_CARD,
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.WAITING_READY: {
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.END,
    },
    HongqueStatus.END: set(),
}


class HongqueStateMachine:
    def __init__(self) -> None:
        self.status = HongqueStatus.STARTING
        self.version = 0
        self.history: list[tuple[HongqueStatus, HongqueStatus]] = []

    @property
    def phase(self) -> str:
        return PHASE_BY_STATUS[self.status]

    def transition(self, target: HongqueStatus) -> None:
        target = HongqueStatus(target)
        if target == self.status:
            return
        if target not in ALLOWED_TRANSITIONS[self.status]:
            raise RuntimeError(
                f"invalid Hongque state transition: {self.status.value} -> {target.value}"
            )
        previous = self.status
        self.status = target
        self.version += 1
        self.history.append((previous, target))

    def force_phase(self, phase: str) -> None:
        """Compatibility hook for restored snapshots and existing test fixtures."""
        try:
            target = STATUS_BY_PHASE[phase]
        except KeyError as exc:
            raise ValueError(f"unknown Hongque phase: {phase}") from exc
        if target != self.status:
            previous = self.status
            self.status = target
            self.version += 1
            self.history.append((previous, target))
