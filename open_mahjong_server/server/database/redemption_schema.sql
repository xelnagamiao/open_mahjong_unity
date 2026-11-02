CREATE TABLE IF NOT EXISTS redeem_campaigns (
    campaign_id BIGSERIAL PRIMARY KEY,
    creation_key VARCHAR(64) NOT NULL UNIQUE,
    creation_hash VARCHAR(64) NOT NULL,
    name VARCHAR(100) NOT NULL,
    kind VARCHAR(16) NOT NULL CHECK(kind IN ('shared','unique')),
    status VARCHAR(16) NOT NULL DEFAULT 'draft' CHECK(status IN ('draft','active','paused','ended')),
    starts_at TIMESTAMPTZ,
    ends_at TIMESTAMPTZ,
    max_claims INTEGER CHECK(max_claims > 0),
    claimed_count INTEGER NOT NULL DEFAULT 0,
    code_count INTEGER NOT NULL CHECK(code_count BETWEEN 1 AND 10000),
    created_by BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK(ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at)
);
CREATE TABLE IF NOT EXISTS redeem_rewards (
    campaign_id BIGINT NOT NULL REFERENCES redeem_campaigns,
    item_id INTEGER NOT NULL REFERENCES item_definitions,
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    PRIMARY KEY(campaign_id,item_id)
);
CREATE TABLE IF NOT EXISTS redeem_codes (
    code_id BIGSERIAL PRIMARY KEY,
    campaign_id BIGINT NOT NULL REFERENCES redeem_campaigns,
    code_hash VARCHAR(64) NOT NULL UNIQUE,
    code_cipher TEXT NOT NULL,
    code_hint VARCHAR(40) NOT NULL,
    claimed_count INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS redeem_codes_campaign_idx ON redeem_codes(campaign_id);
CREATE TABLE IF NOT EXISTS redeem_redemptions (
    redemption_id BIGSERIAL PRIMARY KEY,
    campaign_id BIGINT NOT NULL REFERENCES redeem_campaigns,
    code_id BIGINT NOT NULL REFERENCES redeem_codes,
    user_id BIGINT NOT NULL REFERENCES users,
    operation_id UUID NOT NULL UNIQUE REFERENCES inventory_operations,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS redeem_redemptions_code_idx ON redeem_redemptions(code_id);
-- Existing batches keep their one-claim-per-player activity semantics.
ALTER TABLE redeem_campaigns ADD COLUMN IF NOT EXISTS purpose VARCHAR(16) NOT NULL DEFAULT 'activity'
    CHECK(purpose IN ('ordinary','activity'));
ALTER TABLE redeem_redemptions DROP CONSTRAINT IF EXISTS redeem_redemptions_campaign_id_user_id_key;
CREATE UNIQUE INDEX IF NOT EXISTS redeem_redemptions_code_user_idx ON redeem_redemptions(code_id,user_id);
CREATE INDEX IF NOT EXISTS redeem_redemptions_campaign_user_idx ON redeem_redemptions(campaign_id,user_id);
CREATE TABLE IF NOT EXISTS redeem_rate_limits (
    bucket VARCHAR(100) PRIMARY KEY,
    window_start TIMESTAMPTZ NOT NULL,
    attempts INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS redeem_rate_limits_window_idx ON redeem_rate_limits(window_start);
