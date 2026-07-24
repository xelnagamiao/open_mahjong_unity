"""
雀渣 (tziakcha) → salasasa 国标牌谱转换。

牌墙/动作/番种解码对齐 tziakcha.net/record 内联脚本与 /public/js 逻辑，
以及 tziakcha-fetcher 中同源的 decode 约定；不凭空猜测字段含义。
"""

from __future__ import annotations

import base64
import json
import re
import zlib
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple
from urllib.parse import parse_qs, urlparse
from urllib.request import Request, urlopen

# --- 与雀渣网页 FAN / S2O 常量一致（_tz_inline_1.js） ---

FAN_NAMES = [
    "无",
    "大四喜",
    "大三元",
    "绿一色",
    "九莲宝灯",
    "四杠",
    "连七对",
    "十三幺",
    "清幺九",
    "小四喜",
    "小三元",
    "字一色",
    "四暗刻",
    "一色双龙会",
    "一色四同顺",
    "一色四节高",
    "一色四步高",
    "一色四连环",
    "三杠",
    "混幺九",
    "七对",
    "七星不靠",
    "全双刻",
    "清一色",
    "一色三同顺",
    "一色三节高",
    "全大",
    "全中",
    "全小",
    "清龙",
    "三色双龙会",
    "一色三步高",
    "一色三连环",
    "全带五",
    "三同刻",
    "三暗刻",
    "全不靠",
    "组合龙",
    "大于五",
    "小于五",
    "三风刻",
    "花龙",
    "推不倒",
    "三色三同顺",
    "三色三节高",
    "无番和",
    "妙手回春",
    "海底捞月",
    "杠上开花",
    "抢杠和",
    "碰碰和",
    "混一色",
    "三色三步高",
    "五门齐",
    "全求人",
    "双暗杠",
    "双箭刻",
    "全带幺",
    "不求人",
    "双明杠",
    "和绝张",
    "箭刻",
    "圈风刻",
    "门风刻",
    "门前清",
    "平和",
    "四归一",
    "双同刻",
    "双暗刻",
    "暗杠",
    "断幺",
    "一般高",
    "喜相逢",
    "连六",
    "老少副",
    "幺九刻",
    "明杠",
    "缺一门",
    "无字",
    "边张",
    "嵌张",
    "单钓",
    "自摸",
    "花牌",
    "明暗杠",
    "天和",
    "地和",
    "人和Ⅰ",
    "人和Ⅱ",
]

# seat_index → original_player_index；与网页 S2O / fetcher SEAT_PLAYER_ORDERS 一致
S2O = [
    [0, 1, 2, 3],
    [1, 2, 3, 0],
    [2, 3, 0, 1],
    [3, 0, 1, 2],
    [1, 0, 3, 2],
    [0, 3, 2, 1],
    [3, 2, 1, 0],
    [2, 1, 0, 3],
    [2, 3, 1, 0],
    [3, 1, 0, 2],
    [1, 0, 2, 3],
    [0, 2, 3, 1],
    [3, 2, 0, 1],
    [2, 0, 1, 3],
    [0, 1, 3, 2],
    [1, 3, 2, 0],
]

ACTION_NONE = 0
ACTION_FLOWER = 1
ACTION_DISCARD = 2
ACTION_CHI = 3
ACTION_PENG = 4
ACTION_GANG = 5
ACTION_WIN = 6
ACTION_DRAW = 7
ACTION_PASS = 8
ACTION_ABANDON = 9

API_BASE = "https://tziakcha.net"


# ---------------------------------------------------------------------------
# 网络 / 解压（对齐 parse_script / _qry）
# ---------------------------------------------------------------------------

def parse_session_or_record_id(url_or_id: str) -> str:
    text = (url_or_id or "").strip()
    if not text:
        raise ValueError("空的牌谱 id / url")
    if "://" in text or text.startswith("/"):
        qs = parse_qs(urlparse(text).query)
        if "id" in qs and qs["id"]:
            return qs["id"][0]
        m = re.search(r"[?&]id=([A-Za-z0-9_-]+)", text)
        if m:
            return m.group(1)
        raise ValueError(f"无法从 URL 解析 id: {url_or_id}")
    return text


