"""Room speed is universal; rooms containing bots disable claim protection.

Use a virtual clock at real bot entry points to check all four speeds without
wall-clock flakes or repeatedly running expensive tile evaluators.
"""
import asyncio
import importlib
from types import SimpleNamespace as NS
from unittest.mock import patch

import pytest

from . import pacing
from ... import gamestate_manager as manager_module
from ...test_room_lifecycle import make_room, make_server, parked_loop, USERS


ROOM_VARIANTS = [
    ("guobiao", "guobiao/standard", "GuobiaoGameState"),
    ("guobiao", "guobiao/blood_battle", "GuobiaoGameState"),
    ("qingque", "qingque/standard", "QingqueGameState"),
    ("changsha", "changsha/classic_double_bird", "ChangshaGameState"),
    ("classical", "classical/standard", "ClassicalGameState"),
    ("riichi", "riichi/standard", "RiichiGameState"),
    ("riichi", "riichi/langyong", "RiichiGameState"),
    ("sichuan", "sichuan/standard", "SichuanGameState"),
    ("sichuan", "sichuan/xueliu", "XueliuGameState"),
    ("sichuan", "sichuan/xueliu_exchange", "XueliuGameState"),
    ("jiandan", "jiandan/standard", "JiandanGameState"),
    ("zhongyong", "zhongyong/standard", "ZhongyongGameState"),
    ("zhongyong", "zhongyong/nanque", "NanqueGameState"),
    ("taiwan", "taiwan/standard", "TaiwanGameState"),
    ("shanghai", "shanghai/qiaoma", "ShanghaiGameState"),
    ("shanghai", "shanghai/qinghunpeng", "QinghunpengGameState"),
    ("hongque", "hongque/v1.6", "HongqueGameState"),
    ("hongkong", "hongkong/qingzhang", "HongKongGameState"),
    ("hongkong", "hongkong/new13", "HongKongGameState"),
    ("hongkong", "hongkong/new16", "HongKongGameState"),
]


@pytest.mark.parametrize("rule,sub_rule,class_name", ROOM_VARIANTS)
@pytest.mark.parametrize("speed", [None, "instant", "fast", "medium", "slow", "standard"])
@pytest.mark.parametrize("protection", [False, True])
def test_room_speed_reaches_every_real_state_class(rule, sub_rule, class_name, speed, protection):
    async def run():
        users = [USERS[0], 0, 2, USERS[1]]
        server = make_server(users)
        room = make_room(users, rule)
        room.update(sub_rule=sub_rule, claim_protection=protection)
        server.room_manager.rooms["1"] = room
        if speed is not None:
            assert await server.room_manager.set_bot_speed(str(users[0]), "1", speed) is None
        cls = getattr(manager_module, class_name)
        with patch.object(cls, "run_game_loop", parked_loop):
            try:
                result = await server.gamestate_manager.start_game(str(users[0]), "1")
                assert result is None, result
                state = server.gamestate_manager.get_game_state_by_room_id("1")
                assert isinstance(state, cls)
                assert state.bot_speed == pacing.normalize_bot_speed(speed)
                assert pacing.bot_delay(state) == pacing.BOT_SPEEDS[pacing.normalize_bot_speed(speed)]
                assert not getattr(state, "claim_protection", False)
                assert room["claim_protection"] is protection  # Lobby preference survives.
                # Configuration belongs to this game snapshot, not a mutable lobby.
                room["bot_speed"] = "instant" if state.bot_speed != "instant" else "slow"
                assert state.bot_speed == pacing.normalize_bot_speed(speed)
            finally:
                for gid in list(server.gamestate_manager.gamestate_id_to_game_state):
                    await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=gid)
    asyncio.run(run())


class VirtualClock:
    def __init__(self, cost):
        self.now = 100.0
        self.cost = cost
        self.waits = []

    def monotonic(self):
        return self.now

    def calculate(self):
        self.now += self.cost

    async def sleep(self, seconds):
        self.waits.append(seconds)
        self.now += seconds


