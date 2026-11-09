CREATE TABLE IF NOT EXISTS user_wallets (
    user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
    balance_minor BIGINT NOT NULL DEFAULT 0 CHECK (balance_minor >= 0),
    earned_minor BIGINT NOT NULL DEFAULT 0 CHECK (earned_minor >= 0),
    spent_minor BIGINT NOT NULL DEFAULT 0 CHECK (spent_minor >= 0),
    version BIGINT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS wallet_ledger (
    entry_id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    source VARCHAR(32) NOT NULL,
    source_id VARCHAR(96) NOT NULL,
    delta_minor BIGINT NOT NULL CHECK (delta_minor <> 0),
    balance_after BIGINT NOT NULL CHECK (balance_after >= 0),
    completed_hands INTEGER,
    calculation_version INTEGER NOT NULL DEFAULT 1,
    metadata JSONB NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (user_id,source,source_id)
);
CREATE INDEX IF NOT EXISTS idx_wallet_ledger_user ON wallet_ledger(user_id,entry_id DESC);
CREATE TABLE IF NOT EXISTS shop_offers (
    offer_id VARCHAR(64) PRIMARY KEY,
    item_id INTEGER NOT NULL REFERENCES item_definitions(item_id),
    unit_price_minor BIGINT NOT NULL CHECK (unit_price_minor BETWEEN 1 AND 100000000),
    price_version INTEGER NOT NULL DEFAULT 1 CHECK (price_version > 0),
    max_purchase_quantity INTEGER NOT NULL DEFAULT 100 CHECK (max_purchase_quantity BETWEEN 1 AND 100),
    prevent_duplicate_purchase BOOLEAN NOT NULL DEFAULT FALSE,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS shop_orders (
    order_id UUID PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    request_id VARCHAR(64) NOT NULL,
    request_hash VARCHAR(64) NOT NULL,
    offer_id VARCHAR(64) NOT NULL REFERENCES shop_offers(offer_id),
    item_id INTEGER NOT NULL REFERENCES item_definitions(item_id),
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    unit_price_minor BIGINT NOT NULL CHECK (unit_price_minor > 0),
    total_minor BIGINT NOT NULL CHECK (total_minor > 0),
    result JSONB NOT NULL,
    price_version INTEGER,
    item_snapshot JSONB NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (user_id,request_id)
);
ALTER TABLE shop_offers ADD COLUMN IF NOT EXISTS prevent_duplicate_purchase BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE shop_orders ADD COLUMN IF NOT EXISTS price_version INTEGER;
ALTER TABLE shop_orders ADD COLUMN IF NOT EXISTS item_snapshot JSONB NOT NULL DEFAULT '{}';
CREATE INDEX IF NOT EXISTS idx_shop_orders_created ON shop_orders(created_at DESC,order_id);
CREATE INDEX IF NOT EXISTS idx_shop_orders_user ON shop_orders(user_id,created_at DESC);
