# Phase 13 — Hoàn thiện và bàn giao

Cập nhật rà soát 25/10/2026; báo cáo/slide dùng mốc nội dung 24/10/2026. Phạm vi: chuẩn bị và kiểm tra bộ báo cáo, slide, demo dự phòng, API và gói mã nguồn/bằng chứng để review. Theo yêu cầu người dùng, **chưa tạo hoặc kết nối VPS**; SSH host/user/domain sẽ được điền sau.

Trạng thái: **review candidate**, chưa phải release candidate được nghiệm thu. Hai repository có thay đổi chưa commit; HEAD chỉ là mốc gốc, không định danh đầy đủ bản đang bàn giao. Manifest SHA-256 trong gói ZIP định danh chính xác từng file.

## Bộ bàn giao

- [Hướng dẫn chạy lại và chỉ mục](release/README.md).
- [Bản thảo báo cáo sáu chương](release/thesis-report.md), [số liệu đầy đủ](release/measured-results.md) và [nguồn máy đọc được](release/evidence-summary.json).
- [Sơ đồ hệ thống, ERD và sequence hiện tại](release/diagrams.md).
- [Hướng dẫn API](release/api-guide.md), [OpenAPI trích từ assembly hiện tại](release/openapi.json).
- [Kịch bản demo 10 phút và phương án lỗi](release/demo-script.md); [trình bày bằng chứng offline](release/offline-demo.html).
- [Video bằng chứng dự phòng](release/TixFlow-evidence-fallback.webm), dài khoảng 42 giây, không âm thanh; không phải video thao tác ứng dụng.
- [Slide PowerPoint](release/TixFlow-defense.pptx), [nội dung và lời dẫn](release/defense-outline.md); deck cũng chỉnh sửa được trong chat.
- [Checklist chốt release](release/release-checklist.md).

`scripts/Build-Phase13Evidence.py --package` tạo ZIP trong `artifacts/phase13/packages/`: snapshot hai repo, migration/seed, harness, raw của chiến dịch ngày 22/10, kết quả regression và manifest kiểm tra từng file. Bộ lọc loại `.env` thật, runtime tokens, key files, database backup và thư mục build/tạm. Source vẫn có cấu hình development/test mẫu; không dùng chúng làm production secrets. Gói review không tự deploy, commit hay tag.

## Rà soát ngày 25/10

- Gói 24/10 khớp 1.119 file; 592 input evidence khớp SHA-256. Không có thay đổi source ứng dụng so với snapshot đó; không chạy lại 106 test chỉ vì chỉnh tài liệu/tool đóng gói.
- Số liệu của hai native chart trong PowerPoint khớp JSON đo; 11 slide có speaker notes, hai workbook dữ liệu biểu đồ đi kèm. OpenAPI có 49 đường dẫn khớp provenance.
- Đã bổ sung verifier độc lập chạy bằng Python standard library, dùng được trực tiếp trên ZIP mà không giải nén/thực thi source bên trong. Tám negative-control tests kiểm tra tamper, thiếu/thừa file, trùng manifest, trùng tên kiểu Windows, đường dẫn không hợp lệ, secret path, thiếu variant và regression rỗng/skip.
- Browser Edge kiểm tra bảy trang offline, phím/nút điều hướng, ảnh embedded, không HTTP request, không lỗi JavaScript và không tràn ngang ở 390 px. Video dự phòng tạo từ đúng các trang này, có nhãn phạm vi và kiểm tra giải mã/phát lại.
- File tạm của lần xuất PowerPoint trước đã được dọn. Báo cáo kiểm tra lần này: [review-20261025.json](release/review-20261025.json).

## Đã kiểm chứng

Regression mới: **106/106 pass**, không fail/skip; xem `artifacts/phase13/verification/phase13-regression.trx`. Dữ liệu phase 9 được tính lại từ JSON gốc; smoke/ramp/spike phase 10 được tổng hợp lại từ raw k6, mỗi profile có sáu run đạt. Soak mới có một run lỗi, không được đưa vào so sánh đạt. Trạng thái chiến dịch vẫn là `failed` với `Load database SQL failed.`; nguyên nhân vì sao PostgreSQL dừng chưa được xác định ở lần bàn giao này.

Phase 12 có bảy nhóm kiểm tra migration/restore/restart trên fixture PostgreSQL cách ly. Thời gian restore 1,281 giây chỉ áp dụng fixture 77.935 byte; không phải RTO của VPS.

## Còn mở

Chưa đóng các gate soak, đánh giá AI/provider thật, browser/VPS, tái tạo trên máy sạch bởi người khác, video thao tác thực tế và chốt commit/image digest. Các tài liệu bàn giao phân biệt bằng chứng đã đo với checklist chưa thực hiện. Không dùng số liệu cũ để tuyên bố hệ thống hiện tại đạt 10.000 người dùng đồng thời hoặc SLA production.
