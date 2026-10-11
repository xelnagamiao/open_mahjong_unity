"""One delivery-based step-plus-bank clock for each actual Hangzhou ask."""

from dataclasses import dataclass
import math


@dataclass
class ActionClock:
    bank: float
    step: float
    minimum_duration: float = 0.0  # Preserve configured bot presentation pacing.
    started_at: float | None = None
    received_at: float | None = None

    def start(self, now):
        if self.started_at is None:
            self.started_at = now

    @property
    def deadline(self):
        return None if self.started_at is None else self.started_at + max(self.bank + self.step, self.minimum_duration)

    def remaining_bank(self, now):
        elapsed = 0.0 if self.started_at is None else max(0.0, now - self.started_at)
        return max(0.0, self.bank - max(0.0, elapsed - self.step))

    def parts(self, now):
        elapsed = 0.0 if self.started_at is None else max(0.0, now - self.started_at)
        step = max(0.0, self.step - elapsed)
        # Round the total only once: separate ceilings could add a whole second.
        total = math.ceil(max(0.0, self.bank + self.step - elapsed))
        step_display = min(total, math.ceil(step))
        return total - step_display, step_display
