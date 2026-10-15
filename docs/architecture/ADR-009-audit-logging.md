# ADR-009: Audit Logging

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.8 — "Comprehensive audit logging of user actions"

## Context
The SOW requires comprehensive audit logging for security compliance and VAPT.

## Decision
- Dedicated `audit_logs` table with `bigint` PK for high volume
- Append-only design — no update or delete operations exposed
- Audited events include: login, logout, OTP, registration, approval, device, token, password change, document access, admin actions
- Audit metadata: UserId, Action, EntityType, EntityId, Result, IpAddress, DeviceId, CorrelationId, AdditionalData (JSON)
- **Never** store in audit logs: passwords, OTP values, token hashes, sensitive PII, salary data
- Audit service swallows exceptions to avoid breaking primary operations
- Indexed on: CreatedAt, UserId+CreatedAt, Action

## Consequences
- Full security event trail for VAPT and incident investigation
- Correlation ID links audit events to specific requests
- Protected against unauthorized modification (no update/delete endpoints)