@pytest.mark.parametrize("protection", [False, True])
@pytest.mark.parametrize("speed", ["instant", "slow"])
def test_offline_human_retains_takeover_clock_and_protected_post_gap(monkeypatch, protection, speed):
    async def run():
        clock = VirtualClock(0)
        monkeypatch.setattr(pacing, "time", clock)
        monkeypatch.setattr(pacing, "asyncio", NS(sleep=clock.sleep))
        state = NS(room_rule="guobiao", claim_protection=protection, bot_speed=speed,
                   player_list=[NS(user_id=101, tag_list=["offline"])],
                   server_action_tick=1, waiting_players_list=[0])
        @pacing.paced_bot(default_delay=lambda: .8)
        async def takeover(gs, player, actions, phase):
            assert await pacing.wait_before_bot_submit(gs, player, "cut")
        await takeover(state, 0, ["cut"], "onlycut_after_action")
        assert sum(clock.waits) == pytest.approx(.8 + (.5 if protection else 0))
    asyncio.run(run())


# Each entry is a real production bot function, including rule-local adapters.
BOT_ROUTES = []
for rule in ("guobiao", "qingque", "changsha", "classical", "riichi", "sichuan", "taiwan", "shanghai"):
    BOT_ROUTES.append((rule, "", "auto", 0))
for rule in ("guobiao", "qingque", "changsha", "classical", "sichuan", "taiwan"):
    BOT_ROUTES.append((rule, "", "smart", 2))
BOT_ROUTES += [("guobiao", "", "heuristic", 3), ("riichi", "", "riichi", 2)]
BOT_ROUTES.append(("guobiao", "guobiao/blood_battle", "heuristic", 3))
for sub_rule in ("sichuan/xueliu", "sichuan/xueliu_exchange"):
    BOT_ROUTES.append(("sichuan", sub_rule, "xueliu", 2))
for sub_rule in ("shanghai/qiaoma", "shanghai/qinghunpeng"):
    BOT_ROUTES.append(("shanghai", sub_rule, "shanghai", 2))
for rule, sub_rule in (("jiandan", ""), ("zhongyong", "zhongyong/standard"), ("zhongyong", "zhongyong/nanque")):
    BOT_ROUTES += [(rule, sub_rule, "jiandan", uid) for uid in (0, 2)]
for sub_rule in ("hongkong/qingzhang", "hongkong/new13", "hongkong/new16"):
    BOT_ROUTES += [("hongkong", sub_rule, "hongkong", uid) for uid in (0, 2)]

ENTRY_POINTS = {
    "auto": ("public.ai.auto_cut_ai", "auto_cut_action"),
    "smart": ("public.ai.smart_bot_ai", "smart_bot_action"),
    "heuristic": ("public.ai.guobiao_heuristic_ai", "guobiao_heuristic_action"),
    "riichi": ("public.ai.riichi_smart_bot_ai", "riichi_smart_bot_action"),
    "xueliu": ("game_sichuan.xueliu_bot", "xueliu_smart_bot_action"),
    "shanghai": ("game_shanghai.bot", "shanghai_smart_bot_action"),
    "jiandan": ("game_jiandan.bot", "jiandan_bot_action"),
    "hongkong": ("game_hongkong.bot", "play_bot"),
}


