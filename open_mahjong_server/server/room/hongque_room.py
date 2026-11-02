SUB_RULES = ("hongque/v1.6", "hongque/w-v1.0")


def normalize_hongque_config(config):
    sub_rule = config.get("sub_rule") or SUB_RULES[0]
    if sub_rule not in SUB_RULES:
        raise ValueError("不支持的虹雀子规则")
    result = {"sub_rule": sub_rule, "tactical_call": True, "open_cuohe": False,
              "hepai_way": config.get("hepai_way", "multi_ron")}
    if result["hepai_way"] not in ("multi_ron", "head_bump"):
        raise ValueError("无效的和牌方式")
    colourless = config.get("hongque_colourless", True)
    if not isinstance(colourless, bool):
        raise ValueError("加入无色牌必须是布尔值")
    result["hongque_colourless"] = colourless
    for key, maximum in (("hongque_min_fan", 100), ("hongque_min_points", 10000)):
        value = config.get(key, 0)
        if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= maximum:
            raise ValueError(f"{key} 必须是 0 至 {maximum} 的整数")
        result[key] = value if sub_rule == SUB_RULES[1] else 0
    return result
