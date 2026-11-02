CREATE TABLE IF NOT EXISTS chat_conversations (
    conversation_key VARCHAR(96) PRIMARY KEY,
    kind VARCHAR(8) NOT NULL CHECK (kind IN ('dm','venue')),
    peer_low BIGINT REFERENCES users(user_id) ON DELETE CASCADE,
    peer_high BIGINT REFERENCES users(user_id) ON DELETE CASCADE,
    event_id VARCHAR(32) REFERENCES events(event_id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((kind='dm' AND peer_low<peer_high AND event_id IS NULL)
        OR (kind='venue' AND event_id IS NOT NULL AND peer_low IS NULL AND peer_high IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS chat_direct_pair ON chat_conversations(peer_low,peer_high) WHERE kind='dm';
CREATE UNIQUE INDEX IF NOT EXISTS chat_venue ON chat_conversations(event_id) WHERE kind='venue';
CREATE TABLE IF NOT EXISTS chat_conversation_members (
    conversation_key VARCHAR(96) NOT NULL REFERENCES chat_conversations(conversation_key) ON DELETE CASCADE,
    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    hidden BOOLEAN NOT NULL DEFAULT FALSE,
    last_read_message_id BIGINT NOT NULL DEFAULT 0,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY(conversation_key,user_id)
);
CREATE INDEX IF NOT EXISTS chat_member_user ON chat_conversation_members(user_id,hidden);
CREATE TABLE IF NOT EXISTS chat_messages (
    message_id BIGSERIAL PRIMARY KEY,
    conversation_key VARCHAR(96) NOT NULL REFERENCES chat_conversations(conversation_key) ON DELETE CASCADE,
    sender_user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    content TEXT NOT NULL CHECK (char_length(content) BETWEEN 1 AND 500),
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    request_id VARCHAR(64) NOT NULL,
    UNIQUE(sender_user_id,request_id)
);
CREATE INDEX IF NOT EXISTS chat_messages_history ON chat_messages(conversation_key,message_id DESC);
CREATE INDEX IF NOT EXISTS chat_messages_rate ON chat_messages(sender_user_id,created_at DESC);
