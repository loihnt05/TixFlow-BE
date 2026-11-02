#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
python3 deploy/scripts/smoke.py --env "$ENV_FILE"
dc exec -T postgres psql -X -U tixflow -d tixflow -v ON_ERROR_STOP=1 < deploy/verify-invariants.sql
dc exec -T postgres psql -X -U tixflow -d tixflow -v ON_ERROR_STOP=1 <<'SQL'
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM processing_metrics WHERE sampled_at_utc > now()-interval '60 seconds' AND values_json->>'broker' = 'available') THEN
   RAISE EXCEPTION 'No fresh healthy Worker sample';
 END IF;
END $$;
SQL
dc exec -T prometheus wget -q -O - 'http://localhost:9090/api/v1/targets' | python3 -c '
import json,sys
targets=json.load(sys.stdin)["data"]["activeTargets"]
required={"api","keycloak","rabbitmq","host","prometheus"}
healthy={t["labels"]["job"] for t in targets if t["health"]=="up"}
if not required <= healthy: raise SystemExit("Monitoring targets are not all healthy")
print("Prometheus targets healthy")'
