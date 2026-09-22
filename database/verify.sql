\set ON_ERROR_STOP on

SELECT current_database() AS database_name,
       current_user AS database_user,
       version() AS postgres_version;

SELECT COUNT(*) AS application_table_count
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE'
  AND table_name <> '__EFMigrationsHistory';

SELECT e.name AS event_name,
       e.status AS event_status,
       s.name AS session_name,
       s.starts_at_utc,
       tt.name AS ticket_type,
       tt.inventory_mode,
       ti.total_quantity,
       ti.held_quantity,
       ti.sold_quantity,
       ti.total_quantity - ti.held_quantity - ti.sold_quantity AS available_quantity
FROM events e
JOIN event_sessions s ON s.event_id = e.id
JOIN ticket_types tt ON tt.event_session_id = s.id
JOIN ticket_inventories ti ON ti.ticket_type_id = tt.id
ORDER BY e.name, s.starts_at_utc, tt.code;

SELECT tt.name AS ticket_type,
       COUNT(seat.id) AS seat_count
FROM ticket_types tt
LEFT JOIN seats seat ON seat.ticket_type_id = tt.id
GROUP BY tt.id, tt.name
ORDER BY tt.name;
