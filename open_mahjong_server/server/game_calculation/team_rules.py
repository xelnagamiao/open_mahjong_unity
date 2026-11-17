"""Lobby/team capabilities shared across Mahjong families."""
from .guangdong.config import DOUBLES_SUB_RULE as GUANGDONG_DOUBLES
from .sichuan.doubles_rules import is_team_sub_rule as is_sichuan_team
from .riichi.washizu_doubles import enabled as is_washizu_doubles


def is_team_sub_rule(sub_rule):
    return sub_rule == GUANGDONG_DOUBLES or is_sichuan_team(sub_rule) or is_washizu_doubles(sub_rule)
