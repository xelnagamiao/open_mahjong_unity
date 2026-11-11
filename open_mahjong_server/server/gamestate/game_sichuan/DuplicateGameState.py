"""Two explicit subrules; table-local Sichuan logic remains shared."""
from copy import deepcopy
import asyncio
import time

from .SichuanGameState import SichuanGameState
from .XueliuDoublesGameState import XueliuDoublesGameState
from ...game_calculation.sichuan.duplicate_rules import team_by_seat, flatten_board


COMPARISON_SECONDS = 8


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
        from ..public.game_record_manager import apply_game_title_end_fields
        checkpoint_record = deepcopy(self.game_record)
        apply_game_title_end_fields(self, checkpoint_record["game_title"])
        await self.duplicate_match.submit(self.duplicate_table, self.current_round,
                                         [changes[0], changes[1]], self.duplicate_adjustments,
                                         self.game_record["game_round"][f"round_index_{self.round_index}"],
                                         {"game_record": checkpoint_record, "scores": [p.score for p in self.player_list],
                                          "players": [{key: getattr(p, key) for key in (
                                              "user_id", "username", "score", "team_id", "original_player_index",
                                              "title_used", "profile_used", "character_used", "voice_used")} for p in self.player_list],
                                          "score_history": [list(p.score_history) for p in self.player_list],
                                          "round_number_history": [list(p.round_number_history) for p in self.player_list],
                                          "team_round_history": self.team_round_history})
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
            raise RuntimeError("复式对局已中断，等待管理员处理")
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
            raise RuntimeError("复式对局已中断，等待管理员处理")
        self.game_status = "round_settled"
        self.duplicate_waiting_for = None

    def _restore_match_checkpoint(self):
        data = self.duplicate_match.data
        if not data["completed_boards"]:
            return
        checkpoint = data.get("checkpoints", {}).get(self.duplicate_table)
        if not checkpoint:
            raise ValueError("缺少已完成副的恢复快照")
        self.game_record = deepcopy(checkpoint["game_record"])
        self.team_round_history = deepcopy(checkpoint["team_round_history"])
        for index, (player, score) in enumerate(zip(self.player_list, checkpoint["scores"])):
            player.score = score
            player.score_history = list(checkpoint.get("score_history", [[], [], [], []])[index])
            player.round_number_history = list(checkpoint.get("round_number_history", [[], [], [], []])[index])
        self.current_round = self.round_index = data["completed_boards"] + 1
        self.dealer_index = (self.current_round - 1) % 4

    def _after_record_saved(self, game_id):
        # Keep a durable final record even if the generic record table is
        # temporarily unavailable. Unlock still requires both linked records.
        candidate = deepcopy(self.duplicate_match.data)
        candidate.setdefault("final_records", {})[self.duplicate_table] = deepcopy(self.game_record)
        candidate.setdefault("record_ids", {})[self.duplicate_table] = game_id
        candidate.setdefault("final_players", {})[self.duplicate_table] = [
            {"user_id": p.user_id, "username": p.username, "score": p.score,
             "original_player_index": p.original_player_index, "rank": p.record_counter.rank_result}
            for p in self.player_list]
        self.duplicate_match.save(candidate)

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
        # The last board barrier is durable before either table announces
        # completion. Reuse its end timestamp so crash recovery is identical.
        self.game_record = deepcopy(data["checkpoints"][self.duplicate_table]["game_record"])
        scores = data["scores"]
        public_result = self.duplicate_match.public_info()
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
        # Rebuild the same final UI payload from durable checkpoints after a restart.
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
