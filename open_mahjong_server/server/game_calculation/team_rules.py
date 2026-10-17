"""Lobby/team capabilities shared across Mahjong families."""
from .sichuan.doubles_rules import is_team_sub_rule as is_sichuan_team
from .riichi.washizu_doubles import enabled as is_washizu_doubles


def is_team_sub_rule(sub_rule):
    return is_sichuan_team(sub_rule) or is_washizu_doubles(sub_rule)
