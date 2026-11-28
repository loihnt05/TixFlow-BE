-- 14 original TixFlow demo events, one per month from June 2027 through July 2028.
-- Run with psql -v ON_ERROR_STOP=1 -v target_db=tixflow (or tixflow_acceptance).
BEGIN;
SELECT set_config('app.demo_seed_target', :'target_db', true);
DO $$ BEGIN
  IF current_database() NOT IN ('tixflow','tixflow_acceptance')
     OR current_database() <> current_setting('app.demo_seed_target') THEN
    RAISE EXCEPTION 'Future demo seed refused on %', current_database();
  END IF;
  IF NOT EXISTS (SELECT 1 FROM organizers WHERE id = '10000000-0000-0000-0000-000000000001')
     OR NOT EXISTS (SELECT 1 FROM venues WHERE id = '20000000-0000-0000-0000-000000000002')
     OR NOT EXISTS (SELECT 1 FROM venues WHERE id = '20000000-0000-0000-0000-000000000003')
     OR NOT EXISTS (SELECT 1 FROM venues WHERE id = '20000000-0000-0000-0000-000000000004') THEN
    RAISE EXCEPTION 'Baseline catalog is missing';
  END IF;
END $$;

CREATE TEMP TABLE future_demo ON COMMIT DROP AS
SELECT n,
  ('f1000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid AS event_id,
  ('f2000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid AS session_id,
  ('f3000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid AS type_id,
  ('f4000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid AS inventory_id,
  ('2027-06-15 02:00+00'::timestamptz + (n-1)*interval '1 month') AS starts_at,
  (ARRAY['Music','Food','Art','Business','Education','Technology','Festival'])[(n-1)%7+1] AS category,
  (ARRAY['20000000-0000-0000-0000-000000000002',
         '20000000-0000-0000-0000-000000000003',
         '20000000-0000-0000-0000-000000000004'])[(n-1)%3+1]::uuid AS venue_id,
  (ARRAY['https://images.unsplash.com/photo-1459749411175-04bf5292ceea',
         'https://images.unsplash.com/photo-1442512595331-e89e73853f31',
         'https://images.unsplash.com/photo-1531058020387-3be344556be6'])[(n-1)%3+1] AS image_url
FROM generate_series(1,14) AS n;

INSERT INTO events (id,organizer_id,venue_id,name,slug,description,category,image_url,status,
                    sale_starts_at_utc,sale_ends_at_utc,published_at_utc)
SELECT event_id,'10000000-0000-0000-0000-000000000001',venue_id,
       category || ' Community Day ' || to_char(starts_at AT TIME ZONE 'Asia/Ho_Chi_Minh','Mon YYYY'),
       'tixflow-future-' || to_char(starts_at AT TIME ZONE 'Asia/Ho_Chi_Minh','YYYY-MM'),
       'Sự kiện demo TixFlow theo lịch tháng, dùng để kiểm thử đặt vé và phân trang trong tương lai.',
       category,image_url,'Published','2026-11-01 00:00+00',starts_at-interval '1 day','2026-11-01 00:00+00'
FROM future_demo ON CONFLICT (id) DO NOTHING;

INSERT INTO event_sessions (id,event_id,name,starts_at_utc,ends_at_utc,status)
SELECT session_id,event_id,'Chương trình chính',starts_at,starts_at+interval '6 hours','OnSale'
FROM future_demo ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_types (id,event_session_id,name,code,description,price,currency,inventory_mode,capacity,max_per_order)
SELECT type_id,session_id,'Standard','STANDARD','Vé demo cho sự kiện theo tháng.',
       100000+n*25000,'VND','GeneralAdmission',100+n*10,4
FROM future_demo ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_inventories (id,ticket_type_id,total_quantity,held_quantity,sold_quantity,version)
SELECT inventory_id,type_id,100+n*10,0,0,0 FROM future_demo ON CONFLICT (id) DO NOTHING;
COMMIT;
