-- Original demo events inspired by the category variety on Zipevent, not copied listings.
-- Run after baseline migrations. Supply -v target_db=tixflow or tixflow_acceptance explicitly.
BEGIN;
SELECT set_config('app.demo_seed_target', :'target_db', true);
DO $$ BEGIN
  IF current_database() NOT IN ('tixflow', 'tixflow_acceptance')
     OR current_database() <> current_setting('app.demo_seed_target') THEN
    RAISE EXCEPTION 'Demo catalog seed refused on database %', current_database();
  END IF;
  IF NOT EXISTS (SELECT 1 FROM organizers WHERE id = '10000000-0000-0000-0000-000000000001') THEN
    RAISE EXCEPTION 'Baseline catalog is missing';
  END IF;
END $$;

CREATE TEMP TABLE demo_catalog_rows ON COMMIT DROP AS
SELECT n,
  ('d1000000-0000-4000-8000-' || lpad(to_hex(n), 12, '0'))::uuid AS event_id,
  ('d2000000-0000-4000-8000-' || lpad(to_hex(n), 12, '0'))::uuid AS session_id,
  ('d3000000-0000-4000-8000-' || lpad(to_hex(n), 12, '0'))::uuid AS type_id,
  ('d4000000-0000-4000-8000-' || lpad(to_hex(n), 12, '0'))::uuid AS inventory_id,
  (ARRAY['Music','Festival','Food','Art','Education','Business','Technology','Workshop','Networking','Sports','Fashion','Wellness'])[(n-1)%12+1] AS category,
  (ARRAY['City Sessions','Night Market','Creative Lab','Community Weekend','Open House','Discovery Day'])[(n-1)%6+1] AS format,
  (ARRAY['Sound & Stage','Local Flavours','Digital Futures','Design & Culture','Active Living','New Connections','Career Paths','Family Moments','Urban Travel','Green Ideas'])[(n-1)%10+1] AS theme,
  (ARRAY['20000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000004'])[(n-1)%3+1]::uuid AS venue_id,
  (ARRAY[
    'https://images.unsplash.com/photo-1459749411175-04bf5292ceea',
    'https://images.unsplash.com/photo-1511578314322-379afb476865',
    'https://images.unsplash.com/photo-1531058020387-3be344556be6',
    'https://images.unsplash.com/photo-1442512595331-e89e73853f31',
    'https://images.unsplash.com/photo-1524178232363-1fb2b075b655'
  ])[(n-1)%5+1] AS image_url,
  ('2027-01-10 03:00:00+00'::timestamptz + n * interval '2 days') AS starts_at,
  CASE WHEN n%20=0 THEN 'Draft' WHEN n%20=1 THEN 'Cancelled' ELSE 'Published' END AS event_status,
  CASE WHEN n%20=0 THEN 'Scheduled' WHEN n%20=1 THEN 'Cancelled' ELSE 'OnSale' END AS session_status,
  CASE WHEN n%7=0 THEN 0 ELSE ((n%8)+1)*75000 END AS price,
  80+n*7 AS capacity
FROM generate_series(1,60) AS n;

INSERT INTO events (id,organizer_id,venue_id,name,slug,description,category,image_url,status,
                    sale_starts_at_utc,sale_ends_at_utc,published_at_utc)
SELECT event_id,'10000000-0000-0000-0000-000000000001',venue_id,
       theme || ' — ' || format || ' ' || lpad(n::text,2,'0'),
       'demo-' || lpad(n::text,2,'0') || '-' || lower(replace(theme,' ','-')),
       'Chương trình demo về ' || lower(theme) || ' với hoạt động trải nghiệm, kết nối cộng đồng và vé vào cửa được quản lý trên TixFlow.',
       category,image_url,event_status,
       '2026-11-01 00:00:00+00',starts_at-interval '1 day',
       CASE WHEN event_status='Published' THEN '2026-11-01 00:00:00+00'::timestamptz ELSE NULL END
FROM demo_catalog_rows ON CONFLICT (id) DO NOTHING;

INSERT INTO event_sessions (id,event_id,name,starts_at_utc,ends_at_utc,status)
SELECT session_id,event_id,format,starts_at,starts_at+interval '5 hours',session_status
FROM demo_catalog_rows ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_types (id,event_session_id,name,code,description,price,currency,inventory_mode,capacity,max_per_order)
SELECT type_id,session_id,CASE WHEN price=0 THEN 'Free Registration' ELSE 'Standard Pass' END,
       'STANDARD','Vé tham dự chương trình demo.',price,'VND','GeneralAdmission',capacity,4
FROM demo_catalog_rows ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_inventories (id,ticket_type_id,total_quantity,held_quantity,sold_quantity,version)
SELECT inventory_id,type_id,capacity,0,0,0 FROM demo_catalog_rows ON CONFLICT (id) DO NOTHING;
COMMIT;
