CREATE TABLE IF NOT EXISTS player_mail (
    mail_id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    sender VARCHAR(80) NOT NULL,
    title VARCHAR(160) NOT NULL,
    content TEXT NOT NULL,
    created_by BIGINT NOT NULL,
    delivery_key VARCHAR(64) NOT NULL UNIQUE,
    delivery_hash VARCHAR(64) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMPTZ,
    read_at TIMESTAMPTZ,
    claimed_at TIMESTAMPTZ,
    claim_operation_id UUID REFERENCES inventory_operations(operation_id)
);
CREATE INDEX IF NOT EXISTS idx_player_mail_recipient ON player_mail(user_id,mail_id DESC);
CREATE TABLE IF NOT EXISTS player_mail_rewards (
    mail_id BIGINT NOT NULL REFERENCES player_mail(mail_id) ON DELETE CASCADE,
    item_id INTEGER NOT NULL REFERENCES item_definitions(item_id),
    quantity INTEGER NOT NULL CHECK (quantity BETWEEN 1 AND 1000000),
    PRIMARY KEY (mail_id,item_id)
);
