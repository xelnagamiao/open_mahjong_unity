"""Single calculation entry point; profile comes from the validated room."""

from .models import HandContext, HongKongRules
from .old_style import score_old
from .modern13 import score_modern13
from .modern16 import score_modern16
from .lianhuise13 import score_lianhuise13
from .qingzhang_remix import score_qingzhang_remix


def score_hand(context: HandContext, rules: HongKongRules | None = None):
    rules = rules or HongKongRules()
    if rules.is_remix:
        return score_qingzhang_remix(context,rules)
    if rules.is_old:
        return score_old(context,rules)
    if rules.is_sixteen:
        return score_modern16(context,rules)
    if rules.is_lianhuise:
        return score_lianhuise13(context,rules)
    return score_modern13(context,rules)
