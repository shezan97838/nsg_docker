# ADR-006: Single Device Binding

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.7 Step 3 — "Device binding is completed (IMEI + device token)", §3.8 — "Single device binding per user"

## Context
The SOW requires single device binding per user. It references "IMEI + device token", but raw IMEI access is restricted on modern Android (10+) and iOS.

## Decision
- Use **platform-safe device identifier** (e.g., Android ID, iOS identifierForVendor) instead of raw IMEI
- Device identifier is **hashed** (SHA-256) before storage — server never stores raw identifier
- One active device per user at any time
- New device registration **revokes** the previous active device and all its refresh tokens
- Optional cryptographic key pair binding:
  - Mobile app generates a key pair in device secure storage
  - Public key sent to server during device registration
  - Private key remains on device (never leaves secure enclave)
  - Server can challenge device to prove key possession
- All device registration/revocation events are audited

## Assumption
- Platform-safe identifiers are acceptable (instead of raw IMEI) — See OQ-005
- Cryptographic key binding is an optional hardening mechanism, not required for MVP
- New device registration requires full re-authentication (password + OTP)

## Consequences
- Works on all modern Android/iOS without special permissions
- Single device enforcement prevents unauthorized parallel access
- Device revocation cascades to token revocation for security