def _http_post(path: str, body: Optional[bytes] = None, retries: int = 6) -> dict:
    url = API_BASE + path
    last_err: Exception | None = None
    for attempt in range(1, retries + 1):
        try:
            req = Request(
                url,
                data=body if body is not None else b"",
                method="POST",
                headers={
                    "content-type": "application/x-www-form-urlencoded; charset=UTF-8",
                    "user-agent": "salasasa-tziakcha-converter/1.0",
                },
            )
            with urlopen(req, timeout=45) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except Exception as exc:  # noqa: BLE001 — 网络抖动重试
            last_err = exc
            import time

            time.sleep(1.2 * attempt)
    raise RuntimeError(f"请求失败 {path}: {last_err}")


def fetch_session(session_id: str) -> dict:
    return _http_post(f"/_qry/game/?id={session_id}")


def fetch_raw_record(record_id: str) -> dict:
    return _http_post("/_qry/record/", f"id={record_id}".encode("utf-8"))


def decompress_script(script: str) -> dict:
    """对齐网页 parse_script：atob → pako.inflate → JSON。"""
    raw = base64.b64decode(script)
    text = zlib.decompress(raw).decode("utf-8").replace("\0", "")
    return json.loads(text)


def load_decoded_record(path_or_obj: Any) -> dict:
    """接受文件路径、已解码 record({id,belongs,step})、或原始 API({script})。"""
    if isinstance(path_or_obj, (str, Path)):
        data = json.loads(Path(path_or_obj).read_text(encoding="utf-8"))
    else:
        data = path_or_obj
    if "step" in data and isinstance(data["step"], dict):
        return data
    if "script" in data:
        step = decompress_script(data["script"])
        return {
            "id": data.get("id"),
            "belongs": data.get("belongs"),
            "next": data.get("next"),
            "prev": data.get("prev"),
            "step": step,
        }
    if "w" in data and "a" in data:
        return {"id": None, "belongs": None, "step": data}
    raise ValueError("无法识别的雀渣牌谱结构")


# ---------------------------------------------------------------------------
# 牌编码：雀渣 0..143 → salasasa
# 对齐 TILE[]：m / s / p / ESW N / C(中) F(发) P(白) / 1f..8f
# salasasa：万11-19 饼21-29 条31-39 风41-44 中45 白46 发47 花51-58
# ---------------------------------------------------------------------------

def tz_tile_to_salasasa(tile_id: int) -> int:
    if tile_id < 0 or tile_id > 143:
        raise ValueError(f"非法雀渣牌 id: {tile_id}")
    if tile_id < 36:  # 万
        return 11 + (tile_id >> 2)
    if tile_id < 72:  # 条（雀渣顺序在饼前）
        return 31 + ((tile_id - 36) >> 2)
    if tile_id < 108:  # 饼
        return 21 + ((tile_id - 72) >> 2)
    if tile_id < 124:  # 东南西北
        return 41 + ((tile_id - 108) >> 2)
    if tile_id < 128:  # C 中
        return 45
    if tile_id < 132:  # F 发
        return 47
    if tile_id < 136:  # P 白
        return 46
    return 51 + (tile_id - 136)  # 花


def parse_wall_hex(wall_hex: str) -> List[int]:
    """对齐 parse_wall：144 字节 hex → 144 张牌 id。"""
    if not isinstance(wall_hex, str) or len(wall_hex) < 288:
        raise ValueError("step.w 必须是至少 288 字符的 hex 牌墙")
    out = []
    for i in range(144):
        out.append(int(wall_hex[i * 2 : i * 2 + 2], 16))
    return out


def parse_dice(encoded: int) -> List[int]:
    return [encoded & 15, (encoded >> 4) & 15, (encoded >> 8) & 15, (encoded >> 12) & 15]


def decode_action(tuple3: Sequence[int]) -> dict:
    """对齐 parse_acts / decodeTziakchaAction。"""
    combined, data, time_ms = tuple3
    return {
        "player": (combined >> 4) & 3,
        "type": combined & 15,
        "data": data,
        "time": time_ms,
    }


# ---------------------------------------------------------------------------
# 牌墙打断与发牌（对齐 fetcher setupWallAndDeal / 网页 dice+pick）
# ---------------------------------------------------------------------------

def rotate_wall_by_dice(wall: List[int], dice: List[int], dealer_index: int = 0) -> List[int]:
    wall_break_pos = (dealer_index - (dice[0] + dice[1] - 1) + 12) % 4
    start_pos = wall_break_pos * 36 + (dice[0] + dice[1] + dice[2] + dice[3]) * 2
    start_pos %= len(wall)
    return wall[start_pos:] + wall[:start_pos]


