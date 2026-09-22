BEGIN;

CREATE TABLE users
(
    id               uuid PRIMARY KEY,
    email            varchar(320) NOT NULL,
    normalized_email varchar(320) NOT NULL,
    display_name     varchar(120) NOT NULL,
    password_hash    varchar(500) NOT NULL,
    role             varchar(24)  NOT NULL,
    status           varchar(24)  NOT NULL DEFAULT 'Active',
    created_at_utc   timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc   timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_users_role CHECK (role IN ('Admin', 'Organizer', 'Customer')),
    CONSTRAINT ck_users_status CHECK (status IN ('Active', 'Suspended', 'Disabled')),
    CONSTRAINT ux_users_normalized_email UNIQUE (normalized_email)
);

CREATE TABLE refresh_tokens
(
    id                     uuid PRIMARY KEY,
    user_id                uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash             varchar(128) NOT NULL,
    expires_at_utc         timestamptz  NOT NULL,
    created_at_utc         timestamptz  NOT NULL DEFAULT now(),
    revoked_at_utc         timestamptz,
    replaced_by_token_hash varchar(128),
    created_by_ip          varchar(64),
    revoked_by_ip          varchar(64),
    CONSTRAINT ux_refresh_tokens_token_hash UNIQUE (token_hash)
);

CREATE INDEX ix_refresh_tokens_user_id_expires_at_utc
    ON refresh_tokens (user_id, expires_at_utc);

CREATE TABLE organizers
(
    id            uuid PRIMARY KEY,
    owner_user_id uuid         NOT NULL REFERENCES users (id) ON DELETE RESTRICT,
    name          varchar(180) NOT NULL,
    slug          varchar(180) NOT NULL,
    description   varchar(2000) NOT NULL DEFAULT '',
    contact_email varchar(320) NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_organizers_owner_user_id UNIQUE (owner_user_id),
    CONSTRAINT ux_organizers_slug UNIQUE (slug)
);

CREATE TABLE venues
(
    id             uuid PRIMARY KEY,
    name           varchar(180) NOT NULL,
    address_line   varchar(300) NOT NULL,
    ward           varchar(120),
    district       varchar(120),
    city           varchar(120) NOT NULL,
    country_code   char(2)      NOT NULL DEFAULT 'VN',
    time_zone      varchar(80)  NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
    created_at_utc timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc timestamptz  NOT NULL DEFAULT now()
);

CREATE TABLE events
(
    id                 uuid PRIMARY KEY,
    organizer_id       uuid          NOT NULL REFERENCES organizers (id) ON DELETE RESTRICT,
    venue_id           uuid REFERENCES venues (id) ON DELETE SET NULL,
    name               varchar(240)  NOT NULL,
    slug               varchar(240)  NOT NULL,
    description        varchar(5000) NOT NULL DEFAULT '',
    category           varchar(100)  NOT NULL,
    image_url          varchar(1000),
    status             varchar(24)   NOT NULL DEFAULT 'Draft',
    sale_starts_at_utc timestamptz,
    sale_ends_at_utc   timestamptz,
    published_at_utc   timestamptz,
    created_at_utc     timestamptz   NOT NULL DEFAULT now(),
    updated_at_utc     timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT ck_events_status CHECK (status IN ('Draft', 'Published', 'Cancelled', 'Completed')),
    CONSTRAINT ck_events_sale_window CHECK (
        sale_starts_at_utc IS NULL
        OR sale_ends_at_utc IS NULL
        OR sale_ends_at_utc > sale_starts_at_utc),
    CONSTRAINT ux_events_organizer_slug UNIQUE (organizer_id, slug)
);

CREATE INDEX ix_events_status_sale_starts_at_utc
    ON events (status, sale_starts_at_utc);
CREATE INDEX ix_events_category_status
    ON events (category, status);

CREATE TABLE event_sessions
(
    id             uuid PRIMARY KEY,
    event_id       uuid         NOT NULL REFERENCES events (id) ON DELETE CASCADE,
    name           varchar(180) NOT NULL,
    starts_at_utc  timestamptz  NOT NULL,
    ends_at_utc    timestamptz  NOT NULL,
    status         varchar(24)  NOT NULL DEFAULT 'Scheduled',
    created_at_utc timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_event_sessions_time CHECK (ends_at_utc > starts_at_utc),
    CONSTRAINT ck_event_sessions_status CHECK (
        status IN ('Scheduled', 'OnSale', 'SoldOut', 'Cancelled', 'Completed')),
    CONSTRAINT ux_event_sessions_event_start UNIQUE (event_id, starts_at_utc)
);

