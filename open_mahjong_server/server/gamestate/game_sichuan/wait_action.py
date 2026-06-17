"""四川麻将等待/处理玩家操作。

事件/优先级引擎沿用古典实现；动作处理替换为四川逻辑：
- 无吃牌；
- 切牌强制先打定缺花色；
- 刮风下雨（点杠/摸杠加杠/暗杠）即时收分并记录用于退税；
- 和牌（自摸/点炮/抢杠）不立即结束，交由主循环 settle_win 处理血战续打。
"""
import asyncio
import time
import logging
from .action_check import check_action_after_cut, check_action_jiagang, refresh_waiting_tiles
from .boardcast import broadcast_do_action, broadcast_ready_status
from ..public.game_record_manager import (
    player_action_record_cut, player_action_record_angang,
    player_action_record_jiagang, player_action_record_chipenggang,
)
from ..public.hand_action_notify import apply_player_cut
from ..public.hand_slot_utils import (
    clear_draw_slot, has_draw_slot, infer_bot_cut_class, normalize_tile,
    pick_timeout_discard_tile, remove_angang_tiles, remove_cut_tile, resolve_is_mo_gang,
)
from ..public.logic_common import get_index_relative_position

logger = logging.getLogger(__name__)


def _suit(tile: int) -> int:
    return tile // 10


def _enforce_dingque_first(player, tile_id: int):
    """若玩家手牌仍含定缺花色，强制返回一张定缺花色牌作为实际切牌（自动纠正非法切牌）。"""
    dingque = getattr(player, "dingque_suit", 0)
    if dingque not in (1, 2, 3):
        return tile_id
    if _suit(tile_id) == dingque:
        return tile_id
    dingque_tiles = [t for t in player.hand_tiles if _suit(t) == dingque]
    if dingque_tiles:
        return dingque_tiles[0]
    return tile_id


def _prepare_dingque_cut_action(player, action_data: dict):
    """定缺纠正须在 apply_player_cut 之前进行，避免先删错牌且无法回滚导致双删。

    返回 (用于 apply_player_cut 的 action_data, 是否发生定缺纠正)。
    纠正后 cutIndex 置空，由客户端按 cut_tile 的 tileId 删牌。
    """
    requested = action_data.get("TileId")
    corrected = _enforce_dingque_first(player, requested)
    if corrected == requested:
        return action_data, False
    out = dict(action_data)
    out["TileId"] = corrected
    draw_slot = has_draw_slot(player)
    out["cutClass"] = infer_bot_cut_class(
        player.hand_tiles, corrected, None, draw_slot=draw_slot,
    )
    out["cutIndex"] = None
    return out, True


