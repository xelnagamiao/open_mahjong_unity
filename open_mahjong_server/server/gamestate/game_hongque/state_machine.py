"""Authoritative lifecycle state machine for Hongque games.

``phase`` is retained as a wire-compatibility view.  Server code transitions by
``HongqueStatus`` so invalid lifecycle jumps fail at their source instead of
silently leaving claim timers and turn ownership out of sync.
"""
from __future__ import annotations

from enum import Enum


class HongqueStatus(str, Enum):
    STARTING = "waiting"
    WAITING_HAND_ACTION = "waiting_hand_action"
    RESOLVING_DISCARD = "resolving_discard"
    WAITING_ACTION_AFTER_CUT = "waiting_action_after_cut"
    WAITING_READY = "waiting_ready"
    END = "END"


PHASE_BY_STATUS = {
    HongqueStatus.STARTING: "starting",
    HongqueStatus.WAITING_HAND_ACTION: "turn",
    HongqueStatus.RESOLVING_DISCARD: "resolving",
    HongqueStatus.WAITING_ACTION_AFTER_CUT: "claim",
    HongqueStatus.WAITING_READY: "round_end",
    HongqueStatus.END: "game_end",
}
STATUS_BY_PHASE = {phase: status for status, phase in PHASE_BY_STATUS.items()}

ALLOWED_TRANSITIONS = {
    HongqueStatus.STARTING: {
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.WAITING_HAND_ACTION: {
        HongqueStatus.RESOLVING_DISCARD,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.RESOLVING_DISCARD: {
        HongqueStatus.WAITING_ACTION_AFTER_CUT,
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.WAITING_READY,
        HongqueStatus.END,
    },
    HongqueStatus.WAITING_ACTION_AFTER_CUT: {
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