CREATE INDEX ix_event_sessions_status_starts_at_utc
    ON event_sessions (status, starts_at_utc);

CREATE TABLE ticket_types
(
    id                 uuid PRIMARY KEY,
    event_session_id   uuid         NOT NULL REFERENCES event_sessions (id) ON DELETE CASCADE,
    name               varchar(160) NOT NULL,
    code               varchar(40)  NOT NULL,
    description        varchar(1000),
    price              numeric(14, 2) NOT NULL,
    currency           char(3)      NOT NULL DEFAULT 'VND',
    inventory_mode     varchar(32)  NOT NULL,
    capacity           integer      NOT NULL,
    max_per_order      integer      NOT NULL DEFAULT 10,
    sale_starts_at_utc timestamptz,
    sale_ends_at_utc   timestamptz,
    created_at_utc     timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc     timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_ticket_types_price CHECK (price >= 0),
    CONSTRAINT ck_ticket_types_capacity CHECK (capacity > 0),
    CONSTRAINT ck_ticket_types_max_per_order CHECK (max_per_order > 0),
    CONSTRAINT ck_ticket_types_inventory_mode CHECK (
        inventory_mode IN ('ReservedSeating', 'GeneralAdmission')),
    CONSTRAINT ck_ticket_types_sale_window CHECK (
        sale_starts_at_utc IS NULL
        OR sale_ends_at_utc IS NULL
        OR sale_ends_at_utc > sale_starts_at_utc),
    CONSTRAINT ux_ticket_types_session_code UNIQUE (event_session_id, code)
);

CREATE TABLE seats
(
    id               uuid PRIMARY KEY,
    event_session_id uuid        NOT NULL REFERENCES event_sessions (id) ON DELETE RESTRICT,
    ticket_type_id   uuid        NOT NULL REFERENCES ticket_types (id) ON DELETE CASCADE,
    section          varchar(80) NOT NULL,
    row_label        varchar(20) NOT NULL,
    seat_number      varchar(20) NOT NULL,
    is_accessible    boolean     NOT NULL DEFAULT false,
    is_active        boolean     NOT NULL DEFAULT true,
    created_at_utc   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_seats_session_position UNIQUE (
        event_session_id, section, row_label, seat_number)
);

CREATE INDEX ix_seats_ticket_type_id ON seats (ticket_type_id);

CREATE TABLE ticket_inventories
(
    id             uuid PRIMARY KEY,
    ticket_type_id uuid        NOT NULL REFERENCES ticket_types (id) ON DELETE CASCADE,
    total_quantity integer     NOT NULL,
    held_quantity  integer     NOT NULL DEFAULT 0,
    sold_quantity  integer     NOT NULL DEFAULT 0,
    version        bigint      NOT NULL DEFAULT 0,
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_ticket_inventories_ticket_type_id UNIQUE (ticket_type_id),
    CONSTRAINT ck_ticket_inventories_total CHECK (total_quantity > 0),
    CONSTRAINT ck_ticket_inventories_held CHECK (held_quantity >= 0),
    CONSTRAINT ck_ticket_inventories_sold CHECK (sold_quantity >= 0),
    CONSTRAINT ck_ticket_inventories_available CHECK (
        held_quantity + sold_quantity <= total_quantity)
);

CREATE TABLE holds
(
    id                uuid PRIMARY KEY,
    user_id           uuid         NOT NULL REFERENCES users (id) ON DELETE RESTRICT,
    event_session_id  uuid         NOT NULL REFERENCES event_sessions (id) ON DELETE RESTRICT,
    idempotency_key   varchar(120) NOT NULL,
    status            varchar(24)  NOT NULL DEFAULT 'Active',
    expires_at_utc    timestamptz  NOT NULL,
    created_at_utc    timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc    timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_holds_status CHECK (status IN ('Active', 'Confirmed', 'Expired', 'Cancelled')),
    CONSTRAINT ux_holds_user_idempotency UNIQUE (user_id, idempotency_key)
);

CREATE INDEX ix_holds_status_expires_at_utc
    ON holds (status, expires_at_utc);

