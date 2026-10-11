"""Authoritative two-table barrier and exactly-once team settlement.

Only the coordinator owns cross-table data. Player responses contain progress
until the whole match has finished; full data remains behind the record lock.
"""
import asyncio
from copy import deepcopy
import hashlib
import json
import secrets

from ...game_calculation.sichuan.duplicate_rules import make_board, score_board, rule_profile, team_by_seat


class PairedMatch:
    def __init__(self, snapshot, persist):
        self.data = deepcopy(snapshot)
        self.persist = persist
        self.lock = asyncio.Lock()
        self.events = {}
        self.start_gate = asyncio.Event()
        self.states = {}
        self.ready = {}

    @property
    def completed(self):
        return self.data["status"] == "completed"

    def board(self, number):
        if self.data["status"] != "running" or number != self.data["completed_boards"] + 1:
            raise ValueError("复式副号未开放")
        return deepcopy(self.data["boards"][number - 1])

    def save(self, candidate):
        candidate["revision"] = self.data["revision"] + 1
        self.persist(candidate, self.data["revision"])
        self.data = candidate

    def public_info(self):
        info = {key: deepcopy(self.data[key]) for key in
                ("id", "status", "room_ids", "planned_boards", "completed_boards")}
        info["scoring"] = self.data["profile"]["scoring"]
        info["ready_tables"] = sorted(self.ready)
        info["waiting_tables"] = sorted(self.data.get("pending", {}))
        if self.completed:
            info.update(scores=list(self.data["scores"]), boards=deepcopy(self.data["results"]))
        return info

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
        if any(type(n) is not int for n in delta) or sum(delta) != 0:
            raise ValueError("复式副分不守恒")
        digest = hashlib.sha256(json.dumps([number, scores, adjustments, record], sort_keys=True, ensure_ascii=False).encode()).hexdigest()
        async with self.lock:
            # Completed result retries may be acknowledged, never counted twice.
            if 1 <= number <= len(self.data["results"]):
                prior = self.data["results"][number - 1]["tables"][table]
                if prior["digest"] != digest:
                    raise ValueError("复式重复结果冲突")
                return self.public_info()
            if self.data["status"] != "running" or number != self.data["completed_boards"] + 1:
                raise ValueError("复式结果副号或比赛状态不匹配")
            candidate = deepcopy(self.data)
            pending = candidate.setdefault("pending", {})
            if table in pending and pending[table]["digest"] != digest:
                raise ValueError("复式重复结果冲突")
            pending[table] = {"scores": list(scores), "adjustments": list(adjustments), "delta": delta, "digest": digest}
            candidate.setdefault("pending_checkpoints", {})[table] = deepcopy(checkpoint)
            event = self.events.setdefault(number, asyncio.Event())
            if len(pending) == 2:
                result = score_board(pending["open"]["delta"], pending["closed"]["delta"], scoring=candidate["profile"]["scoring"])
                result.update(number=number, tables=deepcopy(pending))
                candidate["results"].append(result)
                candidate["scores"] = [candidate["scores"][i] + result["scores"][i] for i in range(2)]
                candidate["completed_boards"] = number
                candidate["checkpoints"] = candidate.pop("pending_checkpoints")
                candidate["pending"] = {}
                at_end = number >= candidate["planned_boards"]
                tied = candidate["scores"][0] == candidate["scores"][1]
                if at_end and (candidate["profile"]["tied_match"] == "draw" or not tied):
                    candidate["status"] = "completed"
                elif at_end:
                    candidate["boards"].append(make_board(candidate["seed"], number + 1))
            self.save(candidate)
            if self.data["completed_boards"] >= number:
                event.set()
        await event.wait()
        if self.data["status"] == "interrupted":
            raise RuntimeError("复式对局已中断，等待管理员处理")
        return self.public_info()

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
            # Even a database outage must stop both live tables. The old durable
            # 'running' snapshot is recovered as interrupted on the next startup.
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
        candidate.setdefault("voided_boards", []).append({"number": number,
            "board": candidate["boards"][number - 1], "pending": candidate.get("pending", {}),
            "reason": candidate.get("interruption", "interrupted")})
        candidate["boards"][number - 1] = make_board(secrets.token_hex(32), number)
        candidate.update(status="waiting", pending={}, pending_checkpoints={})
        self.save(candidate)
        self.ready.clear()
        self.states.clear()
        self.events.clear()
        self.start_gate.clear()


def new_match(match_id, sub_rule, room_ids, boards, seed):
    return {"id": match_id, "profile": rule_profile(sub_rule), "room_ids": room_ids,
            "boards": deepcopy(boards), "seed": seed, "planned_boards": len(boards),
            "completed_boards": 0, "scores": [0, 0], "results": [], "pending": {},
            "status": "waiting", "revision": 0, "members": {}}
