-- Shared authentication rate-limit buckets.
-- Run as atendee_migrator on an existing database after migration 020.
-- Partition identifiers are HMAC-SHA256 digests; client IP addresses are never persisted.
\set ON_ERROR_STOP on
BEGIN;

SET LOCAL ROLE atendee_identity_owner;

CREATE TABLE identity."AuthenticationRateLimitBuckets"
(
    "Policy" VARCHAR(64) NOT NULL,
    "PartitionHash" BYTEA NOT NULL,
    "WindowStartedAtUtc" TIMESTAMPTZ NOT NULL,
    "RequestCount" BIGINT NOT NULL,
    "ExpiresAtUtc" TIMESTAMPTZ NOT NULL,
    CONSTRAINT "PK_AuthenticationRateLimitBuckets"
        PRIMARY KEY ("Policy", "PartitionHash", "WindowStartedAtUtc"),
    CONSTRAINT "CK_AuthenticationRateLimitBuckets_PartitionHash"
        CHECK (octet_length("PartitionHash") = 32),
    CONSTRAINT "CK_AuthenticationRateLimitBuckets_RequestCount"
        CHECK ("RequestCount" > 0),
    CONSTRAINT "CK_AuthenticationRateLimitBuckets_Expiry"
        CHECK ("ExpiresAtUtc" > "WindowStartedAtUtc")
);

CREATE INDEX "IX_AuthenticationRateLimitBuckets_ExpiresAtUtc"
    ON identity."AuthenticationRateLimitBuckets" ("ExpiresAtUtc");

REVOKE ALL ON TABLE identity."AuthenticationRateLimitBuckets" FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE
    ON identity."AuthenticationRateLimitBuckets" TO atendee_auth_login;

RESET ROLE;
COMMIT;