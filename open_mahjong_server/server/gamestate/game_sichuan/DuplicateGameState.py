"""Two explicit subrules; table-local Sichuan logic remains shared."""
from copy import deepcopy

from .SichuanGameState import SichuanGameState
from .XueliuDoublesGameState import XueliuDoublesGameState
from ...game_calculation.sichuan.duplicate_rules import team_by_seat


class DuplicateMixin:
    def __init__(self, game_server, room_data, *args):
        super().__init__(game_server, room_data, *args)
        self.duplicate_match = game_server.gamestate_manager.sichuan_duplicate_matches[room_data["duplicate_match_id"]]
        self.duplicate_table = room_data["duplicate_table"]
        self.duplicate_match.states[self.duplicate_table] = self
        self.duplicate_key = room_data["duplicate_key"]
        self.duplicate_wall_type = "key"
        self.duplicate_round_count = self.duplicate_match.data["planned_boards"]
        self._duplicate_seed = self.duplicate_match.data["seed"]
        self._duplicate_round_tiles = [
            [t for i in range(4) for t in board["hands"][i] + board["walls"][i]]
            for board in self.duplicate_match.data["boards"]]
        self._duplicate_tiles = self._duplicate_round_tiles[0]
        self._duplicate_game_id = room_data["duplicate_game_id"]
        self.duplicate_adjustments = [0, 0]
        self.duplicate_last_draw = None
        self.spectator_enabled = False
        self.spectator_manager.enabled = False

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
        title["duplicate_profile"] = deepcopy(self.duplicate_match.data["profile"])
        title["duplicate_match_id"] = self.duplicate_match.data["id"]
        title["duplicate_table"] = self.duplicate_table
        title["doubles_rule_profile"].update(self.duplicate_match.data["profile"])

    def _reset_round_state(self):
        super()._reset_round_state()
        self.duplicate_adjustments = [0, 0]

    def _active_flower_pig_penalty(self, player_index):
        self.duplicate_adjustments[1 - self.player_list[player_index].team_id] += 12
        return 12

    async def _after_round_settled(self):
        self.game_status = "duplicate_wait"
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
        await broadcast_duplicate_status(self)

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
        winner = None if scores[0] == scores[1] else int(scores[1] > scores[0])
        self.team_result = {"scores": dict(enumerate(scores)), "winner_team_id": winner,
                            "reason": data["profile"]["scoring"], "match_points": {
                                t: .5 if winner is None else int(t == winner) for t in (0, 1)},
                            "extra_boards": max(0, data["completed_boards"] - data["planned_boards"])}
        self.game_record["doubles_result"] = deepcopy(self.team_result)
        self.game_record["game_title"]["doubles_result"] = deepcopy(self.team_result)
        self.game_record["duplicate_match"] = self.duplicate_match.public_info()
        self.game_record["duplicate_match"]["ready_tables"] = []
        self.game_record["duplicate_boards"] = deepcopy(data["boards"])
        self.game_record["game_title"]["duplicate_round_tiles"] = [
            [t for i in range(4) for t in b["hands"][i] + b["walls"][i]] for b in data["boards"]]
        for player in self.player_list:
            player.record_counter.rank_result = 1 if winner is None or player.team_id == winner else 2


class SbrDuplicateGameState(DuplicateMixin, SichuanGameState):
    pass


class XueliuDuplicateGameState(DuplicateMixin, XueliuDoublesGameState):
    pass
