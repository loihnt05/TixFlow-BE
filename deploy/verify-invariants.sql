\set ON_ERROR_STOP on
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM ticket_inventories WHERE held_quantity < 0 OR sold_quantity < 0 OR held_quantity + sold_quantity > total_quantity) THEN
    RAISE EXCEPTION 'Inventory invariant failed';
  END IF;
  IF EXISTS (SELECT seat_id FROM seat_allocations WHERE status IN ('Held','Sold') GROUP BY seat_id HAVING count(*) > 1) THEN
    RAISE EXCEPTION 'Duplicate active seat allocation';
  END IF;
  IF to_regclass('public.orders') IS NULL OR to_regclass('public.tickets') IS NULL OR to_regclass('public.deployment_migrations') IS NULL THEN
    RAISE EXCEPTION 'Required production tables missing';
  END IF;
END $$;
