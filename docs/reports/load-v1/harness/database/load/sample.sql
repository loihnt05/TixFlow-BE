SELECT json_build_object('utc',clock_timestamp(),
 'connections',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()),
 'active',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND state='active'),
 'lock_waiters',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'),
 'oldest_transaction_seconds',(SELECT coalesce(max(extract(epoch FROM clock_timestamp()-xact_start)),0) FROM pg_stat_activity WHERE datname=current_database()),
 'pending_outbox',(SELECT count(*) FROM outbox_messages WHERE processed_at_utc IS NULL),
 'oldest_outbox_seconds',(SELECT coalesce(max(extract(epoch FROM clock_timestamp()-occurred_at_utc)),0) FROM outbox_messages WHERE processed_at_utc IS NULL)
);
