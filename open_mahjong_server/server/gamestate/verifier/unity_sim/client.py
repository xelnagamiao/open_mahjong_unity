"""Unity 客户端组件模拟器：只消费 WS，字段与处理函数写入的状态同构。"""
from __future__ import annotations

from copy import deepcopy
from typing import Any, Callable, Dict, List, Optional, Sequence

from .labels import WINDS, build_action_buttons, display_text

POSITIONS = ("self", "right", "top", "left")
HAND_ALLOW = {
    "cut",
    "buhua",
    "hu_self",
    "hu_flower",
    "initial_hu",
    "sea_bottom",
    "buzhang",
    "angang",
    "jiagang",
    "jiuzhongjiupai",
    "riichi_cut",
    "pass",
}
OTHER_ALLOW = {
    "chi_left",
    "chi_mid",
    "chi_right",
    "peng",
    "gang",
    "hu",
    "hu_first",
    "hu_second",
    "hu_third",
    "pass",
    "force_pass",
}
TRANSFER_MELDS = {"chi_left", "chi_mid", "chi_right", "peng", "gang"}
TingpaiFn = Callable[[List[int], List[str]], Sequence[int]]


def parse_gamestate_type(msg_type: Optional[str]) -> tuple[Optional[str], Optional[str]]:
    if not msg_type:
        return None, None
    if msg_type.startswith("gamestate/"):
        parts = msg_type.split("/")
        if len(parts) >= 3:
            return parts[1], "/".join(parts[2:])
        if len(parts) == 2:
            return None, parts[1]
    return None, msg_type