CREATE TABLE hold_items
(
    id             uuid PRIMARY KEY,
    hold_id        uuid           NOT NULL REFERENCES holds (id) ON DELETE CASCADE,
    ticket_type_id uuid           NOT NULL REFERENCES ticket_types (id) ON DELETE RESTRICT,
    quantity       integer        NOT NULL,
    unit_price     numeric(14, 2) NOT NULL,
    CONSTRAINT ck_hold_items_quantity CHECK (quantity > 0),
    CONSTRAINT ck_hold_items_price CHECK (unit_price >= 0)
);

CREATE INDEX ix_hold_items_hold_id ON hold_items (hold_id);

CREATE TABLE orders
(
    id               uuid PRIMARY KEY,
    user_id          uuid           NOT NULL REFERENCES users (id) ON DELETE RESTRICT,
    event_session_id uuid           NOT NULL REFERENCES event_sessions (id) ON DELETE RESTRICT,
    hold_id          uuid           NOT NULL REFERENCES holds (id) ON DELETE RESTRICT,
    order_number     varchar(40)    NOT NULL,
    status           varchar(32)    NOT NULL DEFAULT 'PendingPayment',
    total_amount     numeric(14, 2) NOT NULL,
    currency         char(3)        NOT NULL DEFAULT 'VND',
    created_at_utc   timestamptz    NOT NULL DEFAULT now(),
    updated_at_utc   timestamptz    NOT NULL DEFAULT now(),
    CONSTRAINT ck_orders_status CHECK (
        status IN ('PendingPayment', 'Paid', 'Cancelled', 'Expired', 'Refunded')),
    CONSTRAINT ck_orders_total CHECK (total_amount >= 0),
    CONSTRAINT ux_orders_order_number UNIQUE (order_number),
    CONSTRAINT ux_orders_hold_id UNIQUE (hold_id)
);

CREATE INDEX ix_orders_user_id_created_at_utc
    ON orders (user_id, created_at_utc DESC);

CREATE TABLE order_items
(
    id             uuid PRIMARY KEY,
    order_id       uuid           NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    ticket_type_id uuid           NOT NULL REFERENCES ticket_types (id) ON DELETE RESTRICT,
    quantity       integer        NOT NULL,
    unit_price     numeric(14, 2) NOT NULL,
    CONSTRAINT ck_order_items_quantity CHECK (quantity > 0),
    CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)
);

CREATE INDEX ix_order_items_order_id ON order_items (order_id);

CREATE TABLE seat_allocations
(
    id             uuid PRIMARY KEY,
    seat_id        uuid        NOT NULL REFERENCES seats (id) ON DELETE RESTRICT,
    hold_item_id   uuid REFERENCES hold_items (id) ON DELETE SET NULL,
    order_item_id  uuid REFERENCES order_items (id) ON DELETE SET NULL,
    status         varchar(24) NOT NULL,
    expires_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_seat_allocations_status CHECK (status IN ('Held', 'Sold', 'Released')),
    CONSTRAINT ck_seat_allocations_reference CHECK (
        hold_item_id IS NOT NULL OR order_item_id IS NOT NULL),
    CONSTRAINT ck_seat_allocations_state CHECK (
        (status = 'Held' AND hold_item_id IS NOT NULL AND expires_at_utc IS NOT NULL)
        OR (status = 'Sold' AND order_item_id IS NOT NULL AND expires_at_utc IS NULL)
        OR status = 'Released')
);

CREATE UNIQUE INDEX ux_seat_allocations_active_seat
    ON seat_allocations (seat_id)
    WHERE status IN ('Held', 'Sold');
CREATE INDEX ix_seat_allocations_status_expires_at_utc
    ON seat_allocations (status, expires_at_utc);

CREATE TABLE payments
(
    id                      uuid PRIMARY KEY,
    order_id                uuid           NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    idempotency_key         varchar(120)   NOT NULL,
    method                  varchar(24)    NOT NULL,
    status                  varchar(24)    NOT NULL DEFAULT 'Pending',
    amount                  numeric(14, 2) NOT NULL,
    currency                char(3)        NOT NULL DEFAULT 'VND',
    provider_transaction_id varchar(200),
    paid_at_utc             timestamptz,
    created_at_utc          timestamptz    NOT NULL DEFAULT now(),
    updated_at_utc          timestamptz    NOT NULL DEFAULT now(),
    CONSTRAINT ck_payments_method CHECK (method IN ('Mock', 'Card', 'BankTransfer', 'EWallet')),
    CONSTRAINT ck_payments_status CHECK (status IN ('Pending', 'Succeeded', 'Failed', 'Refunded')),
    CONSTRAINT ck_payments_amount CHECK (amount >= 0),
    CONSTRAINT ux_payments_idempotency_key UNIQUE (idempotency_key)
);

