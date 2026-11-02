"""Shared, persisted receipt format for any server-authoritative inventory award."""
from .inventory import _definition, _public


def make_reward_receipt(cursor, operation_id, user_id, source, source_id, changes):
    items, skipped = [], []
    for change in changes:
        if change['delta'] < 0:
            continue
        item = _public(_definition(cursor, change['item_id']))
        item.update(quantity=change['delta'], quantity_after=change['quantity_after'])
        (items if change['delta'] > 0 else skipped).append(item)
    return dict(version=1, receipt_id=str(operation_id), user_id=user_id,
                source=source, source_id=str(source_id), items=items, skipped_items=skipped)


async def notify_reward(server, result, exclude=None):
    """Delivery is best-effort AFTER commit. An offline player keeps all awarded items."""
    receipt = result.get('reward_receipt')
    if not receipt or not receipt['items']:
        return False
    connection = server.user_id_to_connection.get(receipt['user_id'])
    socket = getattr(connection, 'websocket', None)
    if socket is None or socket is exclude:
        return False
    from .inventory_router import _send
    await _send(socket, dict(type='reward/received', success=True,
                            reward_receipt=receipt, inventory_state=result['inventory_state']))
    return True
