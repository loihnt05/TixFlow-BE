BEGIN;

INSERT INTO ticket_types
    (id, event_session_id, name, code, description, price, currency,
     inventory_mode, capacity, max_per_order, sale_starts_at_utc, sale_ends_at_utc)
VALUES
    ('50000000-0000-0000-0000-000000000012',
     '40000000-0000-0000-0000-000000000001',
     'Flash 50', 'FLASH50', 'Lô vé hữu hạn dùng cho kiểm thử tranh chấp và tải cao.',
     250000, 'VND', 'GeneralAdmission', 50, 2,
     '2026-09-01 01:00:00+00', '2026-12-20 11:00:00+00')
ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_inventories
    (id, ticket_type_id, total_quantity, held_quantity, sold_quantity, version)
VALUES
    ('70000000-0000-0000-0000-000000000012',
     '50000000-0000-0000-0000-000000000012', 50, 0, 0, 0)
ON CONFLICT (id) DO NOTHING;

COMMIT;
