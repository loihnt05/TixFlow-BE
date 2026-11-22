-- Additional public Zipevent listings checked on 2026-11-20.
-- Factual titles, date ranges, venues and source URLs only. Times below mark whole
-- calendar days, not individual show times. No images, prices or tickets are copied.
-- Catalog-only demo: these third-party events cannot be sold through TixFlow.
-- Run after the baseline seed and migrations with psql -v ON_ERROR_STOP=1 -v target_db=tixflow.
BEGIN;
SELECT set_config('app.demo_seed_target', :'target_db', true);
DO $$ BEGIN
  IF current_database() NOT IN ('tixflow', 'tixflow_acceptance')
     OR current_database() <> current_setting('app.demo_seed_target') THEN
    RAISE EXCEPTION 'Zipevent reference v2 seed refused on %', current_database();
  END IF;
  IF NOT EXISTS (SELECT 1 FROM organizers WHERE id = '10000000-0000-0000-0000-000000000001') THEN
    RAISE EXCEPTION 'Baseline catalog is missing';
  END IF;
END $$;

CREATE TEMP TABLE zipevent_references_v2 (
  n integer PRIMARY KEY, title text NOT NULL, source_url text NOT NULL UNIQUE,
  category text NOT NULL, venue_name text, city text,
  first_day date NOT NULL, last_day date NOT NULL,
  CHECK (last_day >= first_day),
  CHECK ((venue_name IS NULL) = (city IS NULL))
) ON COMMIT DROP;

