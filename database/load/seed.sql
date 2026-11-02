-- baseline-v1: deterministic IDs, prices, capacities and sale windows. No production migrations.
BEGIN;
DO $$ BEGIN
    IF current_database() <> 'tixflow_load_tests'
       OR NOT EXISTS (SELECT FROM load_control WHERE dataset = 'load-v2') THEN
        RAISE EXCEPTION 'Baseline seed requires the isolated load database';
    END IF;
END $$;

INSERT INTO events (id, organizer_id, name, slug, category, status, sale_starts_at_utc, sale_ends_at_utc,
                    published_at_utc, created_at_utc, updated_at_utc)
VALUES ('80000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
        'Baseline v1', 'baseline-v1', 'Experiment', 'Published', '2020-01-01', '2099-12-20 11:00Z',
        '2020-01-01', '2020-01-01', '2020-01-01') ON CONFLICT (id) DO NOTHING;
INSERT INTO event_sessions (id, event_id, name, starts_at_utc, ends_at_utc, status, created_at_utc, updated_at_utc)
VALUES ('80000000-0000-0000-0000-000000000002', '80000000-0000-0000-0000-000000000001',
        'Baseline session', '2099-12-20 12:00Z', '2099-12-20 15:00Z', 'OnSale', '2020-01-01', '2020-01-01')
ON CONFLICT (id) DO NOTHING;
INSERT INTO ticket_types (id, event_session_id, name, code, price, currency, inventory_mode, capacity, max_per_order,
                          sale_starts_at_utc, sale_ends_at_utc, created_at_utc, updated_at_utc)
VALUES ('80000000-0000-0000-0000-000000000003', '80000000-0000-0000-0000-000000000002',
        'Baseline Flash 50', 'BASELINE50', 250000, 'VND', 'GeneralAdmission', 50, 2,
        '2020-01-01', '2099-12-20 11:00Z', '2020-01-01', '2020-01-01') ON CONFLICT (id) DO NOTHING;
INSERT INTO ticket_inventories (id, ticket_type_id, total_quantity, held_quantity, sold_quantity, version, updated_at_utc)
VALUES ('80000000-0000-0000-0000-000000000004', '80000000-0000-0000-0000-000000000003', 50, 0, 0, 0, '2020-01-01')
ON CONFLICT (id) DO NOTHING;
INSERT INTO users (id, identity_subject, email, normalized_email, display_name, role, status, created_at_utc, updated_at_utc)
SELECT ('81000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
       'baseline-v1-' || i, 'baseline-v1-' || i || '@example.test',
       upper('baseline-v1-' || i || '@example.test'), 'Auth Test', 'Customer', 'Active', '2020-01-01', '2020-01-01'
FROM generate_series(1, 10000) i ON CONFLICT (id) DO NOTHING;
INSERT INTO ticket_types (id,event_session_id,name,code,price,currency,inventory_mode,capacity,max_per_order,sale_starts_at_utc,sale_ends_at_utc)
VALUES
('80000000-0000-0000-0000-000000000005','80000000-0000-0000-0000-000000000002','Load hot seat','LOADSEAT',250000,'VND','ReservedSeating',1,1,'2020-01-01','2099-12-20 11:00Z'),
('80000000-0000-0000-0000-000000000007','80000000-0000-0000-0000-000000000002','Load checkout','LOADCHECKOUT',250000,'VND','GeneralAdmission',100000,2,'2020-01-01','2099-12-20 11:00Z');
INSERT INTO ticket_inventories(id,ticket_type_id,total_quantity)
VALUES ('80000000-0000-0000-0000-000000000008','80000000-0000-0000-0000-000000000005',1),
('80000000-0000-0000-0000-000000000009','80000000-0000-0000-0000-000000000007',100000);
INSERT INTO seats(id,event_session_id,ticket_type_id,section,row_label,seat_number)
VALUES ('80000000-0000-0000-0000-000000000006','80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000005','Load','A','1');
COMMIT;

