"""In-process pairing of two independent table loops, boards and team results."""
import asyncio
from copy import deepcopy
import hashlib
import json

from ...records.format import ensure_board_instances

from ...game_calculation.sichuan.duplicate_rules import make_board, score_board, score_totals, rule_profile, board_from_tiles, flatten_board, XUELIU_DUPLICATE


class PairedMatch:
    def __init__(self, snapshot):
        self.data = deepcopy(snapshot)
        ensure_board_instances(self.data)
        self.record_inputs = {}
        self.record_commit_event = asyncio.Event()
        self.lock = asyncio.Lock()
        self.events = {}
        self.start_gate = asyncio.Event()
        self.final_record_event = asyncio.Event()
        self.states = {}
        self.ready = {}
        self.board_ready = {}

    @property
    def completed(self):
        return self.data["status"] == "completed"

    @property
    def synchronous(self):
        return self.data.get("sync_mode", True)

    @property
    def imp_scale(self):
        profile = self.data["profile"]
        return profile.get("imp_scale", "jj" if profile["sub_rule"] == XUELIU_DUPLICATE else "sbr")

    def table_progress(self, table):
        return max(self.data["completed_boards"], self.data.get("table_completed_boards", {}).get(table, 0))

    def board(self, number, table=None):
        progress = self.data["completed_boards"] if self.synchronous or table is None else self.table_progress(table)
        if self.data["status"] != "running" or number != progress + 1 or not 1 <= number <= len(self.data["boards"]):
            raise ValueError("复式副号未开放")
        return deepcopy(self.data["boards"][number - 1])

    def save(self, candidate):
        ensure_board_instances(candidate)
        candidate["revision"] = self.data["revision"] + 1
        self.data = candidate

    def public_info(self, table=None):
        info = {key: deepcopy(self.data[key]) for key in
                ("id", "status", "room_ids", "planned_boards", "completed_boards")}
        info.update(scoring=self.data["profile"]["scoring"], sync_mode=self.synchronous)
        if self.data.get("wall_source"):
            info["wall_source"] = self.data["wall_source"]
        info["ready_tables"] = sorted(self.ready)
        info["waiting_tables"] = sorted(self.data.get("pending", {})) if self.synchronous else []
        if table in ("open", "closed"):
            info["table_completed_boards"] = self.table_progress(table)
        if self.completed or (self.synchronous and self.data["completed_boards"] > 0):
            info.update(scores=list(self.data["scores"]), boards=deepcopy(self.data["results"]))
            info.update(score_totals(self.data["results"], imp_scale=self.imp_scale))
        elif not self.synchronous:
            # Do not reveal the other room's pace through paired-board progress.
            info["completed_boards"] = 0
        return info

    async def submit(self, table, number, scores, adjustments, record):
        if table not in ("open", "closed"):
            raise ValueError("复式桌号无效")
        if type(number) is not int or number < 1:
            raise ValueError("复式结果副号无效")
        if (not isinstance(scores, (list, tuple)) or not isinstance(adjustments, (list, tuple))
                or len(scores) != 2 or len(adjustments) != 2
                or any(type(n) is not int for n in [*scores, *adjustments])):
            raise ValueError("复式队分无效")
        delta = [scores[i] + adjustments[i] for i in range(2)]
        if sum(delta) != 0:
            raise ValueError("复式副分不守恒")
        digest = hashlib.sha256(json.dumps([number, scores, adjustments, record], sort_keys=True, ensure_ascii=False).encode()).hexdigest()
        async with self.lock:
            if number <= len(self.data["results"]):
                prior = self.data["results"][number - 1]["tables"][table]
                if prior["digest"] != digest:
                    raise ValueError("复式重复结果冲突")
                return self.public_info()
            pending_boards = deepcopy(self.data["pending_boards"])
            pending = pending_boards.setdefault(str(number), {})
            prior = pending.get(table)
            if prior and prior["digest"] != digest:
                raise ValueError("复式重复结果冲突")
            progress = self.table_progress(table)
            expected = self.data["completed_boards"] + 1 if self.synchronous else progress + 1
            if (self.data["status"] != "running" or number > len(self.data["boards"])
                    or (not prior and number != expected)):
                raise ValueError("复式结果副号或比赛状态不匹配")
            event = self.events.setdefault(number, asyncio.Event())
            must_wait = self.synchronous or number >= len(self.data["boards"])
            if not prior:
                candidate = deepcopy(self.data)
                pending[table] = {"scores": list(scores), "adjustments": list(adjustments),
                                  "delta": delta, "digest": digest}
                candidate["pending_boards"] = pending_boards
                candidate.setdefault("table_completed_boards", {t: self.data["completed_boards"] for t in ("open", "closed")})[table] = number
                if len(pending) == 2:
                    if number != candidate["completed_boards"] + 1:
                        raise ValueError("复式配对结算顺序不匹配")
                    result = score_board(pending["open"]["delta"], pending["closed"]["delta"],
                                         scoring=candidate["profile"]["scoring"], imp_scale=self.imp_scale,
                                         board_score_cap=candidate["profile"].get("board_score_cap"))
                    result.update(number=number, tables=pending)
                    candidate["results"].append(result)
                    candidate["scores"] = [candidate["scores"][i] + result["scores"][i] for i in range(2)]
                    candidate.update(score_totals(candidate["results"], imp_scale=self.imp_scale))
                    candidate["completed_boards"] = number
                    del pending_boards[str(number)]
                    at_end = number >= candidate["planned_boards"]
                    tied = candidate["scores"][0] == candidate["scores"][1]
                    if at_end and (candidate["profile"]["tied_match"] == "draw" or not tied):
                        candidate["status"] = "completed"
                    elif at_end:
                        candidate["boards"].append(make_board(candidate["seed"], number + 1, layout=candidate["profile"].get("wall_layout", "standard")))
                next_pending = pending_boards.get(str(candidate["completed_boards"] + 1), {})
                candidate["pending"] = next_pending
                self.save(candidate)
            if self.data["completed_boards"] >= number:
                event.set()
        if must_wait:
            await event.wait()
        if self.data["status"] == "interrupted":
            raise RuntimeError("复式对局已中断，本场已结束")
        return self.public_info()

    async def wait_ready(self, table, number):
        """Finish both comparison screens before starting the next sync board."""
        if not self.synchronous:
            return
        async with self.lock:
            event = self.events.setdefault(("ready", number), asyncio.Event())
            ready = self.board_ready.setdefault(number, set())
            ready.add(table)
            if len(ready) == 2:
                event.set()
        await event.wait()
        if self.data["status"] != "running":
            raise RuntimeError("复式对局已中断，本场已结束")

    def interrupt(self, reason):
        if self.data["status"] in ("completed", "interrupted", "cancelled"):
            return
        candidate = deepcopy(self.data)
        candidate.update(status="interrupted", interruption=str(reason))
        self.save(candidate)
        self.start_gate.set()
        self.final_record_event.set()
        self.record_commit_event.set()
        for event in self.events.values():
            event.set()
        for state in list(self.states.values()):
            task = getattr(state, "game_task", None)
            if task and task is not asyncio.current_task() and not task.done():
                task.cancel()


def new_match(match_id, sub_rule, room_ids, boards, seed, *, sync_mode=False):
    if type(sync_mode) is not bool:
        raise ValueError("复式同步模式必须为布尔值")
    profile = rule_profile(sub_rule)
    boards = [board_from_tiles(flatten_board(b), b["number"], layout=profile["wall_layout"]) for b in boards]
    data = {"id": match_id, "profile": profile, "room_ids": room_ids, "sync_mode": sync_mode,
            "boards": deepcopy(boards), "seed": seed, "planned_boards": len(boards),
            "completed_boards": 0, "table_completed_boards": {"open": 0, "closed": 0},
            "scores": [0, 0], "small_scores": [0, 0], "imp_scores": [0, 0],
            "results": [], "pending": {}, "pending_boards": {},
            "status": "waiting", "revision": 0, "members": {}}
    ensure_board_instances(data)
    return data