def deal_initial_hands(rotated_wall: List[int], dealer_index: int = 0) -> Tuple[List[List[int]], List[int]]:
    """返回 (hands[4], remaining_wall)。庄家 14 张，闲家 13 张。"""
    front = 0
    hands: List[List[int]] = [[], [], [], []]
    for _ in range(3):
        for offset in range(4):
            p = (dealer_index + offset) % 4
            for _ in range(4):
                hands[p].append(rotated_wall[front])
                front += 1
    for offset in range(4):
        p = (dealer_index + offset) % 4
        hands[p].append(rotated_wall[front])
        front += 1
    hands[dealer_index].append(rotated_wall[front])
    front += 1
    return hands, rotated_wall[front:]


def seats_from_round_index(round_i: int) -> List[int]:
    """salasasa seats[original] = player_index。"""
    mapping = S2O[round_i % len(S2O)]  # player_index → original
    seats = [0, 0, 0, 0]
    for player_index, original in enumerate(mapping):
        seats[original] = player_index
    return seats


def format_fan_list(yaku_entry: Optional[dict]) -> List[str]:
    """对齐网页 setup_stl：FAN[fn] + lo番 + ×(hi+1)。"""
    if not yaku_entry or not isinstance(yaku_entry, dict):
        return []
    table = yaku_entry.get("t") or {}
    cleaned: List[str] = []
    for key, packed in table.items():
        idx = int(key)
        mul = ((int(packed) >> 8) & 0xFF) + 1
        base = FAN_NAMES[idx] if 0 <= idx < len(FAN_NAMES) else f"番{idx}"
        # 网页「独听・边张」等；salasasa 国标短名与示例一致
        if base.startswith("独听・"):
            base = base.replace("独听・", "")
        if base.startswith("※ "):
            base = base.replace("※ ", "")
        if base == "花牌" or mul > 1:
            cleaned.append(f"{base}*{mul}")
        else:
            cleaned.append(base)
    return cleaned

def hu_class_for(winner: int, discarder: Optional[int]) -> str:
    if discarder is None or discarder == winner:
        return "hu_self"
    # discarder 相对 winner：上家 / 对家 / 下家
    delta = (discarder - winner) % 4
    if delta == 3:
        return "hu_first"  # 上家点炮
    if delta == 2:
        return "hu_second"
    if delta == 1:
        return "hu_third"
    return "hu_self"


def ms_to_datetime_str(ms: int) -> str:
    dt = datetime.fromtimestamp(ms / 1000.0, tz=timezone.utc).astimezone()
    return dt.strftime("%Y-%m-%d %H:%M:%S.%f")


# ---------------------------------------------------------------------------
# 手牌模拟（为吃碰杠写出真实 h1/h2/h3）
# ---------------------------------------------------------------------------

@dataclass
class PlayerState:
    hand: List[int] = field(default_factory=list)  # 雀渣原始 id
    last_draw: Optional[int] = None


@dataclass
class TableState:
    players: List[PlayerState]
    wall_front: List[int]
    wall_back: List[int]
    last_discard: Optional[int] = None
    last_discard_player: Optional[int] = None
    last_was_gang: bool = False
    last_was_flower: bool = False

    def remove_exact(self, player: int, tile_id: int) -> int:
        hand = self.players[player].hand
        if tile_id in hand:
            hand.remove(tile_id)
            return tile_id
        base = tile_id & ~3
        for t in list(hand):
            if (t & ~3) == base:
                hand.remove(t)
                return t
        raise RuntimeError(f"玩家{player}手牌无 {tile_id}")

    def remove_by_base(self, player: int, tile_base: int) -> int:
        """tile_base 为 pack_get_tile 风格（已 <<2 的底）。"""
        base = tile_base & ~3
        hand = self.players[player].hand
        for t in list(hand):
            if (t & ~3) == base:
                hand.remove(t)
                return t
        raise RuntimeError(f"玩家{player}手牌无牌型 {tile_base}")


def pack_get_tile(data: int) -> int:
    return (data & 0x3F) << 2


def pack_get_offer(data: int) -> int:
    return (data >> 6) & 3


def is_promoted_kong(data: int) -> bool:
    return (data & 0x0300) == 0x0300


