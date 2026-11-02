-- Read-only, single-snapshot checks. The runner requires every violation count to be zero.
WITH inventory AS (
 SELECT i.*, t.inventory_mode FROM ticket_inventories i JOIN ticket_types t ON t.id=i.ticket_type_id
 WHERE t.event_session_id='80000000-0000-0000-0000-000000000002'
), held AS (
 SELECT hi.ticket_type_id, sum(hi.quantity) AS quantity FROM hold_items hi JOIN holds h ON h.id=hi.hold_id
 WHERE h.status='Active' GROUP BY hi.ticket_type_id
), sold AS (
 SELECT oi.ticket_type_id, sum(oi.quantity) AS quantity FROM order_items oi JOIN orders o ON o.id=oi.order_id
 WHERE o.status='Paid' GROUP BY oi.ticket_type_id
)
SELECT json_build_object(
 'oversold', (SELECT count(*) FROM inventory WHERE held_quantity+sold_quantity>total_quantity OR held_quantity<0 OR sold_quantity<0),
 'inventory_mismatch', (SELECT count(*) FROM inventory i LEFT JOIN held h USING(ticket_type_id) LEFT JOIN sold s USING(ticket_type_id)
   WHERE i.held_quantity<>coalesce(h.quantity,0) OR i.sold_quantity<>coalesce(s.quantity,0)),
 'duplicate_active_seats', (SELECT count(*) FROM (SELECT seat_id FROM seat_allocations WHERE status IN ('Held','Sold') GROUP BY seat_id HAVING count(*)>1) x),
 'seat_inventory_mismatch', (SELECT count(*) FROM inventory i WHERE inventory_mode='ReservedSeating' AND
   i.held_quantity+i.sold_quantity<>(SELECT count(*) FROM seat_allocations a JOIN seats s ON s.id=a.seat_id WHERE s.ticket_type_id=i.ticket_type_id AND a.status IN ('Held','Sold'))),
 'duplicate_holds', (SELECT count(*) FROM (SELECT user_id,idempotency_key FROM holds GROUP BY user_id,idempotency_key HAVING count(*)>1) x),
 'duplicate_orders', (SELECT count(*) FROM (SELECT hold_id FROM orders GROUP BY hold_id HAVING count(*)>1) x),
 'duplicate_payments', (SELECT count(*) FROM (SELECT order_id FROM payments WHERE status='Succeeded' GROUP BY order_id HAVING count(*)>1) x),
 'duplicate_provider_references', (SELECT count(*) FROM (SELECT provider_transaction_id FROM payments WHERE provider_transaction_id IS NOT NULL GROUP BY provider_transaction_id HAVING count(*)>1) x),
 'duplicate_tickets', (SELECT count(*) FROM (SELECT order_item_id,ordinal FROM tickets GROUP BY order_item_id,ordinal HAVING count(*)>1) x),
 'missing_paid_tickets', (SELECT count(*) FROM order_items oi JOIN orders o ON o.id=oi.order_id
   WHERE o.status='Paid' AND oi.quantity<>(SELECT count(*) FROM tickets t WHERE t.order_item_id=oi.id)),
 'overdue_holds', (SELECT count(*) FROM holds WHERE status='Active' AND expires_at_utc<clock_timestamp()-interval '60 seconds'),
 'inventory', (SELECT json_agg(row_to_json(i)) FROM inventory i),
 'paid_orders', (SELECT count(*) FROM orders WHERE status='Paid'),
 'tickets', (SELECT count(*) FROM tickets)
);
