-- Demo catalog for APP-07..APP-20. Run only on an isolated acceptance database.
-- psql -v ON_ERROR_STOP=1 -v acceptance_db=tixflow_acceptance -f database/demo-acceptance-v1.sql
BEGIN;
DO $$
BEGIN
  IF current_database() <> 'tixflow_acceptance' THEN
    RAISE EXCEPTION 'Refusing demo seed on database %; expected tixflow_acceptance', current_database();
  END IF;
  IF NOT EXISTS (SELECT 1 FROM organizers WHERE id = '10000000-0000-0000-0000-000000000001') THEN
    RAISE EXCEPTION 'Apply baseline schema and migrations before demo seed';
  END IF;
END $$;

-- Stable IDs and dates; ON CONFLICT preserves transactions from previous runs.
INSERT INTO events (id, organizer_id, venue_id, name, slug, description, category, image_url,
                    status, sale_starts_at_utc, sale_ends_at_utc, published_at_utc)
VALUES
 ('a1200000-0000-4000-8000-000000000001','10000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000002','Acceptance Last GA Seat',
  'acceptance-last-ga-seat','One available GA ticket for concurrency and payment testing.',
  'Music','https://images.unsplash.com/photo-1459749411175-04bf5292ceea',
  'Published','2026-11-01T00:00:00Z','2027-01-30T00:00:00Z','2026-11-01T00:00:00Z'),
 ('a1200000-0000-4000-8000-000000000002','10000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000003','Acceptance Reserved Seat',
  'acceptance-reserved-seat','Two numbered seats for competing customer checkouts.',
  'Business','https://images.unsplash.com/photo-1511578314322-379afb476865',
  'Published','2026-11-01T00:00:00Z','2027-01-30T00:00:00Z','2026-11-01T00:00:00Z')
ON CONFLICT (id) DO NOTHING;

INSERT INTO event_sessions (id,event_id,name,starts_at_utc,ends_at_utc,status) VALUES
 ('a1210000-0000-4000-8000-000000000001','a1200000-0000-4000-8000-000000000001',
  'GA concurrency','2027-02-01T10:00:00Z','2027-02-01T13:00:00Z','OnSale'),
 ('a1210000-0000-4000-8000-000000000002','a1200000-0000-4000-8000-000000000002',
  'Reserved concurrency','2027-02-02T10:00:00Z','2027-02-02T13:00:00Z','OnSale')
ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_types (id,event_session_id,name,code,description,price,currency,
                          inventory_mode,capacity,max_per_order) VALUES
 ('a1220000-0000-4000-8000-000000000001','a1210000-0000-4000-8000-000000000001',
  'Last GA','LASTGA','Single ticket for contention',100000,'VND','GeneralAdmission',1,1),
 ('a1220000-0000-4000-8000-000000000002','a1210000-0000-4000-8000-000000000002',
  'Reserved','RESERVED','Two numbered seats',200000,'VND','ReservedSeating',2,1)
ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_inventories (id,ticket_type_id,total_quantity,held_quantity,sold_quantity,version) VALUES
 ('a1230000-0000-4000-8000-000000000001','a1220000-0000-4000-8000-000000000001',1,0,0,0),
 ('a1230000-0000-4000-8000-000000000002','a1220000-0000-4000-8000-000000000002',2,0,0,0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO seats (id,event_session_id,ticket_type_id,section,row_label,seat_number) VALUES
 ('a1240000-0000-4000-8000-000000000001','a1210000-0000-4000-8000-000000000002',
  'a1220000-0000-4000-8000-000000000002','A','1','1'),
 ('a1240000-0000-4000-8000-000000000002','a1210000-0000-4000-8000-000000000002',
  'a1220000-0000-4000-8000-000000000002','A','1','2')
ON CONFLICT (id) DO NOTHING;
COMMIT;
