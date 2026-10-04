"""Room-owned bot cadence. Calculation and the artificial wait share one clock.

Offline human automation is deliberately outside this clock.
"""
from __future__ import annotations

import asyncio
from contextvars import ContextVar
from dataclasses import dataclass
from functools import wraps
import time

BOT_SPEEDS = {"instant": 0.0, "fast": 0.5, "medium": 1.0, "slow": 1.5}
DEFAULT_BOT_SPEED = "fast"
DEFAULT_BOT_DELAY = BOT_SPEEDS[DEFAULT_BOT_SPEED]


def normalize_bot_speed(speed) -> str:
    # Older room snapshots used "standard" for the original 0.5-second pace.
    return speed if isinstance(speed, str) and speed in BOT_SPEEDS else DEFAULT_BOT_SPEED


def bot_delay(state, default: float = DEFAULT_BOT_DELAY) -> float:
    speed = getattr(state, "bot_speed", None)
    if speed == "standard":
        speed = "fast"
    return BOT_SPEEDS.get(speed, default)


async def wait_bot_delay(state, started_at: float, default: float = DEFAULT_BOT_DELAY) -> None:
    """Shared clock for adapters whose action protocol differs (e.g. Hongque)."""
    remaining = started_at + bot_delay(state, default) - time.monotonic()
    if remaining > 0:
        await asyncio.sleep(remaining)


def configure_bot_pacing(state, room_data) -> None:
    # Preserve test/headless callers' legacy delay overrides when no room
    # cadence was provided. Live room snapshots always carry this setting.
    if "bot_speed" in room_data:
        state.bot_speed = normalize_bot_speed(room_data["bot_speed"])


def is_bot(state, index: int) -> bool:
    players = getattr(state, "player_list", ())
    return 0 <= index < len(players) and 0 <= getattr(players[index], "user_id", 99) < 10


@dataclass
class BotClock:
    state: object
    player: int
    phase: str
    tick: int | None
    started: float
    delay: float


_clock: ContextVar[BotClock | None] = ContextVar("mahjong_bot_clock", default=None)


def paced_bot(default_delay=lambda: DEFAULT_BOT_DELAY):
    """The public AI entry points all receive (state, seat, actions, phase)."""
    def decorate(fn):
        @wraps(fn)
        async def run(state, player, actions, phase, *args, **kwargs):
            parent = _clock.get()
            if parent is not None and parent.state is state and parent.player == player:
                return await fn(state, player, actions, phase, *args, **kwargs)
            delay = bot_delay(state, default_delay())
            # Some offline-human paths reuse auto_cut_action; room bot settings
            # must not change that player's takeover policy.
            players = getattr(state, "player_list", ())
            if 0 <= player < len(players) and getattr(players[player], "user_id", 0) >= 10:
                delay = default_delay()
            clock = BotClock(state, player, phase, getattr(state, "server_action_tick", None),
                             time.monotonic(), delay)
            token = _clock.set(clock)
            try:
                return await fn(state, player, actions, phase, *args, **kwargs)
            finally:
                _clock.reset(token)
        return run
    return decorate


async def wait_before_bot_submit(state, player: int, action: str) -> bool:
    clock = _clock.get()
    if clock is None or clock.state is not state or clock.player != player:
        return True
    if action != "pass":
        extra = 0.0
        # Offline humans keep their existing takeover timing in protected human rooms.
        if not is_bot(state, player) and clock.delay > 0 and clock.phase == "onlycut_after_action":
            from ..claim_protection import claim_protection_enabled, get_meld_post_gap, supports_claim_protection
            if supports_claim_protection(getattr(state, "room_rule", None)) and claim_protection_enabled(state):
                extra = get_meld_post_gap(state)
        remaining = clock.started + clock.delay + extra - time.monotonic()
        if remaining > 0:
            await asyncio.sleep(remaining)
    return (getattr(state, "server_action_tick", None) == clock.tick
            and player in getattr(state, "waiting_players_list", ())
            and getattr(state, "game_status", None) != "END"
            and getattr(state, "lifecycle_state", "running") == "running")


async def submit_bot_action(send, state, player, action, *args, **kwargs):
    if await wait_before_bot_submit(state, player, action):
        return await send(state, player, action, *args, **kwargs)