# ---------------------------------------------------------------------------
# 单局转换
# ---------------------------------------------------------------------------

def convert_round_step(
    step: dict,
    *,
    round_ordinal: int = 1,
    record_id: Optional[str] = None,
) -> dict:
    wall = parse_wall_hex(step["w"]) if isinstance(step["w"], str) else list(step["w"])
    dice = parse_dice(step["d"]) if isinstance(step["d"], int) else list(step["d"])
    round_i = int(step.get("i") or 0)
    rotated = rotate_wall_by_dice(wall, dice, dealer_index=0)
    hands_tz, remain_tz = deal_initial_hands(rotated, dealer_index=0)

    # salasasa：发牌后、补花前手牌；tiles_list 为发牌后剩余牌山（摸牌顺序）
    p_tiles = {
        f"p{i}_tiles": [tz_tile_to_salasasa(t) for t in sorted(hands_tz[i])]
        for i in range(4)
    }
    tiles_list = [tz_tile_to_salasasa(t) for t in remain_tz]

    state = TableState(
        players=[PlayerState(hand=list(h), last_draw=None) for h in hands_tz],
        wall_front=list(remain_tz),
        wall_back=[],
    )
    # 逆向摸牌从牌山末尾取；与网页 pick_b 一致：从打断点另一侧取。
    # fetcher：backward 时 wallBackIndex -= 1。发牌后 remaining 末尾即岭上侧。
    back_tiles = list(remain_tz)  # 逻辑上从末尾 pop
    front_idx = 0

    actions = [decode_action(a) for a in step["a"]]
    ticks: List[list] = []

    g = step.get("g") or {}
    b = int(step.get("b") or 0)
    score_s = list(step.get("s") or [0, 0, 0, 0])
    yaku_list = step.get("y") or [{}, {}, {}, {}]
    if isinstance(yaku_list, dict):
        # 容错：偶发 dict
        yaku_list = [yaku_list.get(str(i), yaku_list.get(i, {})) for i in range(4)]

    winner_mask = b & 0x0F
    discarder_mask = (b >> 4) & 0x0F
    cuohe_mask = (b >> 8) & 0x0F

    def take_front() -> int:
        nonlocal front_idx
        if front_idx >= len(state.wall_front):
            raise RuntimeError("牌山正面已空")
        # wall_front 与 remain 同步：用索引跳过已从末尾取走的冲突较复杂，
        # 这里用显式列表操作。
        tile = state.wall_front.pop(0)
        return tile

    def take_back() -> int:
        if not state.wall_front:
            raise RuntimeError("牌山已空，无法逆向摸牌")
        return state.wall_front.pop()

    for ac in actions:
        typ = ac["type"]
        p = ac["player"]
        d = ac["data"]
        pl = state.players[p]

        if typ == ACTION_NONE:
            continue
        if typ in (ACTION_PASS, ACTION_ABANDON):
            continue

        if typ == ACTION_FLOWER:
            flower = ((d >> 8) & 0x0F) + 136
            drawn = d & 0xFF
            # 手中移除花，换上摸到的牌；网页一次动作含补花+摸牌
            actual_flower = state.remove_exact(p, flower)
            is_mo_bu = pl.last_draw == actual_flower
            ticks.append(
                ["bh", tz_tile_to_salasasa(actual_flower), p, "T" if is_mo_bu else "F"]
            )
            # 网页从背面取替换牌；drawn 已写在 data 里，信任脚本而非再推牌山
            if drawn in state.wall_front:
                state.wall_front.remove(drawn)
            elif state.wall_front and state.wall_front[-1] == drawn:
                state.wall_front.pop()
            else:
                # 仍信任 data 中的 drawn
                pass
            pl.hand.append(drawn)
            pl.last_draw = drawn
            ticks.append(["bd", tz_tile_to_salasasa(drawn), p])
            state.last_was_flower = True
            state.last_was_gang = False
            continue

        if typ == ACTION_DRAW:
            tile = d & 0xFF
            backward = bool(d & 0x0100)
            if tile in state.wall_front:
                # 尽量与牌山一致移除
                if backward and state.wall_front and state.wall_front[-1] == tile:
                    state.wall_front.pop()
                elif not backward and state.wall_front and state.wall_front[0] == tile:
                    state.wall_front.pop(0)
                else:
                    state.wall_front.remove(tile)
            pl.hand.append(tile)
            pl.last_draw = tile
            sala = tz_tile_to_salasasa(tile)
            if backward:
                if state.last_was_gang:
                    ticks.append(["gd", sala])
                elif state.last_was_flower:
                    ticks.append(["bd", sala, p])
                else:
                    ticks.append(["gd", sala])
            else:
                ticks.append(["d", sala])
            state.last_was_gang = False
            state.last_was_flower = False
            continue

        if typ == ACTION_DISCARD:
            tile = d & 0xFF
            hand_played = bool((d >> 8) & 1)  # 1=手打
            actual = state.remove_exact(p, tile)
            flag = "F" if hand_played else "T"
            ticks.append(["c", tz_tile_to_salasasa(actual), flag])
            state.last_discard = actual
            state.last_discard_player = p
            pl.last_draw = None
            state.last_was_gang = False
            state.last_was_flower = False
            continue

        if typ == ACTION_CHI:
            if not d:
                # 战术鸣牌申请未执行 / 仅宣告
                continue
            tl = pack_get_tile(d)
            offer = pack_get_offer(d)
            t0 = tl - 4 + ((d >> 10) & 3)
            t1 = tl + ((d >> 12) & 3)
            t2 = tl + 4 + ((d >> 14) & 3)
            if offer <= 1:
                code, hand_tiles = "cl", [t1, t2]
            elif offer == 2:
                code, hand_tiles = "cm", [t0, t2]
            else:
                code, hand_tiles = "cr", [t0, t1]
            removed = [state.remove_exact(p, t) for t in hand_tiles]
            called = state.last_discard
            if called is None:
                raise RuntimeError("吃牌时无上家弃牌")
            # 弃牌已在河，不从手牌删
            ticks.append(
                [
                    code,
                    tz_tile_to_salasasa(called),
                    p,
                    tz_tile_to_salasasa(removed[0]),
                    tz_tile_to_salasasa(removed[1]),
                ]
            )
            state.last_discard = None
            pl.last_draw = None
            continue

        if typ == ACTION_PENG:
            if not d:
                continue
            tl = pack_get_tile(d)
            removed = [state.remove_by_base(p, tl) for _ in range(2)]
            called = state.last_discard
            if called is None:
                raise RuntimeError("碰牌时无弃牌")
            ticks.append(
                [
                    "p",
                    tz_tile_to_salasasa(called),
                    p,
                    tz_tile_to_salasasa(removed[0]),
                    tz_tile_to_salasasa(removed[1]),
                ]
            )
            state.last_discard = None
            pl.last_draw = None
            continue

        if typ == ACTION_GANG:
            if not d:
                continue
            tl = pack_get_tile(d)
            if is_promoted_kong(d):
                actual = state.remove_by_base(p, tl)
                is_mo = pl.last_draw is not None and (pl.last_draw & ~3) == (actual & ~3)
                ticks.append(["jg", tz_tile_to_salasasa(actual), "T" if is_mo else "F"])
                state.last_was_gang = True
                pl.last_draw = None
                continue
            offer = pack_get_offer(d)
            if offer == 0:
                # 暗杠
                ids = [state.remove_by_base(p, tl) for _ in range(4)]
                is_mo = pl.last_draw is not None and (pl.last_draw & ~3) == (tl & ~3)
                ticks.append(
                    ["ag", tz_tile_to_salasasa(ids[0]), "T" if is_mo else "F"]
                    + [tz_tile_to_salasasa(x) for x in ids]
                )
            else:
                removed = [state.remove_by_base(p, tl) for _ in range(3)]
                called = state.last_discard
                if called is None:
                    raise RuntimeError("明杠时无弃牌")
                ticks.append(
                    [
                        "g",
                        tz_tile_to_salasasa(called),
                        p,
                        tz_tile_to_salasasa(removed[0]),
                        tz_tile_to_salasasa(removed[1]),
                        tz_tile_to_salasasa(removed[2]),
                    ]
                )
                state.last_discard = None
            state.last_was_gang = True
            pl.last_draw = None
            continue

        if typ == ACTION_WIN:
            # 终局和牌在收尾用 b/y/s 写出。
            # 仅错和（在 cuohe_mask、不在 winner_mask）按网页 init_r 罚分即时落库，无 end。
            is_cuohe = bool(cuohe_mask & (1 << p))
            is_winner = bool(winner_mask & (1 << p))
            if is_cuohe and not is_winner:
                z_share = bool(g.get("z", 1))
                changes = [0, 0, 0, 0]
                if z_share:
                    changes[p] = -30
                    for i in range(4):
                        if i != p:
                            changes[i] = 10
                else:
                    changes[p] = -40
                fans = format_fan_list(yaku_list[p] if p < len(yaku_list) else None)
                if "错和" not in fans:
                    fans = list(fans) + ["错和"]
                fan_total = int((yaku_list[p] or {}).get("f") or 0)
                discarder = state.last_discard_player
                hclass = hu_class_for(p, discarder)
                ticks.append([hclass, p, fan_total, fans, changes])
            continue

    # 终局和 / 流局
    if winner_mask:
        # 可能一炮多响
        discarder = None
        for i in range(4):
            if discarder_mask & (1 << i):
                discarder = i
                break
        for i in range(4):
            if not (winner_mask & (1 << i)):
                continue
            if cuohe_mask & (1 << i):
                continue  # 已作为错和写出
            fans = format_fan_list(yaku_list[i] if i < len(yaku_list) else None)
            fan_total = int((yaku_list[i] or {}).get("f") or 0)
            hclass = hu_class_for(i, discarder)
            # 多人和牌时 step.s 是整盘结算；单人和时直接用 s
            ticks.append([hclass, i, fan_total, fans, list(score_s)])
        ticks.append(["end"])
    else:
        ticks.append(["liuju"])
        ticks.append(["end"])

    round_obj = {
        "round_index": round_ordinal,
        "current_round": round_i + 1,
        "seats": seats_from_round_index(round_i),
        "dealer_index": 0,
        "start_player_index": 0,
        "p0_tiles": p_tiles["p0_tiles"],
        "p1_tiles": p_tiles["p1_tiles"],
        "p2_tiles": p_tiles["p2_tiles"],
        "p3_tiles": p_tiles["p3_tiles"],
        "tiles_list": tiles_list,
        "action_ticks": ticks,
    }
    if record_id:
        round_obj["tziakcha_record_id"] = record_id
    return round_obj


