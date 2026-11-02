RULE_VERSION = 'duocai-v1'
SUB_RULES = ('duocai/guobiao', 'duocai/riichi')


def normalize_config(value=None):
    if value is None:
        return dict(rule_version=RULE_VERSION)
    if not isinstance(value, dict) or set(value) - {'rule_version'} or value.get('rule_version', RULE_VERSION) != RULE_VERSION:
        raise ValueError('夺彩规则版本无效')
    return dict(rule_version=RULE_VERSION)
