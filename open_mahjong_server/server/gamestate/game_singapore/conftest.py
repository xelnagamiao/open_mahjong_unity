import pytest


@pytest.fixture(autouse=True)
def inline_hint_cpu(monkeypatch):
    async def run(state, fn, *args, **kwargs): return fn(*args, **kwargs)
    monkeypatch.setattr('server.gamestate.game_singapore.SingaporeGameState.run_room_bot_cpu', run)