def build_game_title_from_session(
    session: dict,
    records: List[dict],
    *,
    source_url: Optional[str] = None,
) -> dict:
    players = session.get("players") or []
    first_step = records[0]["step"] if records else {}
    g = first_step.get("g") or {}
    start_ms = int(first_step.get("t") or session.get("start_time") or 0)
    last_acts = (records[-1]["step"].get("a") or []) if records else []
    last_dt = last_acts[-1][2] if last_acts else 0
    end_ms = start_ms + int(last_dt) if records else int(session.get("finish_time") or start_ms)

    # 以第一局东家座位顺序作为 original 0..3（S2O[0] 恒等；随机座位由后续 seats 表达）
    title = {
        "rule": "guobiao",
        "room_type": "custom",
        "sub_rule": "guobiao/standard",
        "commitment_hex": "0" * 64,
        "salt": "0" * 32,
        "max_round": max(1, (int(session.get("periods") or len(records)) + 3) // 4),
        "hepai_limit": int(g.get("l") or g.get("b") or 8),
        "open_cuohe": True,  # 雀渣配置含错和规则；具体局内以 tick 为准
        "tips": False,
        "is_player_set_random_seed": False,
        "show_moqie_hint": False,
        "start_time": ms_to_datetime_str(start_ms) if start_ms else None,
        "end_time": ms_to_datetime_str(end_ms) if end_ms else None,
        "master_seed_hex": str(first_step.get("r") or ""),
        "tziakcha_session_id": session.get("id"),
        "tziakcha_title": g.get("t") or session.get("title"),
        "tziakcha_cfg": g,
    }
    if source_url:
        title["source_url"] = source_url

    # original 玩家：优先用第一局 step.p 顺序
    p_round0 = first_step.get("p") or []
    entry_uids = []
    for i in range(4):
        src = p_round0[i] if i < len(p_round0) else (players[i] if i < len(players) else {})
        name = src.get("n") or src.get("name") or f"P{i}"
        # 雀渣 uid 为字符串短 id；salasasa 要 int —— 用稳定 hash 正数占位
        pid = src.get("i") or src.get("id") or name
        uid = _stable_uid(pid)
        entry_uids.append(uid)
        title[f"p{i}_uid"] = uid
        title[f"p{i}_name"] = name
        title[f"p{i}_tziakcha_id"] = pid
        title[f"p{i}_elo"] = src.get("e")
    title["player_entry_order"] = entry_uids
    return title


def _stable_uid(pid: Any) -> int:
    if isinstance(pid, int):
        return pid
    s = str(pid)
    h = 0
    for ch in s:
        h = (h * 131 + ord(ch)) & 0x7FFFFFFF
    return 900000000 + (h % 99999999)


def convert_session_payload(
    session: dict,
    decoded_records: List[dict],
    *,
    source_url: Optional[str] = None,
) -> dict:
    title = build_game_title_from_session(session, decoded_records, source_url=source_url)
    game_round = {}
    for idx, rec in enumerate(decoded_records, start=1):
        step = rec["step"]
        # 若 step.w 仍是 hex 字符串则直接用；若已是 list 则先编码回处理函数兼容
        if isinstance(step.get("w"), list):
            step = dict(step)
            step["w"] = "".join(f"{t:02x}" for t in step["w"])
        if isinstance(step.get("a"), list) and step["a"] and isinstance(step["a"][0], dict):
            # 已 parse_acts 的结构 → 还原为三元组
            step = dict(step)
            step["a"] = [[(x["p"] << 4) | x["a"], x["d"], x["t"]] for x in step["a"]]
        round_obj = convert_round_step(step, round_ordinal=idx, record_id=rec.get("id"))
        game_round[f"round_index_{idx}"] = round_obj
    return {"game_title": title, "game_round": game_round}


def convert_local_session_file(path: str | Path, source_url: Optional[str] = None) -> dict:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if "session" in data and "records" in data:
        return convert_session_payload(data["session"], data["records"], source_url=source_url or data.get("source_url"))
    # 单局
    rec = load_decoded_record(data)
    session = {
        "id": rec.get("belongs"),
        "title": (rec["step"].get("g") or {}).get("t"),
        "periods": 1,
        "players": rec["step"].get("p") or [],
        "start_time": rec["step"].get("t"),
        "finish_time": rec["step"].get("t"),
    }
    return convert_session_payload(session, [rec], source_url=source_url)


def fetch_and_convert(url_or_id: str) -> dict:
    rid = parse_session_or_record_id(url_or_id)
    # 先当 record 拉；belongs 即 session
    raw = fetch_raw_record(rid)
    decoded = load_decoded_record(raw)
    session_id = decoded.get("belongs") or rid
    session = fetch_session(session_id)
    record_ids = [r.get("i") or r.get("id") for r in session.get("records") or []]
    if not record_ids:
        record_ids = [rid]
    records = []
    for mid in record_ids:
        if mid == decoded.get("id"):
            records.append(decoded)
        else:
            records.append(load_decoded_record(fetch_raw_record(mid)))
    return convert_session_payload(
        session,
        records,
        source_url=url_or_id if "://" in url_or_id else f"{API_BASE}/record/?id={rid}",
    )


def main(argv: Optional[Sequence[str]] = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(description="雀渣牌谱 → salasasa 国标牌谱")
    parser.add_argument("input", help="雀渣 URL / record id / 本地 JSON（wtWnLbRT.json 或 2u8pwkTG.json）")
    parser.add_argument("-o", "--output", help="输出路径（默认 stdout / 同目录 .salasasa.json）")
    parser.add_argument("--fetch", action="store_true", help="强制从 tziakcha.net 拉取（input 为 id/url）")
    args = parser.parse_args(list(argv) if argv is not None else None)

    inp = args.input
    path = Path(inp)
    if path.exists():
        result = convert_local_session_file(path)
        default_out = path.with_suffix("").as_posix() + ".salasasa.json"
    elif args.fetch or "://" in inp or not path.suffix:
        result = fetch_and_convert(inp)
        rid = parse_session_or_record_id(inp)
        default_out = str(Path(__file__).resolve().parent / f"{rid}.salasasa.json")
    else:
        raise SystemExit(f"找不到输入: {inp}")

    out = args.output or default_out
    text = json.dumps(result, ensure_ascii=False, indent=2)
    Path(out).write_text(text, encoding="utf-8")
    rounds = len(result.get("game_round") or {})
    print(f"已写入 {out} （{rounds} 局）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
