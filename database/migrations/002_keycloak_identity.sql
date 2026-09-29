-- Versioned, non-destructive and safe to rerun. Never replay 001 on an existing DB.
BEGIN;
SELECT pg_advisory_xact_lock(20260928, 2);

ALTER TABLE users ADD COLUMN IF NOT EXISTS identity_subject varchar(255);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_identity_subject ON users (identity_subject);
-- Legacy values remain intact; externally authenticated profiles store NULL.
ALTER TABLE users ALTER COLUMN password_hash DROP NOT NULL;

COMMENT ON COLUMN users.identity_subject IS
    'Keycloak sub in the configured tixflow realm. Never link existing accounts by email automatically.';
COMMENT ON COLUMN users.password_hash IS
    'Legacy only. Keycloak owns credentials; new OIDC profiles have no local password.';
COMMENT ON TABLE refresh_tokens IS
    'DEPRECATED / UNUSED: retained for compatibility. Keycloak owns sessions and refresh tokens.';

COMMIT;
