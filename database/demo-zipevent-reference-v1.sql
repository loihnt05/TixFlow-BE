-- Public listing references checked on 2026-11-10 at https://www.zipeventapp.com/event/allevent.
-- Titles, date ranges and venues are factual metadata. No Zipevent descriptions, images, prices or tickets are copied.
-- Catalog-only demo: no ticket_types/inventory, so TixFlow cannot sell these third-party events.
-- Run with psql -v ON_ERROR_STOP=1 -v target_db=tixflow (or tixflow_acceptance).
BEGIN;
SELECT set_config('app.demo_seed_target', :'target_db', true);
DO $$ BEGIN
  IF current_database() NOT IN ('tixflow', 'tixflow_acceptance')
     OR current_database() <> current_setting('app.demo_seed_target') THEN
    RAISE EXCEPTION 'Zipevent reference seed refused on %', current_database();
  END IF;
END $$;

CREATE TEMP TABLE zipevent_references (
  n integer PRIMARY KEY, title text, source_url text, category text,
  venue_name text, city text, starts_at timestamptz, ends_at timestamptz
) ON COMMIT DROP;

INSERT INTO zipevent_references VALUES
 (1,'บ้านพิพิธภัณฑ์ คุณาวงศ์ Kunawong House Museum','https://www.zipeventapp.com/e/Kunawong-House-Museum','Art','Kunawong House Museum','Bangkok','2025-01-05 09:30+07','2026-12-27 18:00+07'),
 (2,'Space & Time Cube+','https://www.zipeventapp.com/e/Space-Time-Cube','Art','Seacon Bangkae','Bangkok','2025-03-01 09:00+07','2027-02-28 18:00+07'),
 (3,'Fahlanna Art Museum','https://www.zipeventapp.com/e/Fahlanna-Art-Museum','Art','FAM Fahlanna Art Museum','Chiang Mai','2025-10-10 09:00+07','2028-10-31 18:00+07'),
 (4,'Space & Time Cube Pattaya','https://www.zipeventapp.com/e/Space-Time-Cube-Pattaya','Art','Central Pattaya','Pattaya','2025-12-09 09:00+07','2026-12-08 18:00+07'),
 (5,'Material Submission Showcase - FROM LOCAL TO GLOBAL','https://www.zipeventapp.com/e/Material-Submission-Showcase-FROM-LOCAL-TO-GLOBAL-2473DD17','Art','TCDC Bangkok','Bangkok','2026-01-29 09:00+07','2026-12-30 18:00+07'),
 (6,'Cave Fantasy','https://www.zipeventapp.com/e/Cave-Fantasy','Family','MBK CENTER','Bangkok','2026-02-01 09:00+07','2027-02-01 18:00+07'),
 (7,'Alpha Skills Summit & Expo 2026','https://www.zipeventapp.com/e/alphaskillssummit2026','Education',NULL,NULL,'2026-05-12 10:00+07','2026-11-12 23:59+07'),
 (8,'Mission To The Moon Forum 2026 : Work-Life Evolution','https://www.zipeventapp.com/e/Mission-to-the-Moon-Forum-2026','Business',NULL,NULL,'2026-05-30 09:00+07','2026-12-07 18:00+07'),
 (9,'CREATIVE TALK CONFERENCE 2026 The Festival of Futuristic Minds','https://www.zipeventapp.com/e/ctc2026','Business',NULL,NULL,'2026-06-19 09:00+07','2026-12-20 18:00+07'),
 (10,'Das Malspiel (Play of Painting)','https://www.zipeventapp.com/e/Das-Malspiel-Play-of-Painting-74F040FD','Art','MAIIAM Contemporary Art Museum','Chiang Mai','2026-06-20 09:00+07','2027-06-21 18:00+07'),
 (11,'The Secret Sauce Business Weekend อีสาน 2026','https://www.zipeventapp.com/e/The-Secret-Sauce-Business-Weekend-Isan-2026','Business',NULL,NULL,'2026-07-19 09:00+07','2027-01-25 18:00+07'),
 (12,'THE SECRET SAUCE SUMMIT 2026 : THE AGE OF BUILDERS','https://www.zipeventapp.com/e/THE-SECRET-SAUCE-SUMMIT-2026-THE-AGE-OF-BUILDERS','Business',NULL,NULL,'2026-09-09 09:00+07','2027-03-18 18:00+07'),
 (13,'FOOD TRUCK BEACH FOOD FEST','https://www.zipeventapp.com/e/FOOD-TRUCK-BEACH-FOOD-FEST-640B7E04','Food','Central Pattaya','Pattaya','2026-10-01 09:00+07','2026-11-30 18:00+07'),
 (14,'AT DOOR TICKET - SANGSOM PRESENTS VOLUME PHASE 8','https://www.zipeventapp.com/e/AT-DOOR-SANGSOM-PRESENTS-VOLUME-PHASE-8','Music','The Street Ratchada','Bangkok','2026-10-10 09:00+07','2026-12-19 18:00+07');

INSERT INTO venues (id,organizer_id,name,address_line,city,country_code,time_zone)
SELECT ('e2000000-0000-4000-8000-' || lpad(to_hex(min(n)),12,'0'))::uuid,
       '10000000-0000-0000-0000-000000000001',venue_name,'See original Zipevent listing',city,'TH','Asia/Bangkok'
FROM zipevent_references WHERE venue_name IS NOT NULL GROUP BY venue_name,city
ON CONFLICT (id) DO NOTHING;

INSERT INTO events (id,organizer_id,venue_id,name,slug,description,category,image_url,status,published_at_utc)
SELECT ('e1000000-0000-4000-8000-' || lpad(to_hex(r.n),12,'0'))::uuid,
       '10000000-0000-0000-0000-000000000001',v.id,r.title,
       'zipevent-reference-' || lpad(r.n::text,2,'0'),
       'Tham khảo danh mục Zipevent: ' || r.source_url || '. Đây là bản ghi demo, TixFlow không bán vé sự kiện này.',
       r.category,NULL,'Published','2026-11-10 00:00+00'
FROM zipevent_references r LEFT JOIN venues v ON v.name=r.venue_name AND v.id::text LIKE 'e2000000-%'
ON CONFLICT (id) DO NOTHING;

-- A catalog date range, not a claim about every individual show time.
INSERT INTO event_sessions (id,event_id,name,starts_at_utc,ends_at_utc,status)
SELECT ('e3000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid,
       ('e1000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid,
       'Khoảng ngày theo nguồn Zipevent',starts_at,ends_at,'Scheduled'
FROM zipevent_references ON CONFLICT (id) DO NOTHING;
COMMIT;