CREATE UNIQUE INDEX ux_payments_provider_transaction_id
    ON payments (provider_transaction_id)
    WHERE provider_transaction_id IS NOT NULL;
CREATE INDEX ix_payments_order_id ON payments (order_id);

CREATE TABLE tickets
(
    id               uuid PRIMARY KEY,
    order_item_id    uuid         NOT NULL REFERENCES order_items (id) ON DELETE RESTRICT,
    user_id          uuid         NOT NULL REFERENCES users (id) ON DELETE RESTRICT,
    event_session_id uuid         NOT NULL REFERENCES event_sessions (id) ON DELETE RESTRICT,
    seat_id          uuid REFERENCES seats (id) ON DELETE RESTRICT,
    ticket_code      varchar(50)  NOT NULL,
    qr_token_hash    varchar(128) NOT NULL,
    status           varchar(24)  NOT NULL DEFAULT 'Issued',
    issued_at_utc    timestamptz  NOT NULL DEFAULT now(),
    checked_in_at_utc timestamptz,
    CONSTRAINT ck_tickets_status CHECK (status IN ('Issued', 'Used', 'Cancelled', 'Refunded')),
    CONSTRAINT ux_tickets_ticket_code UNIQUE (ticket_code),
    CONSTRAINT ux_tickets_qr_token_hash UNIQUE (qr_token_hash)
);

CREATE UNIQUE INDEX ux_tickets_active_seat
    ON tickets (seat_id)
    WHERE seat_id IS NOT NULL AND status IN ('Issued', 'Used');
CREATE INDEX ix_tickets_session_status
    ON tickets (event_session_id, status);
CREATE INDEX ix_tickets_user_id ON tickets (user_id);

CREATE TABLE payment_webhook_events
(
    id                uuid PRIMARY KEY,
    provider          varchar(60)  NOT NULL,
    provider_event_id varchar(200) NOT NULL,
    payload           jsonb        NOT NULL,
    received_at_utc   timestamptz  NOT NULL DEFAULT now(),
    processed_at_utc  timestamptz,
    processing_error  varchar(4000),
    CONSTRAINT ux_payment_webhook_provider_event UNIQUE (provider, provider_event_id)
);

CREATE INDEX ix_payment_webhook_events_processed_at_utc
    ON payment_webhook_events (processed_at_utc);

CREATE TABLE idempotency_records
(
    id                   uuid PRIMARY KEY,
    user_id              uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    scope                varchar(80)  NOT NULL,
    key                  varchar(120) NOT NULL,
    request_hash         varchar(128) NOT NULL,
    response_status_code integer,
    response_body        jsonb,
    is_completed         boolean      NOT NULL DEFAULT false,
    expires_at_utc       timestamptz  NOT NULL,
    created_at_utc       timestamptz  NOT NULL DEFAULT now(),
    updated_at_utc       timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ux_idempotency_user_scope_key UNIQUE (user_id, scope, key)
);

CREATE INDEX ix_idempotency_records_expires_at_utc
    ON idempotency_records (expires_at_utc);

CREATE TABLE outbox_messages
(
    id                  uuid PRIMARY KEY,
    type                varchar(200) NOT NULL,
    payload             jsonb        NOT NULL,
    occurred_at_utc     timestamptz  NOT NULL,
    processed_at_utc    timestamptz,
    next_attempt_at_utc timestamptz,
    attempt_count       integer      NOT NULL DEFAULT 0,
    last_error          text,
    CONSTRAINT ck_outbox_messages_attempt_count CHECK (attempt_count >= 0)
);

CREATE INDEX ix_outbox_pending
    ON outbox_messages (next_attempt_at_utc, occurred_at_utc)
    WHERE processed_at_utc IS NULL;

