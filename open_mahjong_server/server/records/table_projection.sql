-- Relational consumers retain each table's original game ID, action positions
-- and timestamps. The only stored replay body lives in game_replays.record.
CREATE OR REPLACE FUNCTION replay_table_payload(t JSONB) RETURNS JSONB
LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE AS $$
DECLARE
    payload JSONB := t->'payload';
    k TEXT;
    h JSONB;
    asks JSONB;
    original_count INTEGER;
    time_count INTEGER;
    tick_list JSONB;
    time_list JSONB;
    rd JSONB;
BEGIN
    FOR k IN SELECT jsonb_array_elements_text(COALESCE(t->'source_omitted_title_fields','[]'::jsonb)) LOOP
        payload := payload #- ARRAY['game_title', k];
    END LOOP;
    FOR k IN SELECT jsonb_object_keys(COALESCE(t->'transport_actions','{}'::jsonb))
             UNION SELECT jsonb_object_keys(COALESCE(t->'source_round_fields','{}'::jsonb))
    LOOP
        rd := payload->'game_round'->k;
        h := COALESCE(t->'source_round_fields'->k,'{}'::jsonb);
        original_count := jsonb_array_length(t->'round_meta'->k->'legacy_node_map')-1;
        time_count := COALESCE((h->>'time_count')::integer, original_count);
        SELECT COALESCE(jsonb_object_agg(a->>'node',a),'{}'::jsonb) INTO asks
        FROM jsonb_array_elements(COALESCE(t->'transport_actions'->k,'[]'::jsonb)) a;
        SELECT COALESCE(jsonb_agg(CASE WHEN asks ? (i+1)::text THEN asks->(i+1)::text->'tick'
                              ELSE rd->'action_ticks'->((t->'round_meta'->k->'legacy_node_map'->>i)::int) END
                              ORDER BY i) FILTER(WHERE i<original_count),'[]'::jsonb),
               COALESCE(jsonb_agg(CASE WHEN i>=original_count THEN h->'time_tail'->(i-original_count)
                              WHEN asks ? (i+1)::text THEN asks->(i+1)::text->'time_ms'
                              ELSE rd->'action_times_ms'->((t->'round_meta'->k->'legacy_node_map'->>i)::int) END
                              ORDER BY i) FILTER(WHERE i<time_count),'[]'::jsonb)
        INTO tick_list,time_list FROM generate_series(0,GREATEST(original_count,time_count)-1) i;
        IF COALESCE((h->>'no_action_ticks')::boolean,FALSE) THEN rd := rd - 'action_ticks';
        ELSE rd := jsonb_set(rd,'{action_ticks}',tick_list); END IF;
        IF rd ? 'action_times_ms' THEN rd := jsonb_set(rd,'{action_times_ms}',time_list); END IF;
        payload := jsonb_set(payload,ARRAY['game_round',k],rd);
    END LOOP;
    RETURN payload;
END;
$$;

CREATE OR REPLACE VIEW game_records AS
SELECT t.game_id, replay_table_payload(r.record->'tables'->t.table_id) AS record,
       t.created_at,t.replay_id,t.table_id
FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id;

-- List filters, ratings and access checks only inspect the title. Keep them
-- independent of the cost of reconstructing thousands of original action nodes.
CREATE OR REPLACE VIEW game_record_metadata AS
SELECT t.game_id, jsonb_build_object('game_title', r.record->'tables'->t.table_id->'payload'->'game_title') AS record,
       t.created_at,t.replay_id,t.table_id
FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id;