INSERT INTO zipevent_references_v2 VALUES
 (1,'PLANET EARTH III - LIVE IN CONCERT','https://www.zipeventapp.com/e/PLANET-EARTH-III--LIVE-IN-CONCERT','Music','Wat Arun Ratchawararam Ratchawaramahawihan','Bangkok','2026-12-03','2026-12-03'),
 (2,'FRIEREN : BEYOND JOURNEY’S END EXHIBITION นิทรรศการการเดินทางของฟรีเรนครั้งแรกในประเทศไทย','https://www.zipeventapp.com/e/BEYOND-JOURNEYS-END-EXHIBITION','Art','JAM SPACE, MBK Center','Bangkok','2026-10-31','2027-01-03'),
 (3,'2026 KIM JI WON FANMEETING <WONEDERLAND> IN BANGKOK','https://www.zipeventapp.com/e/KJW-WONEDERLAND-BKK','Music','SiamPic Hall, Siam Square One','Bangkok','2026-11-28','2026-11-28'),
 (4,'ที่รักสารวัตรเธียร์ l Police in Love - FINAL EPISODE','https://www.zipeventapp.com/e/POLICE-IN-LOVE-FINAL-EPISODE-ROUND2','Music','MCC Hall, The Mall Lifestore Ngamwongwan','Nonthaburi','2026-11-28','2026-11-28'),
 (5,'2026 LEE JE HOON FANMEETING (Our 20th Moment) IN BKK','https://www.zipeventapp.com/e/Lee-Je-Hoon-Fanmeeting-in-bkk','Music','Phenix Grand Ballroom','Bangkok','2026-11-28','2026-11-28'),
 (6,'THE STANDARD ECONOMIC FORUM 2026: Reinvent Thailand','https://www.zipeventapp.com/e/ECONOMICFORUM2026','Business',NULL,NULL,'2026-11-04','2027-05-16'),
 (7,'After Hours','https://www.zipeventapp.com/e/Sala-Saneha-After-Hours','Art','Sala Saneha','Bangkok','2026-10-01','2026-11-30'),
 (8,'Tame','https://www.zipeventapp.com/e/Sala-Saneha-Tame','Art','Sala Saneha','Bangkok','2026-11-01','2026-11-30'),
 (9,'NAZA','https://www.zipeventapp.com/e/Sala-Saneha-NAZA','Art','Sala Saneha','Bangkok','2026-10-01','2026-11-30'),
 (10,'9 temples to heaven','https://www.zipeventapp.com/e/Sala-Saneha-9-temples-to-heaven','Art','Sala Saneha','Bangkok','2026-11-01','2026-11-30'),
 (11,'Marketing Conference 2026: Agentic Marketing','https://www.zipeventapp.com/e/mktcon2026','Business',NULL,NULL,'2026-09-29','2027-04-02'),
 (12,'"ก่อนจะเป็นมิวสิคัล" (The Ensemble) an INTERACTIVE IMMERSIVE MUSICAL EXPERIENCE','https://www.zipeventapp.com/e/The-Ensemble','Music','POEMS DIMENSION (Town in Town)','Bangkok','2026-11-20','2026-11-29'),
 (13,'Dropship Live in Bangkok','https://www.zipeventapp.com/e/Dropship-Live-in-Bangkok','Music','VOLUME LIVEHOUSE, THE STREET RATCHADA','Bangkok','2026-11-20','2026-11-20'),
 (14,'CAT EXPO BANGKOK 2026','https://www.zipeventapp.com/e/CAT-EXPO-BANGKOK-2026','Music','Siam Amazing Park','Bangkok','2026-11-21','2026-11-22'),
 (15,'3rd Thai Traditional Games Festival: Heroes Battle Thailand 2026','https://www.zipeventapp.com/e/Heroes-Battle-Thailand-2026-168ABED7','Festival','Bhiraj Convention Centre, BITEC Bangna','Bangkok','2026-11-21','2026-11-22'),
 (16,'Zato Presents VOL. IS NOW','https://www.zipeventapp.com/e/VOL-IS-NOW','Music','VOLUME LIVEHOUSE, THE STREET RATCHADA','Bangkok','2026-11-21','2026-11-21'),
 (17,'EJAN Fair x Moon Rim Lake 2026','https://www.zipeventapp.com/e/EJAN-Fair-x-Moon-Rim-Lake-2026-94B003A2','Festival','IMPACT Muang Thong Thani','Nonthaburi','2026-11-21','2026-11-25'),
 (18,'Chill at The Museum','https://www.zipeventapp.com/e/Chill-at-The-Museum-E525ECF2','Art','Museum Siam','Bangkok','2026-11-21','2027-04-24'),
 (19,'2026 Phra Pathom Chedi Worship Festival','https://www.zipeventapp.com/e/2026-Phra-Pathom-Chedi-Worship-Festival-AFB13C10','Festival','Phra Pathom Chedi','Nakhon Pathom','2026-11-21','2026-11-29'),
 (20,'Chiangmai Heritage Sky Lanterns Festival','https://www.zipeventapp.com/e/Chiangmai-Heritage-Sky-Lanterns-Festival','Festival','Pang Fan Rock''n River','Chiang Mai','2026-11-24','2026-11-24'),
 (21,'Chiangmai Wonderland Sky Lanterns Festival 2026','https://www.zipeventapp.com/e/Chiangmai-Wonderland-Sky-Lanterns-Festival-2026','Festival',NULL,NULL,'2026-11-24','2026-11-25'),
 (22,'48 Hour Film Project Bangkok 2026 — Film Screenings','https://www.zipeventapp.com/e/48-Hour-Film-Project-Bangkok-2026-Film-Screenings','Art','Century The Movie Plaza Sukhumvit','Bangkok','2026-11-28','2026-11-29'),
 (23,'ZATO Presents TheBiirthdayParty inthecolorofdepression Album Launch Party','https://www.zipeventapp.com/e/Volume-TheBiirthdayParty-inthecolorofdepression-Album-Launch-Party','Music','VOLUME LIVEHOUSE, THE STREET RATCHADA','Bangkok','2026-11-28','2026-11-28'),
 (24,'Playlist Live','https://www.zipeventapp.com/e/PLAYLISTLIVE','Music','Robinson Lifestyle Ratchaphruek','Nonthaburi','2026-11-22','2026-11-22'),
 (25,'ZATO PRESENTS BASE 024 : YOUTH BRUSH SPECIAL SHOW','https://www.zipeventapp.com/e/Volume-Base-024','Music','VOLUME LIVEHOUSE, THE STREET RATCHADA','Bangkok','2026-12-24','2026-12-24'),
 (26,'BLKGEM CONCERT','https://www.zipeventapp.com/e/BLKGEM-CONCERT','Music','Bhiraj Hall, BITEC Bangna','Bangkok','2026-12-06','2026-12-06'),
 (27,'ZATO Presents Hariguem Zaboy Honking Continues Album Launch Party','https://www.zipeventapp.com/e/Volume-Hariguem-Zaboy-Album-Launch','Music','VOLUME LIVEHOUSE, THE STREET RATCHADA','Bangkok','2026-12-04','2026-12-04'),
 (28,'The Great Fundraising Masterclass in Bangkok 2027 [International Applicants]','https://www.zipeventapp.com/e/the-great-fundraising-masterclass-in-bangkok-2027-international-applicants','Education','Goethe-Institute Thailand','Bangkok','2027-01-21','2027-01-22'),
 (29,'The Bells Rang Again - The New Musical','https://www.zipeventapp.com/e/The-Bells-Rang-Again-The-New-Musical','Music','POEMS DIMENSION (Town in Town)','Bangkok','2026-12-11','2026-12-20'),
 (30,'Comic Avenue 12','https://www.zipeventapp.com/e/comic-avenue-12','Art','Westgate Hall, Central Westgate','Nonthaburi','2026-12-05','2026-12-06'),
 (31,'Follow the Future 2026','https://legacy.zipeventapp.com/e/Follow-The-Future-2026','Business','Grand Hall, True Digital Park West','Bangkok','2026-11-21','2026-11-21'),
 (32,'The 3 Asia-Pacific Exotic Animal Veterinary Conference','https://legacy.zipeventapp.com/e/The-3-Asia-Pacific-Exotic-Animal-Veterinary-Conference','Education','IMPACT Forum Building','Nonthaburi','2026-11-30','2026-12-03'),
 (33,'A Safe Journey With Her','https://www.zipeventapp.com/e/A-Safe-Journey-With-Her-2026','Sports','Wachirabenchathat Park','Bangkok','2026-12-06','2026-12-06'),
 (34,'Love in Silence','https://www.zipeventapp.com/e/Love-in-Silence-2A328EFE','Fashion','Maison JE','Bangkok','2026-10-31','2026-12-24'),
 (35,'Cinema in the Secret Garden by SITNEY','https://www.zipeventapp.com/e/Cinema-in-the-Secret-Garden-by-SITNEY','Art','Ten Six Hundred, Chao Phraya','Bangkok','2026-12-18','2026-12-20');

