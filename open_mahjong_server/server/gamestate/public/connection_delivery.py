"""Bound socket backpressure without cancelling the task that owns the game."""
import asyncio

SEND_TIMEOUT_SECONDS = 5.0


async def send_bounded(websocket, payload, *, timeout=None):
    async with asyncio.timeout(SEND_TIMEOUT_SECONDS if timeout is None else timeout):
        await websocket.send_json(payload)
