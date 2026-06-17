"""局终和牌结算演出时长（与客户端 RoundEndTiming 保持一致）。"""

ROUND_END_PRESENTATION_FADE_SEC = 0.35 # 局终面板渐显时间
ROUND_END_HAND_REVEAL_SEC = 1.5 # 赢家明牌展开时间
HEPAI_TRAVEL_SEC = 0.2 # 和牌张就位/河牌抓取（无位移动画时也预留此时长）
HU_FAN_REVEAL_INTERVAL_SEC = 0.5 # 番种展示间隔时间
HU_BEFORE_TOTAL_PANEL_SEC = 0.5 # 和牌前总番数展示时间
HU_CONFIRM_COUNTDOWN_SEC = 8 # 和牌确认倒计时时间
SICHUAN_MID_PANEL_CONFIRM_SEC = 3.0 # 四川终局非末步面板确认等待
SICHUAN_CHAJIAO_STATUS_HOLD_SEC = 0.5 # 四川查叫：有叫/没叫/花猪状态展示
SICHUAN_LIUJU_PANEL_HOLD_SEC = 2.0 # 四川查叫非末步面板停留（不含渐显，已废弃，保留兼容）
LIUJU_CAPTION_HOLD_SEC = 2 # 流局提示停留时间
DRAW_NOTEN_PENALTY_SEC = 3 # 荒牌不听罚符面板显示时间


def hu_result_ready_pre_panel_seconds() -> float:
    """倒牌阶段：位移动画 + 展开停留（与客户端 HepaiRevealTiming.PrePanelTotalSeconds 一致）。"""
    return ROUND_END_HAND_REVEAL_SEC + HEPAI_TRAVEL_SEC


def hu_result_ready_wait_seconds(
    fan_count: int,
    fu_fan_count: int = 0,
    pre_panel_delay_sec: float | None = None,
    include_panel_fade: bool = True,
) -> float:
    if pre_panel_delay_sec is None:
        pre_panel_delay_sec = hu_result_ready_pre_panel_seconds()
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
        total += hu_result_ready_pre_panel_seconds()
    if has_draw_noten_penalty:
        total += DRAW_NOTEN_PENALTY_SEC
    return total


def sichuan_settle_hu_panel_wait_seconds(fan_count: int, *, is_final: bool = False) -> float:
    """四川终局 settle_hu：渐显 + 番种×0.5s + 0.5s 总分 + 3s/8s 确认。"""
    total = ROUND_END_PRESENTATION_FADE_SEC
    total += fan_count * HU_FAN_REVEAL_INTERVAL_SEC
    total += HU_BEFORE_TOTAL_PANEL_SEC
    total += HU_CONFIRM_COUNTDOWN_SEC if is_final else SICHUAN_MID_PANEL_CONFIRM_SEC
    return total


def sichuan_chajiao_panel_wait_seconds(*, is_final: bool = False) -> float:
    """四川查叫非末步：0.5s 状态 + 3s 确认 + 0.35s 渐显间隔；末步由 ready 阶段统一等待。"""
    if is_final:
        return 0.0
    return (
        SICHUAN_CHAJIAO_STATUS_HOLD_SEC
        + SICHUAN_MID_PANEL_CONFIRM_SEC
        + ROUND_END_PRESENTATION_FADE_SEC
    )


def sichuan_liuju_final_ready_wait_seconds() -> float:
    """四川流局末步面板：渐显 + 8s 确认（与 EndResultPanel 一致）。"""
    return ROUND_END_PRESENTATION_FADE_SEC + HU_CONFIRM_COUNTDOWN_SEC


def shuhewei_ready_wait_seconds(shuhewei_reveal_seconds: float, has_hu: bool) -> float:
    total = ROUND_END_PRESENTATION_FADE_SEC + shuhewei_reveal_seconds + HU_CONFIRM_COUNTDOWN_SEC
    if has_hu:
        total += hu_result_ready_pre_panel_seconds()
    return total
