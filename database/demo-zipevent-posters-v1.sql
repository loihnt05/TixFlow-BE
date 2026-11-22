-- Public portrait poster URLs collected from the corresponding Zipevent event pages on 2026-11-20.
-- Apply after demo-zipevent-reference-v1.sql and demo-zipevent-reference-v2.sql.
-- Portrait URLs reference Zipevent-hosted images; this seed does not copy image binaries.
BEGIN;
SELECT set_config('app.demo_seed_target', :'target_db', true);
DO $$ BEGIN
  IF current_database() NOT IN ('tixflow', 'tixflow_acceptance')
     OR current_database() <> current_setting('app.demo_seed_target') THEN
    RAISE EXCEPTION 'Zipevent poster seed refused on %', current_database();
  END IF;
END $$;

CREATE TEMP TABLE zipevent_posters (
  slug text PRIMARY KEY, source_url text NOT NULL, image_url text NOT NULL UNIQUE
) ON COMMIT DROP;

INSERT INTO zipevent_posters VALUES
 ('zipevent-reference-01','https://www.zipeventapp.com/e/Kunawong-House-Museum','https://cdnali.zipeventapp.com/images/events/20517C46-CE59-414B-9D36-B16967F8007B/xl-F3F2A2F9-0F5A-4A7E-AA21-843EF2767FB4.jpg.webp'),
 ('zipevent-reference-02','https://www.zipeventapp.com/e/Space-Time-Cube','https://cdnali.zipeventapp.com/images/events/46286ED9-2BA5-4526-B1E2-AF0760186611/7986B3D2-5642-417C-97D3-535B8B1B57A0.jpg'),
 ('zipevent-reference-03','https://www.zipeventapp.com/e/Fahlanna-Art-Museum','https://cdnali.zipeventapp.com/images/events/5D839D0E-DF53-494A-9791-0917A7CB07F2/xl-30B95D7E-65F6-4D3B-892C-E360EF275AD0.jpg.webp'),
 ('zipevent-reference-04','https://www.zipeventapp.com/e/Space-Time-Cube-Pattaya','https://cdnali.zipeventapp.com/images/events/A7ABC828-64A6-4FB0-ACA3-60693397E938/97A6702B-40C1-4878-B47F-4525CF71B646.jpg'),
 ('zipevent-reference-05','https://www.zipeventapp.com/e/Material-Submission-Showcase-FROM-LOCAL-TO-GLOBAL-2473DD17','https://cdnali.zipeventapp.com/images/events/DFD283A2-D89C-47BD-AD79-1CD0943C20EA/xl-71EB69A4-5505-43A3-AAC2-8240CEE9A21F.jpg.webp'),
 ('zipevent-reference-06','https://www.zipeventapp.com/e/Cave-Fantasy','https://cdnali.zipeventapp.com/images/events/AC86ED1B-8B6F-4AE0-B508-B01345379DF0/94CB23E8-7AE0-4419-BCDB-CC25175A019E.png'),
 ('zipevent-reference-07','https://www.zipeventapp.com/e/alphaskillssummit2026','https://cdnali.zipeventapp.com/images/events/BDCF9CBE-28F8-4010-9CF3-262ED516A2F0/5A9D26D4-D1C0-4180-A587-1C2E781224FD.jpg'),
 ('zipevent-reference-08','https://www.zipeventapp.com/e/Mission-to-the-Moon-Forum-2026','https://cdnali.zipeventapp.com/images/events/8F6B222D-926E-4C58-8BF8-BD488F115C82/xl-E9D48F33-873C-49D3-B1F3-344F1C668C28.jpg.webp'),
 ('zipevent-reference-09','https://www.zipeventapp.com/e/ctc2026','https://cdnali.zipeventapp.com/images/events/14B40A10-90E7-4CE4-B7D9-D1E8FB51CA31/xl-95A0C46E-7A75-4D27-8E74-8679C24C35AB.jpg.webp'),
 ('zipevent-reference-10','https://www.zipeventapp.com/e/Das-Malspiel-Play-of-Painting-74F040FD','https://cdnali.zipeventapp.com/images/events/F578D1C6-E6C1-4F55-BC3B-FC5D7605BF1F/xl-98E9B563-98BD-4BC6-870A-CEB67D8436B8.jpg.webp'),
 ('zipevent-reference-11','https://www.zipeventapp.com/e/The-Secret-Sauce-Business-Weekend-Isan-2026','https://cdnali.zipeventapp.com/images/events/71F52751-4BF9-44D3-9DE9-4A5A650EE3E9/33DAFD49-25EF-442F-A19F-82C8B92A3DBD.jpg'),
 ('zipevent-reference-12','https://www.zipeventapp.com/e/THE-SECRET-SAUCE-SUMMIT-2026-THE-AGE-OF-BUILDERS','https://cdnali.zipeventapp.com/images/events/1626704E-92CC-42F3-9E69-130080F62EC9/9FFA86E0-CF6A-4455-825B-7D78B92CD3E8.jpg'),
 ('zipevent-reference-13','https://www.zipeventapp.com/e/FOOD-TRUCK-BEACH-FOOD-FEST-640B7E04','https://cdnali.zipeventapp.com/images/events/DA2C3F4B-913A-4516-AFB4-D3926DAC114F/xl-2B5B4BE6-0D11-40CE-8612-6F5C005611E2.jpg.webp'),
 ('zipevent-reference-14','https://www.zipeventapp.com/e/AT-DOOR-SANGSOM-PRESENTS-VOLUME-PHASE-8','https://cdnali.zipeventapp.com/images/events/FFC22F54-3E13-4085-860B-3A1070A3103C/xl-4A20EBC9-35ED-4112-886D-0C85702951F1.jpg.webp'),
 ('zipevent-reference-v2-01','https://www.zipeventapp.com/e/PLANET-EARTH-III--LIVE-IN-CONCERT','https://cdnali.zipeventapp.com/images/events/74A2C15D-1BE5-43FA-A8E3-B70A08628C9A/xl-7FA865C3-D90A-491B-ACAE-D2B5ACFFDABF.jpg.webp'),
 ('zipevent-reference-v2-02','https://www.zipeventapp.com/e/BEYOND-JOURNEYS-END-EXHIBITION','https://cdnali.zipeventapp.com/images/events/5ACD2FB9-079F-4146-871B-DF99A64D1FD5/xl-3AE3E662-4910-4B1E-BA90-AB42416FF620.jpg.webp'),
 ('zipevent-reference-v2-03','https://www.zipeventapp.com/e/KJW-WONEDERLAND-BKK','https://cdnali.zipeventapp.com/images/events/261BA914-1E99-447B-A74C-0C7B0978C36E/9BC6E4B2-FDAA-4A71-B4AB-76A1E5A8211F.png'),
 ('zipevent-reference-v2-04','https://www.zipeventapp.com/e/POLICE-IN-LOVE-FINAL-EPISODE-ROUND2','https://cdnali.zipeventapp.com/images/events/73B495C8-8530-400B-BEC0-CF26B78B5CFE/6CC8E949-92CD-4101-AC55-E1490FB98C81.jpg'),
 ('zipevent-reference-v2-05','https://www.zipeventapp.com/e/Lee-Je-Hoon-Fanmeeting-in-bkk','https://cdnali.zipeventapp.com/images/events/59FD31EF-FCC9-4816-9CC9-58A72BCF27B5/94136F7A-C438-4B01-AFA0-2D1C579E409D.jpg'),
 ('zipevent-reference-v2-06','https://www.zipeventapp.com/e/ECONOMICFORUM2026','https://cdnali.zipeventapp.com/images/events/C74F96D8-C6A4-4F5C-B9E3-606EF2ACBEF7/xl-CD8AFBAB-4B6B-4651-8CF5-066C020FA4E4.jpg.webp'),
 ('zipevent-reference-v2-07','https://www.zipeventapp.com/e/Sala-Saneha-After-Hours','https://cdnali.zipeventapp.com/images/events/2D238DB5-3A08-4A7D-8D12-63130A88F072/xl-C349C925-3758-40DA-9755-6DDB78C1A198.jpg.webp'),
 ('zipevent-reference-v2-08','https://www.zipeventapp.com/e/Sala-Saneha-Tame','https://cdnali.zipeventapp.com/images/events/CAB3EE0A-396B-401D-9DA6-E096A20F9EA4/xl-AAB761EB-6001-463A-8C71-A355788D9602.jpg.webp'),
 ('zipevent-reference-v2-09','https://www.zipeventapp.com/e/Sala-Saneha-NAZA','https://cdnali.zipeventapp.com/images/events/03135A55-B472-4D04-BFCC-BA29938ED919/xl-214BBF7A-5ECC-42D6-861A-91C959CE2832.jpg.webp'),
 ('zipevent-reference-v2-10','https://www.zipeventapp.com/e/Sala-Saneha-9-temples-to-heaven','https://cdnali.zipeventapp.com/images/events/2CB681C0-0A1D-40E6-B778-2BCAD4078343/xl-D2A276BC-1C49-47ED-893C-9EDC943AB337.jpg.webp'),
 ('zipevent-reference-v2-11','https://www.zipeventapp.com/e/mktcon2026','https://cdnali.zipeventapp.com/images/events/2863A5AD-B24F-4A38-B62E-72DDE8A5D12D/966F9B93-11B5-4D9C-96BF-297108DE45AC.jpg'),
 ('zipevent-reference-v2-12','https://www.zipeventapp.com/e/The-Ensemble','https://cdnali.zipeventapp.com/images/events/D47CA3E5-AAFC-4C22-A212-B9C0756EFBDF/xl-60BCCA38-41E9-4193-994E-12F1369A0428.jpg.webp'),
 ('zipevent-reference-v2-13','https://www.zipeventapp.com/e/Dropship-Live-in-Bangkok','https://cdnali.zipeventapp.com/images/events/B9AFC4E5-78EA-4156-AE8E-1829A511CB6B/xl-2835FD67-1360-4B17-951B-C61E7CBAF994.jpg.webp'),
 ('zipevent-reference-v2-14','https://www.zipeventapp.com/e/CAT-EXPO-BANGKOK-2026','https://cdnali.zipeventapp.com/images/events/62D54274-8A87-4CAE-B07C-BFD2AD4FEF9D/xl-67668886-4778-466A-A797-1335BD9FF506.jpg.webp'),
 ('zipevent-reference-v2-15','https://www.zipeventapp.com/e/Heroes-Battle-Thailand-2026-168ABED7','https://cdnali.zipeventapp.com/images/events/DA53EA56-DB5B-456C-9A49-877AEAD5FE1E/xl-E8BBF32D-2B79-4ABF-9D9E-67F92753FEEB.jpg.webp'),
 ('zipevent-reference-v2-16','https://www.zipeventapp.com/e/VOL-IS-NOW','https://cdnali.zipeventapp.com/images/events/091975AD-4627-4811-89B6-7BA8B3ADBC3D/xl-A8F6922C-CEB4-4BDA-9F4B-CB7CF9650186.jpg.webp'),
 ('zipevent-reference-v2-17','https://www.zipeventapp.com/e/EJAN-Fair-x-Moon-Rim-Lake-2026-94B003A2','https://cdnali.zipeventapp.com/images/events/E8A569E1-FB3A-426A-ACB0-0AB8B93B4A16/xl-135240C7-D455-4AEE-B0F8-FB76C4C6FBF5.jpg.webp'),
 ('zipevent-reference-v2-18','https://www.zipeventapp.com/e/Chill-at-The-Museum-E525ECF2','https://cdnali.zipeventapp.com/images/events/7BEDD736-F938-49E9-86E5-529ED28CA028/xl-7F0622E0-AF13-4839-9F27-4404D2ED2F37.jpg.webp'),
 ('zipevent-reference-v2-19','https://www.zipeventapp.com/e/2026-Phra-Pathom-Chedi-Worship-Festival-AFB13C10','https://cdnali.zipeventapp.com/images/events/4EABBBE3-B264-4AAF-82EB-996B58CFEEA0/xl-3017E6D2-2603-4236-8FE0-9BF8DE8E7434.jpg.webp'),
 ('zipevent-reference-v2-20','https://www.zipeventapp.com/e/Chiangmai-Heritage-Sky-Lanterns-Festival','https://cdnali.zipeventapp.com/images/events/BE176E0D-61F0-455A-9774-001F39867983/xl-ABC65E03-2529-44FA-A2A4-B90C797A65D1.jpg.webp'),
 ('zipevent-reference-v2-21','https://www.zipeventapp.com/e/Chiangmai-Wonderland-Sky-Lanterns-Festival-2026','https://cdnali.zipeventapp.com/images/events/9A37F157-3472-4F30-9859-A11377A33D95/xl-A377A319-542A-4AA3-956F-83032DE052F5.jpg.webp'),
 ('zipevent-reference-v2-22','https://www.zipeventapp.com/e/48-Hour-Film-Project-Bangkok-2026-Film-Screenings','https://cdnali.zipeventapp.com/images/events/6DA11AB0-7C07-47A2-ADC4-52C141742621/xl-17C1ACA2-FFCE-4401-BDD0-B460E00557EF.jpg.webp'),
 ('zipevent-reference-v2-23','https://www.zipeventapp.com/e/Volume-TheBiirthdayParty-inthecolorofdepression-Album-Launch-Party','https://cdnali.zipeventapp.com/images/events/C43AA9E0-F027-42AF-AFDB-B76D6D1CFC0D/xl-05E97461-2388-42D5-B011-4D6EACFB81DF.jpg.webp'),
 ('zipevent-reference-v2-24','https://www.zipeventapp.com/e/PLAYLISTLIVE','https://cdnali.zipeventapp.com/images/events/5412223C-0528-4397-A00B-D07695B14AAA/xl-4409C01B-41BC-4CFB-B9A7-FB4741D04379.jpg.webp'),
 ('zipevent-reference-v2-25','https://www.zipeventapp.com/e/Volume-Base-024','https://cdnali.zipeventapp.com/images/events/9B3E388D-0647-4D4B-95C9-8A3BE7D93B08/xl-9644E1BA-A6E1-4945-A6FF-163B3F390EE1.jpg.webp'),
 ('zipevent-reference-v2-26','https://www.zipeventapp.com/e/BLKGEM-CONCERT','https://cdnali.zipeventapp.com/images/events/2371CA83-7093-4EA6-9CDB-E006775F7439/xl-76A20EDC-0CEA-4171-B1DF-1BCE911F345C.jpg.webp'),
 ('zipevent-reference-v2-27','https://www.zipeventapp.com/e/Volume-Hariguem-Zaboy-Album-Launch','https://cdnali.zipeventapp.com/images/events/7D6694DF-2951-4B5E-987F-F515E86B1731/xl-09FC4B9F-1590-407A-8C65-8AB1E8F1AD98.jpg.webp'),
 ('zipevent-reference-v2-28','https://www.zipeventapp.com/e/the-great-fundraising-masterclass-in-bangkok-2027-international-applicants','https://cdnali.zipeventapp.com/images/events/A14B2D15-9639-45B9-8768-A0201D8990D8/xl-19E106BD-685A-488E-82FF-B386B0594A17.jpg.webp'),
 ('zipevent-reference-v2-29','https://www.zipeventapp.com/e/The-Bells-Rang-Again-The-New-Musical','https://cdnali.zipeventapp.com/images/events/83D20827-2721-435E-8066-4CF08C806296/xl-701B4892-99B3-4757-8BAF-CD720996B23B.jpg.webp'),
 ('zipevent-reference-v2-30','https://www.zipeventapp.com/e/comic-avenue-12','https://cdnali.zipeventapp.com/images/events/8D4F8BAA-16A9-4945-A494-47BDD530B72F/xl-67790A94-C78F-4C5E-B848-F85787F29095.jpg.webp'),
 ('zipevent-reference-v2-31','https://legacy.zipeventapp.com/e/Follow-The-Future-2026','https://cdnali.zipeventapp.com/images/events/F811FC43-B947-489A-AA3E-89CBE08BA20A/766315BA-656A-4F63-B551-C1460E239481.jpg'),
 ('zipevent-reference-v2-32','https://legacy.zipeventapp.com/e/The-3-Asia-Pacific-Exotic-Animal-Veterinary-Conference','https://cdnali.zipeventapp.com/images/events/8467BDA0-B1EC-426F-9BF8-17F9A578AD65/8D8D3798-B8D0-4A48-825F-E2A355CAB860.png'),
 ('zipevent-reference-v2-33','https://www.zipeventapp.com/e/A-Safe-Journey-With-Her-2026','https://cdnali.zipeventapp.com/images/events/CBA25CDD-B481-482E-9A73-13E3BCF103F5/xl-917BD9B0-B959-4EAE-B497-FE4AC2322107.jpg.webp'),
 ('zipevent-reference-v2-34','https://www.zipeventapp.com/e/Love-in-Silence-2A328EFE','https://cdnali.zipeventapp.com/images/events/EFE461F4-000F-43B6-9B08-6BC342066BF9/xl-95C7F6B4-2F9B-474D-BE2E-A8238EDE2A73.jpg.webp'),
 ('zipevent-reference-v2-35','https://www.zipeventapp.com/e/Cinema-in-the-Secret-Garden-by-SITNEY','https://cdnali.zipeventapp.com/images/events/B5EAFAD2-1335-430A-9070-CABDBC2D98E0/xl-3E95B213-34D3-4757-BEB9-43C4B7DEEF4C.jpg.webp');

DO $$ BEGIN
  IF (SELECT count(*) FROM zipevent_posters p JOIN events e
      ON e.slug=p.slug AND e.organizer_id='10000000-0000-0000-0000-000000000001'
      AND position(p.source_url in e.description)>0) <> 49 THEN
    RAISE EXCEPTION 'Reference events missing or source URLs do not match';
  END IF;
END $$;

UPDATE events e SET image_url=p.image_url
FROM zipevent_posters p
WHERE e.slug=p.slug
  AND e.organizer_id='10000000-0000-0000-0000-000000000001'
  AND e.image_url IS DISTINCT FROM p.image_url;
COMMIT;
