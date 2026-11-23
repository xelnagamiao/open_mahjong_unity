"""Two explicit subrules; table-local Sichuan logic remains shared."""
from copy import deepcopy
import asyncio
import time
import logging
from types import SimpleNamespace

from psycopg2 import InterfaceError, OperationalError

from .SichuanGameState import SichuanGameState
from .XueliuDoublesGameState import XueliuDoublesGameState
from ...database.pool_retry import is_pool_exhausted
from ...game_calculation.sichuan.duplicate_rules import team_by_seat, flatten_board


COMPARISON_SECONDS = 8
logger = logging.getLogger(__name__)
_pending_writes = {}


def wall_publication_pending(wall_id):
    return wall_id in _pending_writes.values()


def _can_retry_publication(error):
    return isinstance(error, (InterfaceError, OperationalError)) or is_pool_exhausted(error)


async def retry_publication(store, match, tables):
    # A transient storage outage must not keep the lobby or either GameState alive.
    delay = 2
    while True:
        await asyncio.sleep(delay)
        try:
            await asyncio.to_thread(store, match, tables)
            return
        except Exception as error:
            if not _can_retry_publication(error):
                logger.exception("复式终局牌谱重试失败 match_id=%s", match["id"])
                return
            delay = min(60, delay * 2)


class DuplicateMixin:
    def __init__(self, game_server, room_data, *args):
        super().__init__(game_server, room_data, *args)
        self.duplicate_match = game_server.gamestate_manager.sichuan_duplicate_matches[room_data["duplicate_match_id"]]
        self.duplicate_table = room_data["duplicate_table"]
        self.duplicate_match.states[self.duplicate_table] = self
        self.duplicate_key = room_data.get("duplicate_key")
        self.wall_source = self.duplicate_match.data.get("wall_source", self.duplicate_match.data.get("wall_type", "key"))
        self.duplicate_wall_type = self.duplicate_match.data.get("wall_type", "key")
        self.duplicate_round_count = self.duplicate_match.data["planned_boards"]
        self._duplicate_seed = self.duplicate_match.data["seed"]
        if self.wall_source == "training":
            self.room_random_seed = int(self._duplicate_seed, 16)
        self._duplicate_round_tiles = [flatten_board(board) for board in self.duplicate_match.data["boards"]]
        self._duplicate_tiles = self._duplicate_round_tiles[0]
        self._duplicate_game_id = room_data.get("duplicate_game_id")
        self.duplicate_adjustments = [0, 0]
        self.duplicate_last_draw = None
        self.spectator_enabled = self.spectator_enabled and self.wall_source == "training"
        self.spectator_manager.enabled = self.spectator_enabled

    async def run_game_loop(self):
        await self.duplicate_match.start_gate.wait()
        if self.duplicate_match.data["status"] != "running":
            return
        try:
            await self.game_loop_sichuan()
        except BaseException:
            self.duplicate_match.interrupt("table_interrupted")
            raise

    def _shuffle_players(self, rng):
        expected = team_by_seat(self.duplicate_table)
        if [p.team_id for p in self.player_list] != expected:
            raise ValueError("开闭室队伍必须交叉落位，且搭档相对")

    def _record_rule_profile(self):
        super()._record_rule_profile()
        title = self.game_record["game_title"]
        title["real_time"] = self.real_time
        title["tactical_grace_seconds"] = self.tactical_grace_seconds
        title["duplicate_profile"] = deepcopy(self.duplicate_match.data["profile"])
        title["duplicate_match_id"] = self.duplicate_match.data["id"]
        title["duplicate_table"] = self.duplicate_table
        title["duplicate_sync_mode"] = self.duplicate_match.synchronous
        title["wall_source"] = self.wall_source
        title["doubles_rule_profile"].update(self.duplicate_match.data["profile"])

    def _reset_round_state(self):
        super()._reset_round_state()
        self.duplicate_adjustments = [0, 0]
        self._duplicate_comparison_deadline = None
        self._duplicate_comparison_initial_ms = None

    async def _after_round_settled(self):
        waits = self.duplicate_match.synchronous or self.current_round >= len(self.duplicate_match.data["boards"])
        self.game_status = "duplicate_wait" if waits else "round_settled"
        self.duplicate_waiting_for = ("board" if self.duplicate_match.synchronous else "match") if waits else None
        self.action_dict = {i: [] for i in range(4)}
        self.waiting_players_list = []
        changes = self.team_round_history[-1]["score_changes"]
        self.team_round_history[-1]["pair_adjustments"] = list(self.duplicate_adjustments)
        from .boardcast import broadcast_duplicate_status
        await broadcast_duplicate_status(self)
        await self.duplicate_match.submit(self.duplicate_table, self.current_round,
                                         [changes[0], changes[1]], self.duplicate_adjustments,
                                         self.game_record["game_round"][f"round_index_{self.round_index}"])
        self.game_status = "round_settled"
        self.duplicate_waiting_for = None
        await broadcast_duplicate_status(self)
        if self.duplicate_match.synchronous and self.duplicate_match.completed:
            # The game loop skips its ordinary ready phase on the final board.
            # Its local settlement and paired comparison still have both phases.
            await self._ready_phase(liuju=self.ended_by == "liuju")

    async def _ready_phase(self, liuju):
        if not self.duplicate_match.synchronous:
            return await super()._ready_phase(liuju)
        from ..public.ready_phase import run_result_ready_phase
        from .boardcast import broadcast_ready_status, broadcast_duplicate_status
        from .board_settlement import ready_seconds
        await run_result_ready_phase(self, ready_seconds(self), broadcast_ready_status)
        await self._comparison_phase()
        if self.duplicate_match.completed:
            return
        self.game_status = "duplicate_wait"
        self.duplicate_waiting_for = "ready"
        await broadcast_duplicate_status(self)
        await self.duplicate_match.wait_ready(self.duplicate_table, self.current_round)
        self.game_status = "round_settled"
        self.duplicate_waiting_for = None

    async def _comparison_phase(self):
        """Local ready skips only the timeline; every table gets its comparison."""
        from .boardcast import broadcast_duplicate_status
        if self.duplicate_match.data["status"] not in ("running", "completed"):
            raise RuntimeError("复式对局已中断，本场已结束")
        self.game_status = "duplicate_wait"
        self.duplicate_waiting_for = "comparison"
        self._duplicate_comparison_deadline = None
        self._duplicate_comparison_initial_ms = COMPARISON_SECONDS * 1000
        try:
            await broadcast_duplicate_status(self)
            # Socket backpressure must not spend the later recipients' display time.
            self._duplicate_comparison_deadline = time.monotonic() + COMPARISON_SECONDS
            self._duplicate_comparison_initial_ms = None
            await asyncio.sleep(COMPARISON_SECONDS)
        finally:
            self._duplicate_comparison_deadline = None
            self._duplicate_comparison_initial_ms = None
        if self.duplicate_match.data["status"] not in ("running", "completed"):
            raise RuntimeError("复式对局已中断，本场已结束")
        self.game_status = "round_settled"
        self.duplicate_waiting_for = None

    async def _store_final_record(self, match_type):
        from ..public.match_result import attach_match_result
        attach_match_result(self.game_record, self.player_list)
        pair = self.duplicate_match
        # Only final records and the settlement fields survive if storage needs a retry.
        players = [SimpleNamespace(**{key: getattr(player, key, None) for key in (
            "user_id", "username", "score", "original_player_index", "title_used", "character_used",
            "profile_used", "voice_used", "avatar_frame_used", "pt")},
            record_counter=SimpleNamespace(rank_result=player.record_counter.rank_result))
            for player in self.player_list]
        pair.record_inputs[self.duplicate_table] = (self.game_record, players, self.room_type, match_type)
        if len(pair.record_inputs) == 2:
            match = {key: pair.data[key] for key in ("id", "profile", "boards", "board_instances", "sync_mode",
                     "wall_id", "key", "table_record_ids", "wall_source") if key in pair.data}
            store = self.db_manager.store_sichuan_pair_records
            try:
                pair.data["record_ids"] = await asyncio.to_thread(store, match, pair.record_inputs) or {}
            except Exception as error:
                if not _can_retry_publication(error):
                    raise
                logger.exception("复式牌谱暂未写入，后台重试 match_id=%s", match["id"])
                retry = asyncio.create_task(retry_publication(store, match, deepcopy(pair.record_inputs)))
                _pending_writes[retry] = match.get("wall_id")
                retry.add_done_callback(_pending_writes.pop)
            finally:
                pair.record_commit_event.set()
        await pair.record_commit_event.wait()
        return (pair.data.get("record_ids") or {}).get(self.duplicate_table)

    def _after_record_saved(self, game_id):
        # The live spectator and the participant archive both need the two final tables.
        candidate = deepcopy(self.duplicate_match.data)
        candidate.setdefault("final_records", {})[self.duplicate_table] = deepcopy(self.game_record)
        candidate.setdefault("record_ids", {})[self.duplicate_table] = game_id
        candidate.setdefault("final_players", {})[self.duplicate_table] = [
            {"user_id": p.user_id, "username": p.username, "score": p.score,
             "original_player_index": p.original_player_index, "rank": p.record_counter.rank_result}
            for p in self.player_list]
        self.duplicate_match.save(candidate)
        if all(t in candidate.get("final_records", {}) for t in ("open", "closed")):
            self.duplicate_match.final_record_event.set()

    async def _wait_for_match_records(self):
        # Participant archives become a match once both final table payloads
        # exist. A failed sibling leaves an explicitly partial local archive.
        try:
            await asyncio.wait_for(self.duplicate_match.final_record_event.wait(), timeout=30)
        except asyncio.TimeoutError:
            pass

    def complete_spectator_record(self, record):
        # Pair totals and both-table progress live outside the generic tick log.
        for key in ("doubles_result", "duplicate_match", "duplicate_boards"):
            if key in self.game_record:
                record[key] = deepcopy(self.game_record[key])
        return record

    def _is_match_end(self):
        return self.duplicate_match.completed

    def _should_play_round(self):
        return not self.duplicate_match.completed

    def _decide_next_dealer(self):
        return self.current_round % 4

    def _finish_team_match(self):
        data = self.duplicate_match.data
        scores = data["scores"]
        public_result = self.duplicate_match.public_info()
        public_result["name"] = data.get("lobby", {}).get("name", "")
        winner = None if scores[0] == scores[1] else int(scores[1] > scores[0])
        self.team_result = {"scores": dict(enumerate(scores)), "winner_team_id": winner,
                            "small_scores": dict(enumerate(public_result["small_scores"])),
                            "imp_scores": dict(enumerate(public_result["imp_scores"])),
                            "reason": data["profile"]["scoring"], "match_points": {
                                t: .5 if winner is None else int(t == winner) for t in (0, 1)},
                            "extra_boards": max(0, data["completed_boards"] - data["planned_boards"])}
        self.game_record["doubles_result"] = deepcopy(self.team_result)
        self.game_record["game_title"]["doubles_result"] = deepcopy(self.team_result)
        self.game_record["duplicate_match"] = public_result
        self.game_record["duplicate_match"]["ready_tables"] = []
        self.game_record["duplicate_boards"] = deepcopy(data["boards"])
        self.game_record["game_title"]["duplicate_round_tiles"] = [flatten_board(b) for b in data["boards"]]
        for player in self.player_list:
            player.record_counter.rank_result = 1 if winner is None or player.team_id == winner else 2
        from .board_settlement import match_summary
        settlement = match_summary(self)
        if settlement is not None:
            self.game_record['game_title']['sichuan_match'] = settlement


class SbrDuplicateGameState(DuplicateMixin, SichuanGameState):
    def _active_flower_pig_score_changes(self, player_index):
        offender_team = self.player_list[player_index].team_id
        changes = {player_index: -12}
        changes.update({player.player_index: 6 for player in self.player_list
                        if player.team_id != offender_team})
        # 两位对手直接入账，队分由个人分汇总；不再额外添加 pair_adjustments。
        return changes


class XueliuDuplicateGameState(DuplicateMixin, XueliuDoublesGameState):
    pass
