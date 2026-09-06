"""复刻 Unity GameRecordManager.ApplyActionToRecordState（跳转推演，无动画）。"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Sequence

from . import meld_codec as codec
from .decoder import (
    hu_fan_contains_cuohe,
    parse_buhua_mo_flag,
    parse_hu_fan_list,
    parse_kan_mo_gang_flag,
    resolve_acting_player,
    resolve_buhua_recipient,
    try_resolve_buhua_transfer,
    validate_kan_mo_gang_tick,
)
from .flags import resolve_record_flags
from .hu_hand import is_flower_win, needs_ron_tile, try_parse_hepai_tile


SKIP_ACTIONS = {"ask_hand", "ask_other", "ca"}


def stringify_tick(tick: Sequence[Any]) -> List[str]:
    out: List[str] = []
    for item in tick:
        if isinstance(item, (list, dict)):
            out.append(json.dumps(item, ensure_ascii=False, separators=(",", ":")))
        elif item is None:
            out.append("")
        else:
            out.append(str(item))
    return out


def parse_tick_bool(tick: Sequence[str], index: int) -> bool:
    if tick is None or index >= len(tick):
        return False
    return str(tick[index]).strip().upper() in ("T", "TRUE", "1")


def parse_score_changes(tick: Sequence[str], index: int) -> Optional[List[int]]:
    if tick is None or index >= len(tick) or not tick[index]:
        return None
    raw = str(tick[index]).strip()
    if not raw.startswith("["):
        return None
    try:
        parsed = json.loads(raw)
        return [int(x) for x in parsed]
    except (TypeError, ValueError, json.JSONDecodeError):
        inner = raw[1:-1]
        parts = [p.strip() for p in inner.split(",") if p.strip() != ""]
        result = []
        for part in parts:
            try:
                result.append(int(part))
            except ValueError:
                result.append(0)
        return result


def relative_position(self_index: int, other_index: int) -> str:
    if self_index == other_index:
        return "self"
    mapping = {
        0: {1: "right", 2: "top", 3: "left"},
        1: {0: "left", 2: "right", 3: "top"},
        2: {0: "top", 1: "left", 3: "right"},
        3: {0: "right", 1: "top", 2: "left"},
    }
    return mapping.get(self_index, {}).get(other_index, "self")


def _player_has_suit(player: "RecordPlayer", suit: int) -> bool:
    for tile_id in player.tile_list:
        if tile_id // 10 == suit:
            return True
    for combo in player.combination_tiles:
        if not combo or len(combo) < 2:
            continue
        try:
            tile_id = codec.normalize_tile(int(combo[1:]))
        except ValueError:
            continue
        if tile_id // 10 == suit:
            return True
    return False


def title_str(game_title: dict, key: str, default: str = "") -> str:
    value = (game_title or {}).get(key, default)
    return "" if value is None else str(value)


@dataclass
class RecordPlayer:
    original_player_index: int
    player_index: int
    score: int = 0
    tile_list: List[int] = field(default_factory=list)
    discard_tiles: List[int] = field(default_factory=list)
    discard_is_moqie: List[bool] = field(default_factory=list)
    discard_riichi_flags: List[bool] = field(default_factory=list)
    combination_tiles: List[str] = field(default_factory=list)
    combination_masks: List[List[int]] = field(default_factory=list)
    huapai_list: List[int] = field(default_factory=list)
    show_hand_draw_slot_active: bool = False
    is_riichi: bool = False
    is_hu: bool = False
    pending_riichi_horizontal: bool = False
    ready_qualification: Optional[str] = None
    tag_list: List[str] = field(default_factory=list)
    dingque_suit: int = 0


class RecordSim:
    """对应 GameRecordManager 跳转推演状态。"""

    def __init__(self, record: dict):
        self.record = record
        self.game_title = record.get("game_title") or {}
        self.flags = resolve_record_flags(self.game_title)
        self.players: Dict[int, RecordPlayer] = {}
        self.current_player_index = 0
        self.last_discard_player_index = -1
        self.last_discard_tile_id = -1
        self.last_winnable_tile_id = -1
        self.last_jiagang_player_index = -1
        self.current_tiles_list: List[int] = []
        self.backward_tiles_type = "double"
        self.record_dead_wall_count = 0
        self.record_dead_wall_mode = ""
        self.record_taiwan_ron_blocked: set[int] = set()
        self.record_riichi_sticks = 0
        self.round_data: dict = {}

    def is_sichuan(self) -> bool:
        return self.flags.rule_id == "sichuan"

    def is_sichuan_blood(self) -> bool:
        if not self.is_sichuan():
            return False
        return title_str(self.game_title, "blood_battle", "true") != "false"

    def is_taiwan(self) -> bool:
        return self.flags.rule_id == "taiwan"

    def rule_key(self) -> str:
        return self.flags.rule_id or title_str(self.game_title, "rule").lower()

    def load_round(self, round_key: str) -> None:
        rounds = (self.record.get("game_round") or {})
        round_data = rounds[round_key]
        self.round_data = round_data
        self.current_player_index = int(round_data.get("start_player_index") or 0)
        self.last_discard_player_index = -1
        self.last_discard_tile_id = -1
        self.last_winnable_tile_id = -1
        self.last_jiagang_player_index = -1
        self.current_tiles_list = list(round_data.get("tiles_list") or [])
        self.backward_tiles_type = "double"
        self.record_dead_wall_count = 16 if self.is_taiwan() else 0
        self.record_dead_wall_mode = "fixed_tail_16" if self.is_taiwan() else ""
        self.record_taiwan_ron_blocked = set()
        riichi = round_data.get("riichi") or {}
        self.record_riichi_sticks = int(riichi.get("riichi_sticks") or 0)
        self.players = {}
        for seat in range(4):
            tiles = list(round_data.get(f"p{seat}_tiles") or [])
            self.players[seat] = RecordPlayer(
                original_player_index=seat,
                player_index=seat,
                tile_list=tiles,
            )

    def snapshot(self) -> dict:
        return {
            "current_player_index": self.current_player_index,
            "remain_tiles": max(0, len(self.current_tiles_list) - self.record_dead_wall_count),
            "last_discard_tile_id": self.last_discard_tile_id,
            "players": {
                str(index): {
                    "tile_list": list(player.tile_list),
                    "discard_tiles": list(player.discard_tiles),
                    "combination_tiles": list(player.combination_tiles),
                    "combination_masks": [list(mask) for mask in player.combination_masks],
                    "huapai_list": list(player.huapai_list),
                    "score": player.score,
                    "is_riichi": player.is_riichi,
                    "is_hu": player.is_hu,
                    "show_hand_draw_slot_active": player.show_hand_draw_slot_active,
                    "ready_qualification": player.ready_qualification,
                    "tag_list": list(player.tag_list),
                    "dingque_suit": player.dingque_suit,
                }
                for index, player in self.players.items()
            },
        }

    def apply_all(self, round_key: str) -> List[dict]:
        self.load_round(round_key)
        frames = [self.snapshot()]
        for raw in self.round_data.get("action_ticks") or []:
            tick = stringify_tick(raw)
            validate_kan_mo_gang_tick(tick, round_key)
            self.apply_tick(tick)
            frames.append(self.snapshot())
        return frames

    def goto_action(self, round_key: str, action_index: int) -> dict:
        self.load_round(round_key)
        ticks = self.round_data.get("action_ticks") or []
        safe = max(0, min(action_index, len(ticks)))
        for raw in ticks[:safe]:
            tick = stringify_tick(raw)
            validate_kan_mo_gang_tick(tick, round_key)
            self.apply_tick(tick)
        return self.snapshot()

    def _consume_wall(self, action: str) -> None:
        if self.is_taiwan():
            if not self.current_tiles_list:
                return
            if action == "d":
                self.current_tiles_list.pop(0)
                return
            if action in ("gd", "bd"):
                self.current_tiles_list.pop()
                if self.record_dead_wall_mode == "fixed_replacement_wall_16":
                    remaining = max(0, self.record_dead_wall_count - 1)
                    if len(self.current_tiles_list) > remaining:
                        remaining += 1
                    self.record_dead_wall_count = remaining
            return
        if not self.current_tiles_list:
            return
        draw_from_front = action == "d" or (
            action == "gd" and self.flags.kong_replacement_from_front
        )
        if not draw_from_front and action in ("gd", "bd"):
            if self.backward_tiles_type == "double" and len(self.current_tiles_list) > 1:
                remove_pos = len(self.current_tiles_list) - 2
            else:
                remove_pos = len(self.current_tiles_list) - 1
            self.current_tiles_list.pop(remove_pos)
            self.backward_tiles_type = "single" if self.backward_tiles_type == "double" else "double"
        else:
            self.current_tiles_list.pop(0)

    def _apply_score_array(self, changes: Optional[List[int]]) -> None:
        if not changes or len(changes) < 4:
            return
        for player in self.players.values():
            player.score += int(changes[player.player_index])

    def _build_jiagang_mask(self, player: RecordPlayer, jiagang_tile: int, actual_jia: int) -> None:
        if self.flags.jiagang_extends_last_meld and player.combination_masks:
            player.combination_masks[-1] = list(player.combination_masks[-1]) + [3, actual_jia]
            return
        idx = codec.find_combination_index(player.combination_tiles, f"k{codec.normalize_tile(jiagang_tile)}")
        if idx >= 0:
            player.combination_tiles[idx] = f"g{codec.normalize_tile(jiagang_tile)}"
            mask = list(player.combination_masks[idx])
            for j, value in enumerate(mask):
                if value == 1:
                    mask.insert(j, actual_jia)
                    mask.insert(j, 3)
                    break
            player.combination_masks[idx] = mask
            return
        player.combination_tiles.append(f"g{jiagang_tile}")
        player.combination_masks.append([0, jiagang_tile, 3, actual_jia, 1, jiagang_tile, 0, jiagang_tile])

    def _remove_claimed_discard(self, claimed_tile: int) -> None:
        if self.last_discard_player_index < 0:
            return
        river = self.players[self.last_discard_player_index].discard_tiles
        if not river:
            return
        if river[-1] == claimed_tile:
            river.pop()
            return
        for i in range(len(river) - 1, -1, -1):
            if river[i] == claimed_tile:
                river.pop(i)
                return

    def _maybe_infer_dingque(self, player: RecordPlayer) -> None:
        if not self.flags.infers_dingque_from_discards or player.dingque_suit != 0:
            return
        missing_suit = 0
        missing_count = 0
        for suit in (1, 2, 3):
            if not _player_has_suit(player, suit):
                missing_count += 1
                missing_suit = suit
        if missing_count == 1:
            player.dingque_suit = missing_suit

    def apply_tick(self, tick: Sequence[str]) -> None:
        if not tick:
            return
        action = tick[0]
        if action == "reset":
            self.current_player_index = codec.parse_tick_int(tick, 1)
            return
        if action in SKIP_ACTIONS:
            return
        if self.is_taiwan():
            self._apply_taiwan_wall_action(action)
            self._restore_taiwan_robbed_jiagang(tick)
        acting = resolve_acting_player(tick, action, self.current_player_index)
        player = self.players[acting]
        next_player = self.current_player_index

        if self.is_taiwan() and action == "state":
            if len(tick) >= 4 and tick[1] == "ready" and len(tick) >= 5:
                qualification = tick[3]
                player.ready_qualification = None if qualification == "none" else qualification
                declared = str(tick[4]).upper() == "T"
                if declared and "declared_ready" not in player.tag_list:
                    player.tag_list.append("declared_ready")
                elif not declared and "declared_ready" in player.tag_list:
                    player.tag_list.remove("declared_ready")
            elif len(tick) >= 4 and tick[1] == "water":
                blocked = str(tick[3]).upper() == "T"
                if blocked:
                    self.record_taiwan_ron_blocked.add(acting)
                else:
                    self.record_taiwan_ron_blocked.discard(acting)
            self.current_player_index = next_player
            return

        if action in ("d", "gd", "bd"):
            player.tile_list.append(codec.parse_tick_int(tick, 1))
            player.show_hand_draw_slot_active = True
            self._consume_wall(action)
            next_player = acting
        elif action == "c":
            cut_tile = codec.parse_tick_int(tick, 1)
            is_moqie = parse_tick_bool(tick, 2)
            is_horizontal = len(tick) > 3 and tick[3] == "H"
            if is_moqie and player.tile_list and player.tile_list[-1] == cut_tile:
                player.tile_list.pop()
            elif cut_tile in player.tile_list:
                player.tile_list.remove(cut_tile)
            player.show_hand_draw_slot_active = False
            player.discard_tiles.append(cut_tile)
            player.discard_is_moqie.append(is_moqie)
            player.discard_riichi_flags.append(is_horizontal)
            self.last_discard_player_index = acting
            self.last_discard_tile_id = cut_tile
            self.last_winnable_tile_id = cut_tile
            self.last_jiagang_player_index = -1
            next_player = (acting + 1) % 4
            self._maybe_infer_dingque(player)
        elif action == "bh":
            buhua_tile = codec.parse_tick_int(tick, 1)
            is_mo = parse_buhua_mo_flag(tick)
            if is_mo and player.tile_list and player.tile_list[-1] == buhua_tile:
                player.tile_list.pop()
                player.show_hand_draw_slot_active = False
            elif buhua_tile in player.tile_list:
                player.tile_list.remove(buhua_tile)
            self._apply_buhua_ownership(tick, acting, buhua_tile)
            next_player = acting
        elif action == "ag":
            angang_tile = codec.parse_tick_int(tick, 1)
            is_mo = parse_kan_mo_gang_flag(tick)
            removed = codec.resolve_angang_removed_tiles(tick, player.tile_list, angang_tile, is_mo)
            player.combination_tiles.append(f"G{angang_tile}")
            player.combination_masks.append(
                codec.build_angang_mask_from_removed(removed, self.rule_key())
            )
            if is_mo:
                player.show_hand_draw_slot_active = False
            self._apply_gang_score(tick)
            next_player = acting
        elif action == "jg":
            jiagang_tile = codec.parse_tick_int(tick, 1)
            is_mo = parse_kan_mo_gang_flag(tick)
            removed = codec.remove_n_by_normalized(player.tile_list, jiagang_tile, 1, prefer_draw_slot_first=is_mo)
            actual = removed[0] if removed else jiagang_tile
            self.last_winnable_tile_id = actual
            self.last_jiagang_player_index = acting
            self._build_jiagang_mask(player, jiagang_tile, actual)
            if is_mo:
                player.show_hand_draw_slot_active = False
            self._apply_gang_score(tick)
            next_player = acting
        elif action in ("cl", "cm", "cr", "p", "g"):
            ming = codec.parse_tick_int(tick, 1)
            removed = codec.resolve_hand_tiles(tick, action, ming)
            for tile_id in removed:
                if tile_id in player.tile_list:
                    player.tile_list.remove(tile_id)
            self.last_winnable_tile_id = -1
            self.last_jiagang_player_index = -1
            self._remove_claimed_discard(ming)
            discarder = self.last_discard_player_index if self.last_discard_player_index >= 0 else self.current_player_index
            relative = relative_position(acting, discarder)
            player.combination_tiles.append(codec.build_combination_target(action, ming))
            player.combination_masks.append(codec.build_mingpai_mask(action, ming, removed, relative))
            player.show_hand_draw_slot_active = False
            if action == "g":
                self._apply_gang_score(tick)
            next_player = acting
        elif action in ("hu_self", "hu_first", "hu_second", "hu_third"):
            hepai = codec.parse_tick_int(tick, 1)
            self.players[hepai].is_hu = True
            if action != "hu_self" and not hu_fan_contains_cuohe(tick) and not is_flower_win(tick, self.rule_key()):
                win_tile = try_parse_hepai_tile(tick, self.rule_key())
                if win_tile < 10:
                    win_tile = self.last_winnable_tile_id
                hu_player = self.players[hepai]
                if hu_player.show_hand_draw_slot_active and hu_player.tile_list:
                    hu_player.tile_list.pop()
                    hu_player.show_hand_draw_slot_active = False
                if win_tile >= 10 and needs_ron_tile(hu_player.tile_list, self.rule_key()):
                    hu_player.tile_list.append(win_tile)
            self._apply_score_array(parse_score_changes(tick, 4))
        elif action == "hu_riichi":
            hepai = codec.parse_tick_int(tick, 1)
            self.players[hepai].is_hu = True
            self._apply_score_array(parse_score_changes(tick, 6) if len(tick) > 6 else None)
            collected = codec.parse_tick_int(tick, 11) if len(tick) > 11 else 0
            if collected > 0:
                self.record_riichi_sticks = 0
        elif action == "ryuukyoku":
            self._apply_score_array(parse_score_changes(tick, 2))
        elif action == "shuhewei":
            changes = parse_score_changes(tick, 2)
            if changes and len(changes) >= 4:
                for player in self.players.values():
                    player.score += int(changes[player.original_player_index])
        elif action == "riichi":
            riichi_player = codec.parse_tick_int(tick, 1)
            self.players[riichi_player].is_riichi = True
            self.record_riichi_sticks += 1
        elif action == "dora":
            pass
        elif action == "gr":
            if len(tick) >= 6 and tick[1] == "gs":
                arr = [codec.parse_tick_int(tick, 2 + i) for i in range(4)]
                self._apply_score_array(arr)
        elif action == "liuju":
            if len(tick) >= 2 and self.is_sichuan_blood():
                step = tick[1]
                if step == "settle_hu":
                    self._apply_score_array(parse_score_changes(tick, 6))
                elif step == "chajiao":
                    self._apply_score_array(parse_score_changes(tick, 5))
                elif step == "cha_refund":
                    self._apply_score_array(parse_score_changes(tick, 2))
                elif step == "final" and len(tick) >= 3:
                    scores = parse_score_changes(tick, 2)
                    if scores:
                        for i, value in enumerate(scores[:4]):
                            self.players[i].score = int(value)
        elif action in ("jiuzhongjiupai", "end"):
            pass

        self.current_player_index = next_player

    def _apply_gang_score(self, tick: Sequence[str]) -> None:
        if not self.is_sichuan_blood():
            return
        text = list(tick)
        try:
            gs_index = len(text) - 1 - text[::-1].index("gs")
        except ValueError:
            return
        if gs_index + 4 >= len(text):
            return
        arr = [codec.parse_tick_int(text, gs_index + 1 + i) for i in range(4)]
        if any(arr):
            self._apply_score_array(arr)

    def _apply_buhua_ownership(self, tick: Sequence[str], action_player: int, published_tile: int) -> None:
        recipient = resolve_buhua_recipient(tick, action_player)
        transfer = try_resolve_buhua_transfer(tick)
        transfers_published = (
            transfer is not None
            and transfer[0] == action_player
            and transfer[1] == published_tile
        )
        initial = action_player if transfers_published else recipient
        self.players[initial].huapai_list.append(published_tile)
        if transfer is None:
            return
        from_player, transfer_tile = transfer
        if transfer_tile in self.players[from_player].huapai_list:
            self.players[from_player].huapai_list.remove(transfer_tile)
        self.players[recipient].huapai_list.append(transfer_tile)

    def _apply_taiwan_wall_action(self, action: str) -> None:
        if self.record_dead_wall_mode != "kong_expands_tail":
            return
        if action == "gd" and self.last_jiagang_player_index >= 0:
            self.record_dead_wall_count += 1
            self.last_jiagang_player_index = -1
            return
        if action not in ("ag", "g"):
            return
        established = sum(
            1
            for player in self.players.values()
            for code in player.combination_tiles
            if code and code[0] in ("g", "G")
        )
        if established >= 3:
            return
        self.record_dead_wall_count += 1

    def _restore_taiwan_robbed_jiagang(self, tick: Sequence[str]) -> None:
        if not tick or tick[0] not in ("hu_first", "hu_second", "hu_third"):
            return
        if self.last_jiagang_player_index < 0 or self.last_winnable_tile_id < 10:
            return
        fans = parse_hu_fan_list(tick, 3)
        if "错和" in fans or "抢杠" not in fans:
            return
        source = self.players[self.last_jiagang_player_index]
        key = f"g{codec.normalize_tile(self.last_winnable_tile_id)}"
        idx = codec.find_combination_index(source.combination_tiles, key)
        if idx < 0 or idx >= len(source.combination_masks) or not source.combination_masks[idx]:
            return
        mask = list(source.combination_masks[idx])
        robbed = codec.normalize_tile(self.last_winnable_tile_id)
        added = -1
        for i in range(0, len(mask) - 1, 2):
            if mask[i] == 3 and codec.normalize_tile(mask[i + 1]) == robbed:
                added = i
                break
        if added < 0:
            return
        del mask[added:added + 2]
        source.combination_tiles[idx] = f"k{codec.normalize_tile(self.last_winnable_tile_id)}"
        source.combination_masks[idx] = mask


_COMMENT_LINE = re.compile(r"//.*?$")


def load_jsonc(path) -> dict:
    text = path.read_text(encoding="utf-8")
    cleaned = []
    in_string = False
    escape = False
    for line in text.splitlines():
        out = []
        i = 0
        in_string = False
        escape = False
        while i < len(line):
            ch = line[i]
            if in_string:
                out.append(ch)
                if escape:
                    escape = False
                elif ch == "\\":
                    escape = True
                elif ch == '"':
                    in_string = False
                i += 1
                continue
            if ch == '"':
                in_string = True
                out.append(ch)
                i += 1
                continue
            if ch == "/" and i + 1 < len(line) and line[i + 1] == "/":
                break
            out.append(ch)
            i += 1
        cleaned.append("".join(out))
    return json.loads("\n".join(cleaned))
