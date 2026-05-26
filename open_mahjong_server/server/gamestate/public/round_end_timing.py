"""局终和牌结算演出时长（与客户端 RoundEndTiming 保持一致）。"""

ROUND_END_PRESENTATION_FADE_SEC = 0.35 # 局终面板渐显时间
ROUND_END_HAND_REVEAL_SEC = 1.5 # 赢家明牌展开时间
HU_FAN_REVEAL_INTERVAL_SEC = 0.5 # 番种展示间隔时间
HU_BEFORE_TOTAL_PANEL_SEC = 0.5 # 和牌前总番数展示时间
HU_CONFIRM_COUNTDOWN_SEC = 8 # 和牌确认倒计时时间
LIUJU_CAPTION_HOLD_SEC = 2 # 流局提示停留时间
DRAW_NOTEN_PENALTY_SEC = 3 # 荒牌不听罚符面板显示时间


def hu_result_ready_wait_seconds(
    fan_count: int,
    fu_fan_count: int = 0,
    pre_panel_delay_sec: float = ROUND_END_HAND_REVEAL_SEC,
    include_panel_fade: bool = True,
) -> float:
    total = pre_panel_delay_sec
    if include_panel_fade:
        total += ROUND_END_PRESENTATION_FADE_SEC
    total += fu_fan_count * HU_FAN_REVEAL_INTERVAL_SEC
    total += fan_count * HU_FAN_REVEAL_INTERVAL_SEC
    total += HU_BEFORE_TOTAL_PANEL_SEC + HU_CONFIRM_COUNTDOWN_SEC
    return total


def liuju_ready_wait_seconds(include_hand_reveal: bool = False, has_draw_noten_penalty: bool = False) -> float:
    total = ROUND_END_PRESENTATION_FADE_SEC + LIUJU_CAPTION_HOLD_SEC
    if include_hand_reveal:
        total += ROUND_END_HAND_REVEAL_SEC
    if has_draw_noten_penalty:
        total += DRAW_NOTEN_PENALTY_SEC
    return total


def shuhewei_ready_wait_seconds(shuhewei_reveal_seconds: float, has_hu: bool) -> float:
    total = ROUND_END_PRESENTATION_FADE_SEC + shuhewei_reveal_seconds + HU_CONFIRM_COUNTDOWN_SEC
    if has_hu:
        total += ROUND_END_HAND_REVEAL_SEC
    return total
