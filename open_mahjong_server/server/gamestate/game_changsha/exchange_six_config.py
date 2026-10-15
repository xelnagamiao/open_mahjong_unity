"""换六张固定配置：建房和加载对局时共用，覆盖客户端传来的旧玩法设置。"""
SUB_RULE = "changsha/exchange_six"
RULE_VERSION = 2
FIXED_OPTIONS = {
    "tactical_call": False,
    "open_kong_replacement_count": 4,
    "bird_count": 2,
    "dealer_bird": False,
    "base_score_no_dealer": True,
    "small_hu_score": 2,
    "big_hu_score": 8,
    "small_hu_self_draw_only": False,
    "score_cap_type": "none",
    "score_cap_value": 0,
    "initial_hu_si_xi": True,
    "initial_hu_ban_ban_hu": False,
    "initial_hu_que_yi_se": False,
    "initial_hu_liu_liu_shun": False,
    "initial_hu_san_tong": False,
}


def normalize_config(config, sub_rule):
    if sub_rule == SUB_RULE:
        return {"exchange_selection_seconds": 30, **config, **FIXED_OPTIONS}
    return config
