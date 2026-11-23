"""Let a completed game wait for a pooled connection without blocking other tables."""
import asyncio
import logging

from psycopg2.pool import PoolError


logger = logging.getLogger(__name__)


def is_pool_exhausted(error):
    # psycopg2 uses PoolError for closed pools and invalid returns as well.
    return isinstance(error, PoolError) and error.args == ("connection pool exhausted",)


async def run_db_write(operation, *args, **kwargs):
    write = asyncio.create_task(asyncio.to_thread(operation, *args, **kwargs))
    try:
        return await asyncio.shield(write)
    except asyncio.CancelledError as cancelled:
        # The worker may already own a transaction. Drain it before the game is cleaned up.
        try:
            await write
        finally:
            raise cancelled


async def run_with_pool_retry(operation, *args, **kwargs):
    delay = 0.25
    while True:
        try:
            return await run_db_write(operation, *args, **kwargs)
        except PoolError as error:
            if not is_pool_exhausted(error):
                raise
            if delay == 0.25:
                logger.warning("数据库连接池已满，等待空闲连接后继续终局写入")
            await asyncio.sleep(delay)
            delay = min(5, delay * 2)
