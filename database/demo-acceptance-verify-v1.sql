-- Read-only verification for the acceptance fixture. Emit exactly one JSON row.
DO $$
BEGIN
    IF current_database() <> 'tixflow_acceptance' THEN
        RAISE EXCEPTION 'Acceptance verification refused on database %', current_database();
    END IF;
END $$;

WITH fixture AS (
    SELECT anchor_utc
    FROM demo_seed_runs
    WHERE seed_version = 'demo-acceptance-refresh-v2'
)
SELECT json_build_object(
    'counts', json_build_object(
        'events', (SELECT count(*) FROM events WHERE id IN (
            'a1200000-0000-4000-8000-000000000001', 'a1200000-0000-4000-8000-000000000002')),
        'sessions', (SELECT count(*) FROM event_sessions WHERE id IN (
            'a1210000-0000-4000-8000-000000000001', 'a1210000-0000-4000-8000-000000000002')),
        'ticketTypes', (SELECT count(*) FROM ticket_types WHERE id IN (
            'a1220000-0000-4000-8000-000000000001', 'a1220000-0000-4000-8000-000000000002')),
        'inventories', (SELECT count(*) FROM ticket_inventories WHERE id IN (
            'a1230000-0000-4000-8000-000000000001', 'a1230000-0000-4000-8000-000000000002')),
        'seats', (SELECT count(*) FROM seats WHERE id IN (
            'a1240000-0000-4000-8000-000000000001', 'a1240000-0000-4000-8000-000000000002'))
    ),
    'anchorUtc', (SELECT anchor_utc FROM fixture),
    'invariants', json_build_object(
        'eventWindows', (SELECT count(*) = 2 FROM events e CROSS JOIN fixture f
            WHERE e.id IN ('a1200000-0000-4000-8000-000000000001', 'a1200000-0000-4000-8000-000000000002')
              AND e.status = 'Published'
              AND e.sale_starts_at_utc = f.anchor_utc - interval '1 day'
              AND e.sale_ends_at_utc = f.anchor_utc + interval '30 days'),
        'sessionWindows', (SELECT count(*) = 2 FROM event_sessions s CROSS JOIN fixture f
            WHERE s.id IN ('a1210000-0000-4000-8000-000000000001', 'a1210000-0000-4000-8000-000000000002')
              AND s.status = 'OnSale'
              AND s.starts_at_utc = f.anchor_utc + interval '32 days'
              AND s.ends_at_utc = f.anchor_utc + interval '32 days 3 hours'),
        'links', (SELECT count(*) = 2 FROM (VALUES
            ('a1210000-0000-4000-8000-000000000001'::uuid, 'a1200000-0000-4000-8000-000000000001'::uuid,
             'a1220000-0000-4000-8000-000000000001'::uuid, 'a1230000-0000-4000-8000-000000000001'::uuid),
            ('a1210000-0000-4000-8000-000000000002'::uuid, 'a1200000-0000-4000-8000-000000000002'::uuid,
             'a1220000-0000-4000-8000-000000000002'::uuid, 'a1230000-0000-4000-8000-000000000002'::uuid)
        ) expected(session_id, event_id, ticket_type_id, inventory_id)
        JOIN event_sessions s ON s.id = expected.session_id AND s.event_id = expected.event_id
        JOIN ticket_types t ON t.id = expected.ticket_type_id AND t.event_session_id = s.id
        JOIN ticket_inventories i ON i.id = expected.inventory_id AND i.ticket_type_id = t.id),
        'inventory', (SELECT count(*) = 2 FROM ticket_inventories i
            WHERE i.id IN ('a1230000-0000-4000-8000-000000000001', 'a1230000-0000-4000-8000-000000000002')
              AND i.held_quantity >= 0 AND i.sold_quantity >= 0
              AND i.held_quantity + i.sold_quantity <= i.total_quantity),
        'seats', (SELECT count(*) = 2 FROM seats s
            WHERE s.id IN ('a1240000-0000-4000-8000-000000000001', 'a1240000-0000-4000-8000-000000000002')
              AND s.event_session_id = 'a1210000-0000-4000-8000-000000000002'
              AND s.ticket_type_id = 'a1220000-0000-4000-8000-000000000002'
              AND s.is_active)
    )
)::text;
