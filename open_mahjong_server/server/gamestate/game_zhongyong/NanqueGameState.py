from .ZhongyongGameState import ZhongyongGameState

class NanqueGameState(ZhongyongGameState):
    """Nanque uses its own points and the three-winner retirement flow."""
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="nanque-test"):
        room_data = dict(room_data or self._default_room_data())
        room_data["room_rule"] = "zhongyong"
        room_data["sub_rule"] = "zhongyong/nanque"
        super().__init__(game_server, room_data, calculation_service, db_manager, gamestate_id)
