BEGIN;

INSERT INTO venues (id, organizer_id, name, address_line, district, city)
VALUES
    ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'Saigon Riverside Center', '12 Ton Duc Thang', 'Quận 1', 'TP. Hồ Chí Minh'),
    ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 'Hanoi Innovation Hub', '29 Lieu Giai', 'Ba Đình', 'Hà Nội'),
    ('20000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', 'Da Nang Expo Center', '09 Cach Mang Thang Tam', 'Cẩm Lệ', 'Đà Nẵng')
ON CONFLICT (id) DO NOTHING;

INSERT INTO events
    (id, organizer_id, venue_id, name, slug, description, category, image_url, status,
     sale_starts_at_utc, sale_ends_at_utc, published_at_utc)
VALUES
    ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002', 'Vietnam Future Forum 2026', 'vietnam-future-forum-2026', 'Các nhà lãnh đạo và chuyên gia cùng thảo luận về tương lai kinh tế số Việt Nam.', 'Business', 'https://images.unsplash.com/photo-1511578314322-379afb476865?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-09-15 01:00:00+00', '2026-11-14 16:59:00+00', '2026-09-15 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002', 'Saigon Indie Music Weekend', 'saigon-indie-music-weekend', 'Hai đêm nhạc độc lập quy tụ những nghệ sĩ mới nổi trong khu vực Đông Nam Á.', 'Music', 'https://images.unsplash.com/photo-1459749411175-04bf5292ceea?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-09-20 01:00:00+00', '2026-11-20 11:00:00+00', '2026-09-20 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000003', 'AI & Data Summit Vietnam', 'ai-data-summit-vietnam', 'Hội nghị thực tiễn về AI, dữ liệu và cách doanh nghiệp đưa mô hình vào sản xuất.', 'Technology', 'https://images.unsplash.com/photo-1485827404703-89b55fcc595e?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-09-20 01:00:00+00', '2026-11-27 11:00:00+00', '2026-09-20 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000004', 'Creative Talk: Design Tomorrow', 'creative-talk-design-tomorrow', 'Một ngày dành cho nhà thiết kế, người làm sản phẩm và cộng đồng sáng tạo.', 'Art', 'https://images.unsplash.com/photo-1531058020387-3be344556be6?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-09-25 01:00:00+00', '2026-12-04 11:00:00+00', '2026-09-25 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002', 'The Coffee & Culture Fair', 'coffee-culture-fair', 'Trải nghiệm cà phê Việt Nam từ nông trại đến ly cùng các nhà rang xay địa phương.', 'Food', 'https://images.unsplash.com/photo-1442512595331-e89e73853f31?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-10-01 01:00:00+00', '2026-12-11 11:00:00+00', '2026-10-01 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000007', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000003', 'Hanoi Product Meetup 18', 'hanoi-product-meetup-18', 'Các câu chuyện sản phẩm chân thật và cơ hội kết nối cộng đồng tại Hà Nội.', 'Networking', 'https://images.unsplash.com/photo-1517245386807-bb43f82c33c4?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-10-02 01:00:00+00', '2026-12-17 11:00:00+00', '2026-10-02 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000008', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002', 'Saigon Jazz Under the Stars', 'saigon-jazz-under-the-stars', 'Đêm nhạc jazz ngoài trời dành cho những người yêu âm nhạc và thành phố.', 'Music', 'https://images.unsplash.com/photo-1516280440614-37939bbacd81?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-10-03 01:00:00+00', '2026-12-24 11:00:00+00', '2026-10-03 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000009', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000004', 'Future Skills for Everyone', 'future-skills-for-everyone', 'Chuỗi workshop thực hành về những kỹ năng cần thiết trong thế giới việc làm mới.', 'Education', 'https://images.unsplash.com/photo-1524178232363-1fb2b075b655?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-10-04 01:00:00+00', '2027-01-07 11:00:00+00', '2026-10-04 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000010', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000003', 'Startup Showcase Vietnam', 'startup-showcase-vietnam', 'Đêm pitching trực tiếp dành cho các đội ngũ khởi nghiệp giai đoạn đầu.', 'Business', 'https://images.unsplash.com/photo-1556761175-b413da4baf72?auto=format&fit=crop&w=1600&q=82', 'Published', '2026-10-05 01:00:00+00', '2027-01-14 11:00:00+00', '2026-10-05 01:00:00+00'),
    ('30000000-0000-0000-0000-000000000011', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002', 'Circular Design Workshop', 'circular-design-workshop', 'Workshop thiết kế sản phẩm bền vững từ những vật liệu đã có quanh ta.', 'Workshop', 'https://images.unsplash.com/photo-1455390582262-044cdead277a?auto=format&fit=crop&w=1600&q=82', 'Draft', '2026-11-01 01:00:00+00', '2027-01-20 11:00:00+00', NULL),
    ('30000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000004', 'Coastal Food Festival', 'coastal-food-festival', 'Lễ hội ẩm thực ven biển với đầu bếp và nhà sản xuất địa phương.', 'Food', 'https://images.unsplash.com/photo-1555939594-58d7cb561ad1?auto=format&fit=crop&w=1600&q=82', 'Cancelled', '2026-09-01 01:00:00+00', '2026-11-30 11:00:00+00', NULL)
ON CONFLICT (id) DO NOTHING;

INSERT INTO event_sessions (id, event_id, name, starts_at_utc, ends_at_utc, status)
VALUES
    ('40000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000002', 'Diễn đàn chính', '2026-11-15 02:00:00+00', '2026-11-15 10:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003', 'Đêm nhạc thứ nhất', '2026-11-21 12:00:00+00', '2026-11-21 16:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000004', '30000000-0000-0000-0000-000000000004', 'Hội nghị chính', '2026-11-28 02:00:00+00', '2026-11-28 10:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000005', '30000000-0000-0000-0000-000000000005', 'Creative day', '2026-12-05 02:00:00+00', '2026-12-05 10:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000006', '30000000-0000-0000-0000-000000000006', 'Ngày hội cà phê', '2026-12-12 02:00:00+00', '2026-12-12 10:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000007', '30000000-0000-0000-0000-000000000007', 'Meetup tối', '2026-12-18 11:30:00+00', '2026-12-18 14:30:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000008', '30000000-0000-0000-0000-000000000008', 'Jazz night', '2026-12-25 12:00:00+00', '2026-12-25 15:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000009', '30000000-0000-0000-0000-000000000009', 'Ngày workshop', '2027-01-08 02:00:00+00', '2027-01-08 09:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000010', 'Pitching night', '2027-01-15 11:00:00+00', '2027-01-15 15:00:00+00', 'OnSale'),
    ('40000000-0000-0000-0000-000000000011', '30000000-0000-0000-0000-000000000011', 'Workshop chính', '2027-01-21 02:00:00+00', '2027-01-21 09:00:00+00', 'Scheduled'),
    ('40000000-0000-0000-0000-000000000012', '30000000-0000-0000-0000-000000000012', 'Festival day', '2026-12-01 02:00:00+00', '2026-12-01 11:00:00+00', 'Cancelled')
ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_types
    (id, event_session_id, name, code, description, price, currency, inventory_mode, capacity, max_per_order)
VALUES
    ('50000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000002', 'Standard', 'STANDARD', 'Vé tham dự tiêu chuẩn.', 650000, 'VND', 'GeneralAdmission', 1200, 6),
    ('50000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000003', 'General Admission', 'GENERAL', 'Vé vào cửa hai đêm nhạc.', 350000, 'VND', 'GeneralAdmission', 700, 6),
    ('50000000-0000-0000-0000-000000000005', '40000000-0000-0000-0000-000000000004', 'Conference Pass', 'PASS', 'Vé trọn ngày hội nghị.', 890000, 'VND', 'GeneralAdmission', 1800, 6),
    ('50000000-0000-0000-0000-000000000006', '40000000-0000-0000-0000-000000000005', 'Creative Pass', 'PASS', 'Vé tham dự chương trình sáng tạo.', 420000, 'VND', 'GeneralAdmission', 450, 6),
    ('50000000-0000-0000-0000-000000000007', '40000000-0000-0000-0000-000000000006', 'Day Pass', 'DAY', 'Vé trải nghiệm ngày hội.', 120000, 'VND', 'GeneralAdmission', 900, 8),
    ('50000000-0000-0000-0000-000000000008', '40000000-0000-0000-0000-000000000007', 'Community', 'COMMUNITY', 'Vé miễn phí có đăng ký.', 0, 'VND', 'GeneralAdmission', 180, 2),
    ('50000000-0000-0000-0000-000000000009', '40000000-0000-0000-0000-000000000008', 'Jazz Pass', 'PASS', 'Vé khu vực phổ thông.', 280000, 'VND', 'GeneralAdmission', 280, 4),
    ('50000000-0000-0000-0000-000000000010', '40000000-0000-0000-0000-000000000009', 'Workshop Pass', 'PASS', 'Vé trọn ngày workshop.', 150000, 'VND', 'GeneralAdmission', 3000, 10),
    ('50000000-0000-0000-0000-000000000011', '40000000-0000-0000-0000-000000000010', 'Showcase Pass', 'PASS', 'Vé tham dự pitching night.', 200000, 'VND', 'GeneralAdmission', 350, 4)
ON CONFLICT (id) DO NOTHING;

INSERT INTO ticket_inventories (id, ticket_type_id, total_quantity, held_quantity, sold_quantity, version)
VALUES
    ('70000000-0000-0000-0000-000000000003', '50000000-0000-0000-0000-000000000003', 1200, 0, 124, 0),
    ('70000000-0000-0000-0000-000000000004', '50000000-0000-0000-0000-000000000004', 700, 0, 211, 0),
    ('70000000-0000-0000-0000-000000000005', '50000000-0000-0000-0000-000000000005', 1800, 0, 386, 0),
    ('70000000-0000-0000-0000-000000000006', '50000000-0000-0000-0000-000000000006', 450, 0, 97, 0),
    ('70000000-0000-0000-0000-000000000007', '50000000-0000-0000-0000-000000000007', 900, 0, 302, 0),
    ('70000000-0000-0000-0000-000000000008', '50000000-0000-0000-0000-000000000008', 180, 0, 73, 0),
    ('70000000-0000-0000-0000-000000000009', '50000000-0000-0000-0000-000000000009', 280, 0, 119, 0),
    ('70000000-0000-0000-0000-000000000010', '50000000-0000-0000-0000-000000000010', 3000, 0, 642, 0),
    ('70000000-0000-0000-0000-000000000011', '50000000-0000-0000-0000-000000000011', 350, 0, 81, 0)
ON CONFLICT (id) DO NOTHING;

COMMIT;