@pytest.mark.parametrize("rule,sub_rule,kind,uid", BOT_ROUTES)
@pytest.mark.parametrize("speed", pacing.BOT_SPEEDS)
@pytest.mark.parametrize("phase", ["waiting_hand_action", "onlycut_after_action"])
@pytest.mark.parametrize("cost", [.2, 2.0])
@pytest.mark.parametrize("protection", [False, True])
def test_every_bot_entry_uses_room_speed_and_includes_calculation_time(monkeypatch, caplog, rule, sub_rule, kind, uid, speed, phase, cost, protection):
    async def run():
        clock = VirtualClock(cost)
        monkeypatch.setattr(pacing, "time", clock)
        monkeypatch.setattr(pacing, "asyncio", NS(sleep=clock.sleep))
        module_name, name = ENTRY_POINTS[kind]
        module = importlib.import_module("server.gamestate." + module_name)
        player = NS(user_id=uid, username="bot", hand_tiles=[11, 12], has_draw_slot=True,
                    combination_tiles=[], combination_mask=[], discard_tiles=[], tag_list=[],
                    riichi_candidate_cuts={}, kuikae_forbidden_tiles=set(), ready_locked=False,
                    dingque_suit=0, post_hu_lock=False, huapai_list=[], xueliu_throw_tiles=[])
        state = NS(room_rule=rule, sub_rule=sub_rule, bot_speed=speed, claim_protection=protection,
                   game_status=phase, server_action_tick=1, waiting_players_list=[0],
                   current_player_index=0, player_list=[player], action_dict={0: ["cut"]},
                   is_xueliu=True, xueliu_exchange=sub_rule.endswith("exchange"), xueliu_meld_count=4)
        sent = []
        async def send(*args, **kwargs):
            sent.append((clock.now - 100, args, kwargs))
        if kind == "hongkong":
            state.submit_action = send
            def choose(*args, **kwargs):
                clock.calculate()
                return {"action_type": "cut", "TileId": 11}
            monkeypatch.setattr(module, "choose_action", choose)
        else:
            monkeypatch.setattr(module, "get_ai_action", send)
        if hasattr(module, "count_visible_tiles"):
            monkeypatch.setattr(module, "count_visible_tiles", lambda _: [0] * 34)
        if kind == "heuristic":
            monkeypatch.setattr(module, "context_from_game", lambda *_: NS(scorer=None))
        async def calculate(*args, **kwargs):
            clock.calculate()
            if kind in ("riichi", "xueliu") or (kind == "shanghai" and sub_rule.endswith("qinghunpeng")):
                return "cut", 11, 0
            if kind == "shanghai":
                return 11, 0
            return "cut", 11
        if hasattr(module, "run_room_bot_cpu"):
            monkeypatch.setattr(module, "run_room_bot_cpu", calculate)
        if kind == "jiandan" and uid == 2:
            def pick(*args, **kwargs):
                clock.calculate()
                return 11, 0
            monkeypatch.setattr(module, "find_best_cut", pick)
        if kind == "auto" or (kind == "jiandan" and uid == 0):
            original_pick = module._pick_auto_cut_tile
            def pick(*args):
                clock.calculate()
                return original_pick(*args)
            monkeypatch.setattr(module, "_pick_auto_cut_tile", pick)
        args = [state, 0, ["cut"], phase]
        if kind in ("jiandan", "hongkong"):
            args.append(1)
        await getattr(module, name)(*args)
        assert len(sent) == 1
        assert sent[0][0] == pytest.approx(max(cost, pacing.BOT_SPEEDS[speed]))
        assert sum(clock.waits) == pytest.approx(max(0, pacing.BOT_SPEEDS[speed] - cost))
        assert not any(record.levelname == "ERROR" for record in caplog.records)
    asyncio.run(run())


@pytest.mark.parametrize("uid", [0, 2, 3])
@pytest.mark.parametrize("speed", pacing.BOT_SPEEDS)
@pytest.mark.parametrize("cost", [.2, 2.0])
def test_hongque_distinct_protocol_shares_the_same_room_clock(monkeypatch, uid, speed, cost):
    from ...game_hongque import get_action as module
    async def run():
        clock = VirtualClock(cost)
        monkeypatch.setattr(pacing, "time", clock)
        monkeypatch.setattr(pacing, "asyncio", NS(sleep=clock.sleep))
        monkeypatch.setattr(module, "time", clock)
        monkeypatch.setattr(module, "kong_candidates", lambda *_: [])
        monkeypatch.setattr(module, "kong_win_candidates", lambda *_: [])
        monkeypatch.setattr(module, "OpponentView", NS(from_player=lambda _: None))
        players = [NS(user_id=uid if i == 0 else 100+i, index=i, hand=["AX1"], melds=[],
                      discards=[], supplements=0, drawn_tile="AX1", last_draw_was_supplement=False)
                   for i in range(4)]
        state = NS(phase="turn", game_status="waiting_hand_action", action_tick=1, _lock=asyncio.Lock(),
                   current_player_index=0, players=players, wall=["AX2"], last_discard=None,
                   bot_speed=speed, Debug=False, _rng=NS(choice=lambda hand: hand[0]))
        sent = []
        async def send(*args, **kwargs): sent.append(clock.now-100)
        state.submit_action = send
        async def calculate(*args, **kwargs):
            clock.calculate()
            return None if uid == 0 else {"action": "discard", "tile": "AX1"}
        monkeypatch.setattr(module, "run_room_bot_cpu", calculate)
        await module.bot_turn(state, 1)
        assert sent == pytest.approx([max(cost, pacing.BOT_SPEEDS[speed])])
        assert sum(clock.waits) == pytest.approx(max(0, pacing.BOT_SPEEDS[speed]-cost))
    asyncio.run(run())
