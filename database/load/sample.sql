SELECT json_build_object('utc',clock_timestamp(),
 'connections',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()),
 'active',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND state='active'),
 'lock_waiters',(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'),
 'oldest_transaction_seconds',(SELECT coalesce(max(extract(epoch FROM clock_timestamp()-xact_start)),0) FROM pg_stat_activity WHERE datname=current_database()),
 'pending_outbox',(SELECT count(*) FROM outbox_messages WHERE processed_at_utc IS NULL),
 'oldest_outbox_seconds',(SELECT coalesce(max(extract(epoch FROM clock_timestamp()-occurred_at_utc)),0) FROM outbox_messages WHERE processed_at_utc IS NULL),
 'consumer_lag_seconds',(SELECT coalesce(max(extract(epoch FROM i.processed_at_utc-o.occurred_at_utc)),0) FROM message_inbox i JOIN outbox_messages o ON o.id=i.message_id WHERE i.processed_at_utc>clock_timestamp()-interval '5 minutes'),
 'blocked_queries',(SELECT coalesce(json_agg(x),'[]') FROM
   (SELECT pid,pg_blocking_pids(pid) AS blockers,left(query,500) AS query
    FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock') x)
);
