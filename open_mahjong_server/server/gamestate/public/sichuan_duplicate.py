"""Durable per-table progress, optional board barriers, and score publication."""
import asyncio
from copy import deepcopy
import hashlib
import json
import secrets

from ...game_calculation.sichuan.duplicate_rules import make_board, score_board, score_totals, rule_profile, board_from_tiles, flatten_board, XUELIU_DUPLICATE


class PairedMatch:
    def __init__(self, snapshot, persist):
        self.data = deepcopy(snapshot)
        self.persist = persist
        self.lock = asyncio.Lock()
        self.events = {}
        self.start_gate = asyncio.Event()
        self.states = {}
        self.ready = {}
        self.board_ready = {}

    @property
    def completed(self):
        return self.data["status"] == "completed"

    @property
    def synchronous(self):
        # Existing saved matches retain their original board barrier.
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
        candidate["revision"] = self.data["revision"] + 1
        self.persist(candidate, self.data["revision"])
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

    def _pending_boards(self):
        if "pending_boards" in self.data:
            return deepcopy(self.data["pending_boards"])
        if not self.data.get("pending"):
            return {}
        entries = deepcopy(self.data["pending"])
        for table, entry in entries.items():
            entry["checkpoint"] = deepcopy(self.data.get("pending_checkpoints", {}).get(table))
        return {str(self.data["completed_boards"] + 1): entries}

    async def submit(self, table, number, scores, adjustments, record, checkpoint=None):
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
            pending_boards = self._pending_boards()
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
                                  "delta": delta, "digest": digest, "checkpoint": deepcopy(checkpoint)}
                candidate["pending_boards"] = pending_boards
                candidate.setdefault("table_completed_boards", {t: self.data["completed_boards"] for t in ("open", "closed")})[table] = number
                candidate.setdefault("table_checkpoints", {})[table] = deepcopy(checkpoint)
                if len(pending) == 2:
                    if number != candidate["completed_boards"] + 1:
                        raise ValueError("复式配对结算顺序不匹配")
                    result = score_board(pending["open"]["delta"], pending["closed"]["delta"],
                                         scoring=candidate["profile"]["scoring"], imp_scale=self.imp_scale,
                                         board_score_cap=candidate["profile"].get("board_score_cap"))
                    # Private checkpoint hands must never enter public results.
                    result.update(number=number, tables={t: {k: deepcopy(v) for k, v in entry.items() if k != "checkpoint"}
                                                       for t, entry in pending.items()})
                    candidate["results"].append(result)
                    candidate["scores"] = [candidate["scores"][i] + result["scores"][i] for i in range(2)]
                    candidate.update(score_totals(candidate["results"], imp_scale=self.imp_scale))
                    candidate["completed_boards"] = number
                    candidate["checkpoints"] = {t: deepcopy(entry["checkpoint"]) for t, entry in pending.items()}
                    del pending_boards[str(number)]
                    at_end = number >= candidate["planned_boards"]
                    tied = candidate["scores"][0] == candidate["scores"][1]
                    if at_end and (candidate["profile"]["tied_match"] == "draw" or not tied):
                        candidate["status"] = "completed"
                    elif at_end:
                        candidate["boards"].append(make_board(candidate["seed"], number + 1, layout=candidate["profile"].get("wall_layout", "standard")))
                next_pending = pending_boards.get(str(candidate["completed_boards"] + 1), {})
                candidate["pending"] = {t: {k: deepcopy(v) for k, v in entry.items() if k != "checkpoint"} for t, entry in next_pending.items()}
                candidate["pending_checkpoints"] = {t: deepcopy(entry["checkpoint"]) for t, entry in next_pending.items()}
                self.save(candidate)
            if self.data["completed_boards"] >= number:
                event.set()
        if must_wait:
            await event.wait()
        if self.data["status"] == "interrupted":
            raise RuntimeError("复式对局已中断，等待管理员处理")
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
            raise RuntimeError("复式对局已中断，等待管理员处理")

    def interrupt(self, reason):
        if self.data["status"] in ("completed", "interrupted", "cancelled"):
            return
        candidate = deepcopy(self.data)
        candidate.update(status="interrupted", interruption=str(reason))
        previous_revision = self.data["revision"]
        try:
            self.save(candidate)
        except BaseException:
            candidate["revision"] = previous_revision
            raise
        finally:
            self.data = candidate
            self.start_gate.set()
            for event in self.events.values():
                event.set()
            for state in list(self.states.values()):
                task = getattr(state, "game_task", None)
                if task and task is not asyncio.current_task() and not task.done():
                    task.cancel()

    def restart_unfinished_board(self):
        if self.data["status"] != "interrupted":
            raise ValueError("只能重开已中断的复式比赛")
        candidate = deepcopy(self.data)
        number = candidate["completed_boards"] + 1
        # Replace every potentially exposed unmatched board, including the fast
        # table's submitted boards; retain all durably paired settlements.
        last_exposed = min(len(candidate["boards"]), max(self.table_progress(t) for t in ("open", "closed")) + 1)
        pending = self._pending_boards()
        for n in range(number, last_exposed + 1):
            candidate.setdefault("voided_boards", []).append({"number": n,
                "board": candidate["boards"][n - 1], "pending": pending.get(str(n), {}),
                "reason": candidate.get("interruption", "interrupted")})
            if candidate.get("wall_source") != "training":
                candidate["boards"][n - 1] = make_board(secrets.token_hex(32), n, layout=candidate["profile"].get("wall_layout", "standard"))
        candidate.update(status="waiting", pending={}, pending_checkpoints={}, pending_boards={},
                         table_completed_boards={t: candidate["completed_boards"] for t in ("open", "closed")},
                         table_checkpoints=deepcopy(candidate.get("checkpoints", {})))
        self.save(candidate)
        self.ready.clear()
        self.states.clear()
        self.events.clear()
        self.board_ready.clear()
        self.start_gate.clear()


def new_match(match_id, sub_rule, room_ids, boards, seed, *, sync_mode=False):
    if type(sync_mode) is not bool:
        raise ValueError("复式同步模式必须为布尔值")
    profile = rule_profile(sub_rule)
    boards = [board_from_tiles(flatten_board(b), b["number"], layout=profile["wall_layout"]) for b in boards]
    return {"id": match_id, "profile": profile, "room_ids": room_ids, "sync_mode": sync_mode,
            "boards": deepcopy(boards), "seed": seed, "planned_boards": len(boards),
            "completed_boards": 0, "table_completed_boards": {"open": 0, "closed": 0},
            "scores": [0, 0], "small_scores": [0, 0], "imp_scores": [0, 0],
            "results": [], "pending": {}, "pending_boards": {},
            "status": "waiting", "revision": 0, "members": {}}