def _as_list(value: Any) -> List[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return list(value)
    if isinstance(value, tuple):
        return list(value)
    return [value]


def _as_int(value: Any, default: int = 0) -> int:
    try:
        if value is None:
            return default
        return int(value)
    except (TypeError, ValueError):
        return default


def extract_tiles_from_mask(mask: Any) -> List[int]:
    if not mask:
        return []
    if isinstance(mask, list) and mask and isinstance(mask[0], list):
        tiles: List[int] = []
        for item in mask:
            tiles.extend(extract_tiles_from_mask(item))
        return tiles
    tiles = []
    seq = list(mask)
    for i in range(1, len(seq), 2):
        tile = _as_int(seq[i], 0)
        if tile > 0:
            tiles.append(tile)
    return tiles


def _empty_seat() -> Dict[str, Any]:
    return {
        "username": "",
        "user_id": 0,
        "score": 0,
        "wind": "",
        "hand_tiles_count": 0,
        "discard_tiles": [],
        "combination_tiles": [],
        "combination_masks": [],
        "huapai_list": [],
        "tag_list": [],
        "panel_text": "",
    }


class UnitySim:
    """模拟 GameStateNetworkManager 之后的组件树（本家视角）。"""

    def __init__(
        self,
        *,
        viewer_user_id: int = 101,
        tingpai_fn: Optional[TingpaiFn] = None,
        tips_enabled: bool = True,
    ):
        self.viewer_user_id = int(viewer_user_id)
        self.tingpai_fn = tingpai_fn
        self.tips_enabled = tips_enabled
        self.reset()

    def reset(self) -> None:
        self.network = {
            "last_type": None,
            "last_suffix": None,
            "last_ask": None,
            "last_do": None,
            "message_count": 0,
        }
        self.gsm = {
            "selfIndex": 0,
            "allowActionList": [],
            "selfHandTiles": [],
            "player_to_info": {pos: _empty_seat() for pos in POSITIONS},
            "LastAskActionTick": 0,
            "remainTiles": 0,
            "IsSelfActionRequired": False,
            "CurrentPlayer": None,
            "indexToPosition": {},
            "roomRule": "guobiao",
            "gamestateId": None,
            "lastCutCardID": 0,
            "lastDiscardPlayerPosition": None,
            "currentAskCutTileId": 0,
            "chiCandidates": {},
        }
        self.board = {
            "scores": {pos: 0 for pos in POSITIONS},
            "winds": {pos: "" for pos in POSITIONS},
            "remain_tiles": 0,
            "remain_text": "余:0",
            "round_text": "",
            "huang_tiao": None,
        }
        self.canvas = {
            "self_hand_slots": [],
            "panels": {pos: {"username": "", "score": 0, "text": ""} for pos in POSITIONS},
        }
        self.action_button = {"buttons": [], "visible": False}
        self.timer = {"remaining_time": 0, "running": False}
        self.action_display = {pos: "" for pos in POSITIONS}
        self.game3d = {
            pos: {
                "river": [],
                "melds": [],
                "hand_count": 0,
                "flowers": [],
            }
            for pos in POSITIONS
        }
        self.game3d["claim_glow"] = {"tile": 0, "on": False}
        self.tips = {"waiting_tiles": [], "visible": False, "hand": [], "combos": []}
        self.end_result = {
            "show_result": False,
            "hepai_player_index": None,
            "player_to_score": {},
            "hu_fan": None,
            "hu_class": None,
            "ready": {},
        }
        self.vote = {"active": False, "payload": None}

    def snapshot(self) -> Dict[str, Any]:
        return {
            "NetworkManagerSim": deepcopy(self.network),
            "GsmSim": deepcopy(self.gsm),
            "BoardCanvasSim": deepcopy(self.board),
            "GameCanvasSim": deepcopy(self.canvas),
            "ActionButtonSim": deepcopy(self.action_button),
            "TimerSim": deepcopy(self.timer),
            "ActionDisplaySim": deepcopy(self.action_display),
            "Game3DSim": deepcopy(self.game3d),
            "TipsSim": deepcopy(self.tips),
            "EndResultSim": deepcopy(self.end_result),
            "VoteSim": deepcopy(self.vote),
        }

    def apply(self, message: Dict[str, Any]) -> Dict[str, Any]:
        return apply_message(self, message)

    def pos_of(self, seat: int) -> str:
        mapping = self.gsm.get("indexToPosition") or {}
        key = seat if seat in mapping else str(seat)
        if key in mapping:
            return mapping[key]
        self_index = int(self.gsm.get("selfIndex") or 0)
        return POSITIONS[(int(seat) - self_index) % 4]

    def _seat_info(self, pos: str) -> Dict[str, Any]:
        info = self.gsm["player_to_info"].setdefault(pos, _empty_seat())
        return info

    def _set_index_map(self, self_index: int) -> None:
        self.gsm["selfIndex"] = int(self_index)
        mapping = {}
        for seat in range(4):
            mapping[seat] = POSITIONS[(seat - int(self_index)) % 4]
            mapping[str(seat)] = mapping[seat]
        self.gsm["indexToPosition"] = mapping

    def _sync_views(self) -> None:
        for pos in POSITIONS:
            info = self._seat_info(pos)
            self.board["scores"][pos] = info.get("score") or 0
            self.board["winds"][pos] = info.get("wind") or ""
            self.canvas["panels"][pos] = {
                "username": info.get("username") or "",
                "score": info.get("score") or 0,
                "text": f"{info.get('username') or ''} {info.get('score') or 0}".strip(),
            }
            info["hand_tiles_count"] = (
                len(self.gsm["selfHandTiles"]) if pos == "self" else int(info.get("hand_tiles_count") or 0)
            )
            self.game3d[pos]["river"] = list(info.get("discard_tiles") or [])
            self.game3d[pos]["melds"] = list(info.get("combination_tiles") or [])
            self.game3d[pos]["hand_count"] = info["hand_tiles_count"]
            self.game3d[pos]["flowers"] = list(info.get("huapai_list") or [])
        self.canvas["self_hand_slots"] = list(self.gsm["selfHandTiles"])
        remain = int(self.gsm.get("remainTiles") or 0)
        self.board["remain_tiles"] = remain
        self.board["remain_text"] = f"余:{remain}"
        self.board["huang_tiao"] = self.gsm.get("CurrentPlayer")

    def _clear_buttons(self) -> None:
        self.action_button = {"buttons": [], "visible": False}
        self.timer["running"] = False
        self.game3d["claim_glow"] = {"tile": 0, "on": False}

    def _set_buttons(self, actions: Sequence[str], *, kind: str) -> None:
        buttons = build_action_buttons(actions, kind=kind)
        self.action_button = {"buttons": buttons, "visible": bool(buttons)}

    def _show_display(self, pos: str, action: str) -> None:
        text = display_text(action)
        if text:
            self.action_display[pos] = text

    def _refresh_tips(self) -> None:
        if not self.tips_enabled or self.tingpai_fn is None:
            return
        hand = list(self.gsm["selfHandTiles"])
        combos = list(self._seat_info("self").get("combination_tiles") or [])
        waiting = sorted(self.tingpai_fn(hand, combos) or [])
        self.tips = {
            "waiting_tiles": waiting,
            "visible": bool(waiting),
            "hand": hand,
            "combos": combos,
        }

    def _hide_tips(self) -> None:
        self.tips["visible"] = False

    def _handle_game_start(self, message: Dict[str, Any]) -> None:
        info = message.get("game_info") or {}
        players = info.get("players_info") or []
        self_index = 0
        viewer = int(info.get("self_user_id") or self.viewer_user_id)
        for player in players:
            if int(player.get("user_id") or 0) == viewer:
                self_index = int(player.get("player_index") or 0)
                break
        if info.get("self_player_index") is not None:
            self_index = int(info.get("self_player_index"))
        self._set_index_map(self_index)
        self.gsm["roomRule"] = info.get("room_rule") or "guobiao"
        self.gsm["gamestateId"] = info.get("gamestate_id")
        self.gsm["remainTiles"] = _as_int(info.get("tile_count"), 0)
        self.gsm["IsSelfActionRequired"] = False
        self.gsm["allowActionList"] = []
        self._clear_buttons()
        self.end_result["show_result"] = False
        round_no = _as_int(info.get("current_round"), 1)
        self.board["round_text"] = f"第{round_no}局"
        for player in players:
            seat = _as_int(player.get("player_index"), 0)
            pos = self.pos_of(seat)
            dest = self._seat_info(pos)
            hand = _as_list(player.get("hand_tiles")) if player.get("hand_tiles") is not None else []
            dest.update(
                {
                    "username": player.get("username") or f"P{seat}",
                    "user_id": _as_int(player.get("user_id"), 0),
                    "score": _as_int(player.get("score"), 0),
                    "wind": WINDS.get(seat, ""),
                    "hand_tiles_count": _as_int(
                        player.get("hand_tiles_count"), len(hand)
                    ),
                    "discard_tiles": list(_as_list(player.get("discard_tiles"))),
                    "combination_tiles": list(_as_list(player.get("combination_tiles"))),
                    "combination_masks": list(_as_list(player.get("combination_mask"))),
                    "huapai_list": list(_as_list(player.get("huapai_list"))),
                    "tag_list": list(_as_list(player.get("tag_list"))),
                }
            )
            if pos == "self":
                self.gsm["selfHandTiles"] = list(hand)
                dest["hand_tiles_count"] = len(self.gsm["selfHandTiles"])
        current = info.get("current_player_index")
        if current is not None:
            self.gsm["CurrentPlayer"] = self.pos_of(int(current))
        self._sync_views()

    def _handle_ask_hand(self, message: Dict[str, Any]) -> None:
        info = message.get("ask_hand_action_info") or {}
        self.gsm["LastAskActionTick"] = _as_int(info.get("action_tick"), self.gsm["LastAskActionTick"])
        self.gsm["remainTiles"] = _as_int(info.get("remain_tiles"), self.gsm["remainTiles"])
        player_index = _as_int(info.get("player_index"), 0)
        pos = self.pos_of(player_index)
        remaining = _as_int(info.get("remaining_time"), 0)
        actions = [str(item) for item in _as_list(info.get("action_list"))]
        self.gsm["CurrentPlayer"] = pos
        if pos == "self":
            self.gsm["allowActionList"] = [item for item in actions if item in HAND_ALLOW]
            self.gsm["IsSelfActionRequired"] = True
            self._set_buttons(self.gsm["allowActionList"], kind="hand")
            self.timer = {"remaining_time": remaining, "running": True}
            self._hide_tips()
        else:
            self.gsm["allowActionList"] = []
            self.gsm["IsSelfActionRequired"] = False
            self._clear_buttons()
        self.network["last_ask"] = {"kind": "hand", "player_index": player_index, "actions": actions}
        self._sync_views()

    def _handle_ask_other(self, message: Dict[str, Any]) -> None:
        info = message.get("ask_other_action_info") or {}
        self.gsm["LastAskActionTick"] = _as_int(info.get("action_tick"), self.gsm["LastAskActionTick"])
        actions = [str(item) for item in _as_list(info.get("action_list"))]
        remaining = _as_int(info.get("remaining_time"), 0)
        cut_tile = _as_int(info.get("cut_tile"), 0)
        self.gsm["currentAskCutTileId"] = cut_tile
        self.gsm["chiCandidates"] = info.get("chi_candidates") or {}
        allow = [item for item in actions if item in OTHER_ALLOW]
        self.gsm["allowActionList"] = allow
        if allow:
            self.gsm["IsSelfActionRequired"] = True
            self._set_buttons(allow, kind="other")
            self.timer = {"remaining_time": remaining, "running": True}
            self.game3d["claim_glow"] = {"tile": cut_tile, "on": True}
        else:
            self.gsm["IsSelfActionRequired"] = False
            self._clear_buttons()
        self.network["last_ask"] = {"kind": "other", "actions": actions, "cut_tile": cut_tile}
        self._sync_views()

    def _remove_one(self, seq: List[int], tile: int) -> None:
        if tile in seq:
            seq.remove(tile)

    def _remove_claimed_discard(self, discarder_pos: str, tile: int) -> None:
        river = self._seat_info(discarder_pos).get("discard_tiles") or []
        if tile in river:
            idx = len(river) - 1 - river[::-1].index(tile)
            river.pop(idx)
        self._seat_info(discarder_pos)["discard_tiles"] = river

    def _handle_do_action(self, message: Dict[str, Any]) -> None:
        info = message.get("do_action_info") or {}
        actions = [str(item) for item in _as_list(info.get("action_list"))]
        actor = _as_int(info.get("action_player"), 0)
        pos = self.pos_of(actor)
        is_claim = bool(info.get("is_claim"))
        is_silent = bool(info.get("silent"))
        self.network["last_do"] = {
            "actions": actions,
            "action_player": actor,
            "is_claim": is_claim,
            "silent": is_silent,
        }
        if is_claim:
            for action in actions:
                if not is_silent:
                    self._show_display(pos, action)
            self._clear_buttons()
            return
        cut_tile = info.get("cut_tile")
        cut_tiles = _as_list(info.get("cut_tiles"))
        if not cut_tiles and cut_tile:
            cut_tiles = [cut_tile]
        deal_tile = info.get("deal_tile")
        deal_tiles = _as_list(info.get("deal_tiles"))
        if not deal_tiles and deal_tile:
            deal_tiles = [deal_tile]
        buhua_tile = info.get("buhua_tile")
        combination_target = info.get("combination_target") or ""
        combination_mask = info.get("combination_mask")
        cut_from_player = info.get("cut_from_player")

        for action in actions:
            if not is_silent:
                self._show_display(pos, action)
            if action in ("deal_tile", "deal_gang_tile", "deal_buhua_tile"):
                resolved = [int(item) for item in deal_tiles if item is not None]
                self.gsm["remainTiles"] = max(0, int(self.gsm["remainTiles"] or 0) - max(len(resolved), 1))
                if pos == "self":
                    self.gsm["selfHandTiles"].extend(resolved)
                else:
                    self._seat_info(pos)["hand_tiles_count"] = int(self._seat_info(pos)["hand_tiles_count"] or 0) + max(
                        len(resolved), 1
                    )
            elif action == "cut":
                resolved = [int(item) for item in cut_tiles if item is not None]
                claimed = int(cut_tile or (resolved[-1] if resolved else 0))
                self.gsm["lastCutCardID"] = claimed
                self.gsm["lastDiscardPlayerPosition"] = pos
                river = self._seat_info(pos).setdefault("discard_tiles", [])
                river.extend(resolved)
                if pos == "self":
                    for tile in resolved:
                        self._remove_one(self.gsm["selfHandTiles"], int(tile))
                else:
                    self._seat_info(pos)["hand_tiles_count"] = max(
                        0, int(self._seat_info(pos)["hand_tiles_count"] or 0) - len(resolved)
                    )
            elif action == "buhua":
                tile = _as_int(buhua_tile, 0)
                self._seat_info(pos).setdefault("huapai_list", []).append(tile)
                if pos == "self":
                    self._remove_one(self.gsm["selfHandTiles"], tile)
                else:
                    self._seat_info(pos)["hand_tiles_count"] = max(
                        0, int(self._seat_info(pos)["hand_tiles_count"] or 0) - 1
                    )
            elif action in TRANSFER_MELDS or action in ("angang", "jiagang", "buzhang"):
                info_seat = self._seat_info(pos)
                masks = info_seat.setdefault("combination_masks", [])
                if action == "jiagang" or (action == "buzhang" and str(combination_target).startswith("k")):
                    combos = info_seat.setdefault("combination_tiles", [])
                    target = str(combination_target)
                    g_combo = "g" + target[1:] if target else "g"
                    if target in combos:
                        idx = combos.index(target)
                        combos[idx] = g_combo
                        if idx < len(masks):
                            masks[idx] = list(combination_mask or masks[idx])
                        else:
                            masks.append(list(combination_mask or []))
                    else:
                        combos.append(g_combo)
                        masks.append(list(combination_mask or []))
                    if pos == "self":
                        tiles = extract_tiles_from_mask(combination_mask)
                        tile = tiles[-1] if tiles else _as_int(cut_tile, 0)
                        self._remove_one(self.gsm["selfHandTiles"], tile)
                    else:
                        info_seat["hand_tiles_count"] = max(0, int(info_seat["hand_tiles_count"] or 0) - 1)
                elif action == "angang" or (action == "buzhang" and str(combination_target).startswith("G")):
                    info_seat.setdefault("combination_tiles", []).append(combination_target)
                    masks.append(list(combination_mask or []))
                    removed = extract_tiles_from_mask(combination_mask)
                    if pos == "self":
                        for tile in removed or list(self.gsm["selfHandTiles"][-4:]):
                            self._remove_one(self.gsm["selfHandTiles"], int(tile))
                    else:
                        info_seat["hand_tiles_count"] = max(0, int(info_seat["hand_tiles_count"] or 0) - 4)
                else:
                    if cut_from_player is not None:
                        self._remove_claimed_discard(self.pos_of(int(cut_from_player)), _as_int(cut_tile, 0))
                    info_seat.setdefault("combination_tiles", []).append(combination_target)
                    masks.append(list(combination_mask or []))
                    removed = extract_tiles_from_mask(combination_mask)
                    if pos == "self":
                        for tile in removed:
                            self._remove_one(self.gsm["selfHandTiles"], int(tile))
                    else:
                        n = len(removed) if removed else (3 if action == "gang" else 2)
                        info_seat["hand_tiles_count"] = max(0, int(info_seat["hand_tiles_count"] or 0) - n)

        if any(action in TRANSFER_MELDS for action in actions):
            self.gsm["CurrentPlayer"] = pos
        if pos == "self":
            self.gsm["allowActionList"] = []
            self.gsm["IsSelfActionRequired"] = False
            self._clear_buttons()
            self.timer["running"] = False
            self._refresh_tips()
        self._sync_views()

    def _handle_show_result(self, message: Dict[str, Any]) -> None:
        info = message.get("show_result_info") or {}
        self.end_result = {
            "show_result": True,
            "hepai_player_index": info.get("hepai_player_index"),
            "player_to_score": dict(info.get("player_to_score") or {}),
            "hu_fan": info.get("hu_fan"),
            "hu_class": info.get("hu_class"),
            "hu_score": info.get("hu_score"),
            "ready": dict(self.end_result.get("ready") or {}),
        }
        scores = info.get("player_to_score") or {}
        for seat, score in scores.items():
            pos = self.pos_of(_as_int(seat, 0))
            self._seat_info(pos)["score"] = _as_int(score, 0)
        self.gsm["IsSelfActionRequired"] = False
        self._clear_buttons()
        self._sync_views()

    def _handle_ready(self, message: Dict[str, Any]) -> None:
        info = message.get("ready_status_info") or message.get("ask_hand_action_info") or {}
        ready_map = info.get("player_to_ready") or info.get("ready_map") or {}
        if ready_map:
            self.end_result["ready"] = {str(k): bool(v) for k, v in ready_map.items()}
        actions = [str(item) for item in _as_list(info.get("action_list"))]
        if "ready" in actions or message.get("type", "").endswith("ready_status"):
            if self.gsm.get("IsSelfActionRequired") or "ready" in actions:
                self.gsm["allowActionList"] = [item for item in actions if item in ("ready", "pass")]
                if "ready" in (info.get("action_list") or ["ready"]):
                    self.gsm["allowActionList"] = ["ready"]
                    self.gsm["IsSelfActionRequired"] = True
                    self._set_buttons(["ready"], kind="hand")
        self._sync_views()

    def _handle_vote(self, suffix: str, message: Dict[str, Any]) -> None:
        if suffix == "vote_end":
            self.vote = {"active": False, "payload": message}
            return
        self.vote = {"active": True, "payload": message.get("vote_info") or message}

    def _handle_game_end(self, _message: Dict[str, Any]) -> None:
        self.gsm["IsSelfActionRequired"] = False
        self._clear_buttons()


def apply_message(sim: UnitySim, message: Dict[str, Any]) -> Dict[str, Any]:
    payload = message if isinstance(message, dict) else {"raw": message}
    msg_type = payload.get("type")
    rule, suffix = parse_gamestate_type(msg_type)
    sim.network["last_type"] = msg_type
    sim.network["last_suffix"] = suffix
    sim.network["message_count"] = int(sim.network.get("message_count") or 0) + 1
    if suffix == "game_start":
        sim._handle_game_start(payload)
    elif suffix == "broadcast_hand_action":
        sim._handle_ask_hand(payload)
    elif suffix == "ask_other_action":
        sim._handle_ask_other(payload)
    elif suffix == "do_action":
        sim._handle_do_action(payload)
    elif suffix == "show_result":
        sim._handle_show_result(payload)
    elif suffix == "ready_status":
        sim._handle_ready(payload)
    elif suffix == "game_end":
        sim._handle_game_end(payload)
    elif msg_type in ("gamestate/vote_update", "gamestate/vote_end") or suffix in ("vote_update", "vote_end"):
        sim._handle_vote(suffix or msg_type.split("/")[-1], payload)
    elif rule and suffix:
        # 国标一期忽略定缺/立直/数和尾等规则专有后缀
        pass
    return sim.snapshot()
