-- Versioned, non-destructive and safe to rerun.
-- Adds rich profile metadata for partner organizers and assigns seeded catalog events across 6 partner organizers.
BEGIN;
SELECT pg_advisory_xact_lock(20260928, 13);

ALTER TABLE organizers ADD COLUMN IF NOT EXISTS avatar_url varchar(1000);
ALTER TABLE organizers ADD COLUMN IF NOT EXISTS website_url varchar(500);
ALTER TABLE organizers ADD COLUMN IF NOT EXISTS is_verified boolean NOT NULL DEFAULT false;
ALTER TABLE organizers ADD COLUMN IF NOT EXISTS follower_count integer NOT NULL DEFAULT 0;
ALTER TABLE organizers ADD COLUMN IF NOT EXISTS event_count integer NOT NULL DEFAULT 0;

INSERT INTO users
    (id, email, normalized_email, display_name, password_hash, role, status)
VALUES
    ('00000000-0000-0000-0000-000000000012', 'partner.techvn@tixflow.local', 'PARTNER.TECHVN@TIXFLOW.LOCAL', 'TechVN Community', 'SEED_ONLY_REPLACE_WITH_REAL_HASH', 'Organizer', 'Active'),
    ('00000000-0000-0000-0000-000000000013', 'partner.startupvn@tixflow.local', 'PARTNER.STARTUPVN@TIXFLOW.LOCAL', 'Startup Vietnam Network', 'SEED_ONLY_REPLACE_WITH_REAL_HASH', 'Organizer', 'Active'),
    ('00000000-0000-0000-0000-000000000014', 'partner.musicconnect@tixflow.local', 'PARTNER.MUSICCONNECT@TIXFLOW.LOCAL', 'Music Connect Asia', 'SEED_ONLY_REPLACE_WITH_REAL_HASH', 'Organizer', 'Active'),
    ('00000000-0000-0000-0000-000000000015', 'partner.designhub@tixflow.local', 'PARTNER.DESIGNHUB@TIXFLOW.LOCAL', 'Design & Creative Hub VN', 'SEED_ONLY_REPLACE_WITH_REAL_HASH', 'Organizer', 'Active'),
    ('00000000-0000-0000-0000-000000000016', 'partner.saigonartisan@tixflow.local', 'PARTNER.SAIGONARTISAN@TIXFLOW.LOCAL', 'Saigon Artisan & Culture', 'SEED_ONLY_REPLACE_WITH_REAL_HASH', 'Organizer', 'Active')
ON CONFLICT (id) DO NOTHING;

UPDATE organizers
SET description = 'Nhà sản xuất concert, đêm nhạc Jazz và sự kiện giải trí trực tiếp hàng đầu tại Việt Nam.',
    avatar_url = 'https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=300&q=80',
    website_url = 'https://tixflow.vn',
    is_verified = true,
    follower_count = 48500,
    event_count = 18
WHERE id = '10000000-0000-0000-0000-000000000001';

INSERT INTO organizers
    (id, owner_user_id, name, slug, description, contact_email, avatar_url, website_url, is_verified, follower_count, event_count)
VALUES
    ('10000000-0000-0000-0000-000000000002',
     '00000000-0000-0000-0000-000000000012',
     'TechVN Community',
     'techvn',
     'Cộng đồng công nghệ, AI, Data Engineering và phát triển sản phẩm lớn nhất Đông Nam Á.',
     'partner.techvn@tixflow.local',
     'https://images.unsplash.com/photo-1531482615713-2afd69097998?auto=format&fit=crop&w=300&q=80',
     'https://techvn.org',
     true,
     34200,
     27),
    ('10000000-0000-0000-0000-000000000003',
     '00000000-0000-0000-0000-000000000013',
     'Startup Vietnam Network',
     'startup-vietnam',
     'Mạng lưới kết nối nhà sáng lập, quỹ đầu tư mạo hiểm và diễn đàn lãnh đạo kinh tế số.',
     'partner.startupvn@tixflow.local',
     'https://images.unsplash.com/photo-1552664730-d307ca884978?auto=format&fit=crop&w=300&q=80',
     'https://startup.vn',
     true,
     29400,
     21),
    ('10000000-0000-0000-0000-000000000004',
     '00000000-0000-0000-0000-000000000014',
     'Music Connect Asia',
     'music-connect',
     'Đơn vị tổ chức lễ hội âm nhạc độc lập, live session và tour diễn nghệ sĩ trẻ châu Á.',
     'partner.musicconnect@tixflow.local',
     'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?auto=format&fit=crop&w=300&q=80',
     'https://musicconnect.asia',
     true,
     42800,
     15),
    ('10000000-0000-0000-0000-000000000005',
     '00000000-0000-0000-0000-000000000015',
     'Design & Creative Hub VN',
     'design-hub-vn',
     'Không gian sáng tạo quy tụ cộng đồng thiết kế sản phẩm, nghệ thuật thị giác và giáo dục hiện đại.',
     'partner.designhub@tixflow.local',
     'https://images.unsplash.com/photo-1497366216548-37526070297c?auto=format&fit=crop&w=300&q=80',
     'https://designhub.vn',
     true,
     19600,
     19),
    ('10000000-0000-0000-0000-000000000006',
     '00000000-0000-0000-0000-000000000016',
     'Saigon Artisan & Culture',
     'saigon-artisan-culture',
     'Tổ chức lễ hội văn hóa, triển lãm cà phê đặc sản và trải nghiệm ẩm thực thủ công bản địa.',
     'partner.saigonartisan@tixflow.local',
     'https://images.unsplash.com/photo-1501339847302-ac426a4a7cbb?auto=format&fit=crop&w=300&q=80',
     'https://saigonartisan.vn',
     true,
     24100,
     14)
ON CONFLICT (id) DO UPDATE
SET name = EXCLUDED.name,
    description = EXCLUDED.description,
    avatar_url = EXCLUDED.avatar_url,
    website_url = EXCLUDED.website_url,
    is_verified = EXCLUDED.is_verified,
    follower_count = EXCLUDED.follower_count,
    event_count = EXCLUDED.event_count;

-- Distribute seeded published events across the 6 partner organizers while preserving total event counts.
UPDATE events SET organizer_id = '10000000-0000-0000-0000-000000000003'
WHERE id IN ('30000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000010');

UPDATE events SET organizer_id = '10000000-0000-0000-0000-000000000004'
WHERE id IN ('30000000-0000-0000-0000-000000000003');

UPDATE events SET organizer_id = '10000000-0000-0000-0000-000000000002'
WHERE id IN ('30000000-0000-0000-0000-000000000004', '30000000-0000-0000-0000-000000000007');

UPDATE events SET organizer_id = '10000000-0000-0000-0000-000000000005'
WHERE id IN ('30000000-0000-0000-0000-000000000005', '30000000-0000-0000-0000-000000000009');

UPDATE events SET organizer_id = '10000000-0000-0000-0000-000000000006'
WHERE id IN ('30000000-0000-0000-0000-000000000006');

COMMIT;
