# ADR-004: Refresh Token Rotation

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.8 — "Configurable session timeout with secure logout"

## Context
Short-lived JWT access tokens require a refresh mechanism. The SOW requires configurable session timeout and secure logout.

## Decision
- Opaque refresh tokens (64-byte cryptographically random)
- Stored as SHA-256 hash in database (never plaintext)
- **Token rotation**: each refresh issues a new token and revokes the old one
- **Family tracking**: all tokens in a rotation chain share a `FamilyId`
- **Reuse detection**: if a revoked token is presented, the entire family is revoked
- Device binding enforced on every refresh
- Logout revokes the current refresh token

## Reuse Detection Flow
1. Client uses refresh token A → Server issues token B, revokes A
2. Attacker replays token A → Server detects A is revoked → **Revokes entire family** → All sessions invalidated
3. Audit event created for security investigation

## Consequences
- Strong protection against refresh token theft
- Compromised token is detected on first reuse
- Requires database lookup on every refresh (acceptable for auth operations)