CREATE TABLE audit_logs
(
    id             uuid PRIMARY KEY,
    actor_user_id  uuid,
    action         varchar(120) NOT NULL,
    entity_type    varchar(120) NOT NULL,
    entity_id      varchar(120) NOT NULL,
    data           jsonb,
    ip_address     varchar(64),
    created_at_utc timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX ix_audit_logs_entity
    ON audit_logs (entity_type, entity_id, created_at_utc DESC);
CREATE INDEX ix_audit_logs_actor
    ON audit_logs (actor_user_id, created_at_utc DESC);

INSERT INTO users
    (id, email, normalized_email, display_name, password_hash, role, status)
VALUES
    ('00000000-0000-0000-0000-000000000001',
     'organizer@tixflow.local',
     'ORGANIZER@TIXFLOW.LOCAL',
     'TixFlow Demo Organizer',
     'SEED_ONLY_REPLACE_WITH_REAL_HASH',
     'Organizer',
     'Active'),
    ('00000000-0000-0000-0000-000000000002',
     'customer@tixflow.local',
     'CUSTOMER@TIXFLOW.LOCAL',
     'TixFlow Demo Customer',
     'SEED_ONLY_REPLACE_WITH_REAL_HASH',
     'Customer',
     'Active');

INSERT INTO organizers
    (id, owner_user_id, name, slug, description, contact_email)
VALUES
    ('10000000-0000-0000-0000-000000000001',
     '00000000-0000-0000-0000-000000000001',
     'TixFlow Live',
     'tixflow-live',
     'Nhà tổ chức mẫu dùng cho môi trường phát triển.',
     'organizer@tixflow.local');

INSERT INTO venues
    (id, name, address_line, district, city)
VALUES
    ('20000000-0000-0000-0000-000000000001',
     'TixFlow Arena',
     '01 Đường Demo',
     'Quận 1',
     'TP. Hồ Chí Minh');

INSERT INTO events
    (id, organizer_id, venue_id, name, slug, description, category, status,
     sale_starts_at_utc, sale_ends_at_utc, published_at_utc)
VALUES
    ('30000000-0000-0000-0000-000000000001',
     '10000000-0000-0000-0000-000000000001',
     '20000000-0000-0000-0000-000000000001',
     'TixFlow Flash Sale Demo',
     'tixflow-flash-sale-demo',
     'Sự kiện mẫu để phát triển luồng tìm kiếm, giữ chỗ và kiểm thử đồng thời.',
     'Music',
     'Published',
     '2026-09-01 01:00:00+00',
     '2026-12-20 11:00:00+00',
     now());

INSERT INTO event_sessions
    (id, event_id, name, starts_at_utc, ends_at_utc, status)
VALUES
    ('40000000-0000-0000-0000-000000000001',
     '30000000-0000-0000-0000-000000000001',
     'Đêm diễn chính',
     '2026-12-20 12:00:00+00',
     '2026-12-20 15:00:00+00',
     'OnSale');

INSERT INTO ticket_types
    (id, event_session_id, name, code, description, price, currency,
     inventory_mode, capacity, max_per_order)
VALUES
    ('50000000-0000-0000-0000-000000000001',
     '40000000-0000-0000-0000-000000000001',
     'VIP',
     'VIP',
     'Khu VIP có ghế cố định.',
     1500000,
     'VND',
     'ReservedSeating',
     10,
     4),
    ('50000000-0000-0000-0000-000000000002',
     '40000000-0000-0000-0000-000000000001',
     'Standard',
     'STANDARD',
     'Vé đứng theo số lượng.',
     500000,
     'VND',
     'GeneralAdmission',
     500,
     10);

INSERT INTO seats
    (id, event_session_id, ticket_type_id, section, row_label, seat_number)
VALUES
    ('60000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '01'),
    ('60000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '02'),
    ('60000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '03'),
    ('60000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '04'),
    ('60000000-0000-0000-0000-000000000005', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '05'),
    ('60000000-0000-0000-0000-000000000006', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '06'),
    ('60000000-0000-0000-0000-000000000007', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '07'),
    ('60000000-0000-0000-0000-000000000008', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '08'),
    ('60000000-0000-0000-0000-000000000009', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '09'),
    ('60000000-0000-0000-0000-000000000010', '40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', 'VIP', 'A', '10');

INSERT INTO ticket_inventories
    (id, ticket_type_id, total_quantity, held_quantity, sold_quantity, version)
VALUES
    ('70000000-0000-0000-0000-000000000001',
     '50000000-0000-0000-0000-000000000001', 10, 0, 0, 0),
    ('70000000-0000-0000-0000-000000000002',
     '50000000-0000-0000-0000-000000000002', 500, 0, 0, 0);

COMMIT;