async def wait_action(self):
    self.waiting_players_list = []
    used_time = 0

    for i in range(4):
        while not self.action_queues[i].empty():
            try:
                self.action_queues[i].get_nowait()
            except Exception:
                break

    for player_index, action_list in self.action_dict.items():
        if action_list:
            self.waiting_players_list.append(player_index)
            self.action_events[player_index].clear()

    player_index = None
    action_data = None
    action_type = None
    timeout_grace = 0 if self.game_status == "waiting_ready" else self.step_time

    while self.waiting_players_list and any(self.player_list[i].remaining_time + timeout_grace > used_time for i in self.waiting_players_list):
        task_list = []
        task_to_player = {}
        for waiting_player_index in self.waiting_players_list:
            action_task = asyncio.create_task(self.action_events[waiting_player_index].wait())
            task_list.append(action_task)
            task_to_player[action_task] = waiting_player_index
        timer_task = asyncio.create_task(asyncio.sleep(1))
        task_list.append(timer_task)

        time_start = time.time()
        done, pending = await asyncio.wait(task_list, return_when=asyncio.FIRST_COMPLETED)
        time_end = time.time()
        for task in pending:
            task.cancel()

        for task in done:
            if task == timer_task:
                used_time += 1
            else:
                temp_player_index = task_to_player[task]
                temp_action_data = await self.action_queues[temp_player_index].get()
                temp_action_type = temp_action_data.get("action_type")
                temp_action_data = dict(temp_action_data)
                used_time += time_end - time_start
                used_int_time = int(used_time)
                if timeout_grace > 0 and used_int_time >= timeout_grace:
                    self.player_list[temp_player_index].remaining_time -= (used_int_time - timeout_grace)
                self.action_dict[temp_player_index] = []
                if temp_player_index in self.waiting_players_list:
                    self.waiting_players_list.remove(temp_player_index)

                do_interrupt = True
                for check_player_index in self.waiting_players_list:
                    for action in self.action_dict[check_player_index]:
                        if self.action_priority[temp_action_type] < self.action_priority[action]:
                            do_interrupt = False

                if not action_data:
                    action_data = dict(temp_action_data)
                    action_type = temp_action_type
                    player_index = temp_player_index
                elif self.action_priority[temp_action_type] > self.action_priority[action_type]:
                    action_data = dict(temp_action_data)
                    action_type = temp_action_type
                    player_index = temp_player_index

                if do_interrupt:
                    self.waiting_players_list = []

    if self.waiting_players_list:
        for i in self.waiting_players_list:
            self.player_list[i].remaining_time = 0

    match self.game_status:
        case "waiting_hand_action":
            if action_data:
                if action_type == "cut":
                    player = self.player_list[player_index]
                    cut_action_data, dingque_corrected = _prepare_dingque_cut_action(player, action_data)
                    cut_result = await apply_player_cut(self, player_index, cut_action_data)
                    if cut_result is None:
                        return
                    tile_id, is_moqie, cut_tile_index = cut_result
                    if dingque_corrected:
                        cut_tile_index = None
                    self.player_list[player_index].discard_tiles.append(tile_id)
                    player_action_record_cut(self, cut_tile=tile_id, is_moqie=is_moqie)
                    if self.current_player_index == self.dealer_index:
                        self.xunmu += 1
                    await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                                              cut_tile=tile_id, cut_class=is_moqie, cut_tile_index=cut_tile_index)
                    refresh_waiting_tiles(self, self.current_player_index)
                    self.action_dict = check_action_after_cut(self, tile_id)
                    self.last_action_was_gang = False  # 杠上炮仅对杠后那一张弃牌生效，检查后清除
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut"
                    else:
                        self._clear_paofen_pending(self.current_player_index)
                        self.game_status = "deal_card"

                elif action_type == "angang":
                    angang_tile = normalize_tile(action_data.get("target_tile"))
                    player = self.player_list[self.current_player_index]
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(player.hand_tiles, angang_tile, draw_slot=draw_slot)
                    removed = remove_angang_tiles(player.hand_tiles, angang_tile, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    player.combination_tiles.append(f"G{angang_tile}")
                    add_mask = [2, removed[0], 2, removed[1], 2, removed[2], 2, removed[3]]
                    player.combination_mask.append(add_mask)
                    # 下雨2：暗杠收未和牌每人 2 分
                    gang_changes = self._record_gang_score(self.current_player_index, angang_tile, "xiayu2")
                    player_action_record_angang(self, angang_tile=angang_tile, is_mo_gang=is_mo_gang, combination_mask=add_mask,
                                                  gang_score_changes=gang_changes)
                    await broadcast_do_action(self, action_list=["angang"], action_player=self.current_player_index,
                                              combination_mask=add_mask, combination_target=f"G{angang_tile}",
                                              is_mo_gang=is_mo_gang, gang_score_changes=gang_changes, gang_score_type="xiayu2")
                    self.last_action_was_gang = True
                    self.game_status = "deal_card_after_gang"

                elif action_type == "jiagang":
                    jiagang_tile = normalize_tile(action_data.get("target_tile"))
                    player = self.player_list[self.current_player_index]
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(player.hand_tiles, jiagang_tile, draw_slot=draw_slot)
                    remove_cut_tile(player.hand_tiles, jiagang_tile, is_mo_gang, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    combination_index = -1
                    for idx, combo in enumerate(player.combination_tiles):
                        if combo == f"k{jiagang_tile}":
                            combination_index = idx
                            break
                    if combination_index >= 0:
                        for idx, mask in enumerate(player.combination_mask[combination_index]):
                            if mask == 1:
                                player.combination_mask[combination_index].insert(idx, jiagang_tile)
                                player.combination_mask[combination_index].insert(idx, 3)
                                break
                        player.combination_tiles.remove(f"k{jiagang_tile}")
                        player.combination_tiles.append(f"g{jiagang_tile}")
                    # 下雨1：摸牌加杠收未和牌每人 1 分；手牌加杠不收分（并清除待退税杠，避免杠上炮误退更早的刮风/下雨）
                    gang_changes = None
                    if is_mo_gang:
                        gang_changes = self._record_gang_score(self.current_player_index, jiagang_tile, "xiayu1")
                    else:
                        self._clear_paofen_pending(self.current_player_index)
                    player_action_record_jiagang(self, jiagang_tile=jiagang_tile, is_mo_gang=is_mo_gang,
                                                 gang_score_changes=gang_changes)
                    await broadcast_do_action(self, action_list=["jiagang"], action_player=self.current_player_index,
                                              combination_mask=player.combination_mask[combination_index] if combination_index >= 0 else None,
                                              combination_target=f"k{jiagang_tile}", is_mo_gang=is_mo_gang,
                                              gang_score_changes=gang_changes, gang_score_type="xiayu1" if is_mo_gang else None)
                    self.jiagang_tile = jiagang_tile
                    self.last_action_was_gang = True
                    self.action_dict = check_action_jiagang(self, jiagang_tile)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_qianggang"
                    else:
                        self.game_status = "deal_card_after_gang"
                    return

                elif action_type == "hu_self":
                    self.pending_win = {"type": "zimo", "discarder": None}
                    self.game_status = "settle_win"
                    return
                else:
                    logger.error(f"四川 waiting_hand_action 非法 action_type: {action_type}")
                    return
            else:
                # 超时自动出牌（强制定缺优先）
                player = self.player_list[self.current_player_index]
                draw_slot = has_draw_slot(player)
                is_moqie = draw_slot
                tile_id = player.hand_tiles[-1] if draw_slot else pick_timeout_discard_tile(player.hand_tiles)
                tile_id = _enforce_dingque_first(player, tile_id)
                if _suit(tile_id) == getattr(player, "dingque_suit", 0) and not (draw_slot and player.hand_tiles[-1] == tile_id):
                    is_moqie = False
                remove_cut_tile(player.hand_tiles, tile_id, is_moqie, draw_slot=draw_slot)
                clear_draw_slot(player)
                player.discard_tiles.append(tile_id)
                player_action_record_cut(self, cut_tile=tile_id, is_moqie=is_moqie)
                if self.current_player_index == self.dealer_index:
                    self.xunmu += 1
                await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                                          cut_tile=tile_id, cut_class=is_moqie)
                refresh_waiting_tiles(self, self.current_player_index)
                self.action_dict = check_action_after_cut(self, tile_id)
                self.last_action_was_gang = False
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut"
                else:
                    self._clear_paofen_pending(self.current_player_index)
                    self.game_status = "deal_card"
                return

        case "waiting_action_after_cut":
            tile_id = self.player_list[self.current_player_index].discard_tiles[-1]
            combination_mask = []
            combination_target = ""
            if action_data:
                refresh_waiting_tiles(self, player_index)
                if action_type == "peng":
                    if self.player_list[player_index].hand_tiles.count(tile_id) < 2:
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].combination_tiles.append(f"k{tile_id}")
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"k{tile_id}"
                    if relative_position == "left":
                        combination_mask = [1, tile_id, 0, tile_id, 0, tile_id]
                    elif relative_position == "right":
                        combination_mask = [0, tile_id, 0, tile_id, 1, tile_id]
                    elif relative_position == "top":
                        combination_mask = [0, tile_id, 1, tile_id, 0, tile_id]

                elif action_type == "gang":
                    if self.player_list[player_index].hand_tiles.count(tile_id) < 3:
                        self.game_status = "deal_card"
                        return
                    for _ in range(3):
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].combination_tiles.append(f"g{tile_id}")
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"g{tile_id}"
                    if relative_position == "left":
                        combination_mask = [1, tile_id, 0, tile_id, 0, tile_id, 0, tile_id]
                    elif relative_position == "right":
                        combination_mask = [0, tile_id, 0, tile_id, 0, tile_id, 1, tile_id]
                    elif relative_position == "top":
                        combination_mask = [0, tile_id, 1, tile_id, 0, tile_id, 0, tile_id]

                elif action_type == "hu":
                    self.pending_win = {"type": "ron", "discarder": self.current_player_index, "hepai_tile": tile_id}
                    self.player_list[self.current_player_index].discard_tiles.pop(-1)
                    self.game_status = "settle_win"
                    return

                if action_type in ("peng", "gang"):
                    discarder = self.current_player_index
                    self.player_list[discarder].discard_tiles.pop(-1)
                    self.player_list[discarder].discard_origin_tiles.append(tile_id)
                    self.player_list[player_index].combination_mask.append(combination_mask)
                    clear_draw_slot(self.player_list[player_index])
                    # 点炮者上一杠的杠上炮可能性消失：玩家被碰/杠走了牌
                    self._clear_paofen_pending(discarder)
                    self.current_player_index = player_index
                    gang_changes = None
                    if action_type == "gang":
                        gang_changes = self._record_gang_score(player_index, tile_id, "guafeng", payer_index=discarder)
                    player_action_record_chipenggang(self, action_type=action_type, mingpai_tile=tile_id,
                                                      action_player=player_index, combination_mask=combination_mask,
                                                      gang_score_changes=gang_changes)
                    if action_type == "gang":
                        # 刮风：点杠（明杠他人弃牌）收点杠者 2 分
                        await broadcast_do_action(self, action_list=["gang"], action_player=self.current_player_index,
                                                  combination_mask=combination_mask, combination_target=combination_target,
                                                  gang_score_changes=gang_changes, gang_score_type="guafeng")
                        self.last_action_was_gang = True
                        self.game_status = "deal_card_after_gang"
                    else:
                        await broadcast_do_action(self, action_list=[action_type], action_player=self.current_player_index,
                                                  combination_mask=combination_mask, combination_target=combination_target)
                        self.last_action_was_gang = False
                        self.game_status = "onlycut_after_action"
                    return

                if action_type == "pass":
                    self._clear_paofen_pending(self.current_player_index)
                    self.game_status = "deal_card"
                    return
            else:
                self._clear_paofen_pending(self.current_player_index)
                self.game_status = "deal_card"
                return

        case "onlycut_after_action":
            if action_data and action_type == "cut":
                player = self.player_list[self.current_player_index]
                cut_action_data, dingque_corrected = _prepare_dingque_cut_action(player, action_data)
                cut_result = await apply_player_cut(self, self.current_player_index, cut_action_data)
                if cut_result is None:
                    return
                tile_id, is_moqie, cut_tile_index = cut_result
                if dingque_corrected:
                    cut_tile_index = None
                self.player_list[self.current_player_index].discard_tiles.append(tile_id)
                player_action_record_cut(self, cut_tile=tile_id, is_moqie=is_moqie)
                self.last_action_was_gang = False
                await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                                          cut_tile=tile_id, cut_class=is_moqie, cut_tile_index=cut_tile_index)
                refresh_waiting_tiles(self, self.current_player_index)
                self.action_dict = check_action_after_cut(self, tile_id)
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut"
                else:
                    self.game_status = "deal_card"
                return
            else:
                player = self.player_list[self.current_player_index]
                tile_id = pick_timeout_discard_tile(player.hand_tiles)
                tile_id = _enforce_dingque_first(player, tile_id)
                remove_cut_tile(player.hand_tiles, tile_id, False, draw_slot=False)
                clear_draw_slot(player)
                player.discard_tiles.append(tile_id)
                player_action_record_cut(self, cut_tile=tile_id, is_moqie=False)
                self.last_action_was_gang = False
                await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                                          cut_tile=tile_id, cut_class=False)
                refresh_waiting_tiles(self, self.current_player_index)
                self.action_dict = check_action_after_cut(self, tile_id)
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut"
                else:
                    self.game_status = "deal_card"
                return

        case "waiting_action_qianggang":
            temp_jiagang_tile = self.jiagang_tile
            self.jiagang_tile = None
            if action_data and action_type == "hu":
                # 抢杠：加杠不成立 → 退回该加杠下雨分，杠回退为碰，并将牌判给抢杠者
                refund_changes = self._refund_last_gang(self.current_player_index, temp_jiagang_tile)
                jia_player = self.player_list[self.current_player_index]
                if f"g{temp_jiagang_tile}" in jia_player.combination_tiles:
                    jia_player.combination_tiles.remove(f"g{temp_jiagang_tile}")
                    jia_player.combination_tiles.append(f"k{temp_jiagang_tile}")
                    for mask in jia_player.combination_mask:
                        if 3 in mask:
                            i3 = mask.index(3)
                            del mask[i3:i3 + 2]
                            break
                self.pending_win = {
                    "type": "qianggang",
                    "discarder": self.current_player_index,
                    "hepai_tile": temp_jiagang_tile,
                    "gang_refund_changes": refund_changes,
                }
                self.game_status = "settle_win"
                return
            else:
                self.game_status = "deal_card_after_gang"
                return

        case "waiting_ready":
            if action_data and action_type == "ready":
                await broadcast_ready_status(self)
                return True
            for wait_player_index, wait_actions in self.action_dict.items():
                if "ready" in wait_actions:
                    self.action_dict[wait_player_index] = []
            await broadcast_ready_status(self)
            return False