INSERT INTO venues (id,organizer_id,name,address_line,city,country_code,time_zone)
SELECT ('e5000000-0000-4000-8000-' || lpad(to_hex(min(n)),12,'0'))::uuid,
       '10000000-0000-0000-0000-000000000001',venue_name,
       'See original Zipevent listing',city,'TH','Asia/Bangkok'
FROM zipevent_references_v2 WHERE venue_name IS NOT NULL GROUP BY venue_name,city
ON CONFLICT (id) DO NOTHING;

INSERT INTO events (id,organizer_id,venue_id,name,slug,description,category,image_url,status,published_at_utc)
SELECT ('e4000000-0000-4000-8000-' || lpad(to_hex(r.n),12,'0'))::uuid,
       '10000000-0000-0000-0000-000000000001',v.id,r.title,
       'zipevent-reference-v2-' || lpad(r.n::text,2,'0'),
       'Nguồn tham khảo: ' || r.source_url || '. Bản ghi chỉ để xem; TixFlow không bán vé sự kiện này.',
       CASE r.category
         WHEN 'Festival' THEN 'Entertainment'
         WHEN 'Sports' THEN 'Health'
         WHEN 'Fashion' THEN 'Art'
         ELSE r.category
       END,NULL,'Published','2026-11-20 00:00+00'
FROM zipevent_references_v2 r
LEFT JOIN venues v ON v.name=r.venue_name AND v.city=r.city AND v.id::text LIKE 'e5000000-%'
ON CONFLICT (id) DO UPDATE SET venue_id = EXCLUDED.venue_id
WHERE events.venue_id IS NULL AND EXCLUDED.venue_id IS NOT NULL;

INSERT INTO event_sessions (id,event_id,name,starts_at_utc,ends_at_utc,status)
SELECT ('e6000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid,
       ('e4000000-0000-4000-8000-' || lpad(to_hex(n),12,'0'))::uuid,
       'Khoảng ngày theo nguồn Zipevent',
       first_day::timestamp AT TIME ZONE 'Asia/Bangkok',
       (last_day::timestamp + interval '23 hours 59 minutes') AT TIME ZONE 'Asia/Bangkok',
       'Scheduled'
FROM zipevent_references_v2 ON CONFLICT (id) DO NOTHING;
COMMIT;
