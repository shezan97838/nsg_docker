# ADR-003: JWT Access Tokens

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.8 — "JWT/OAuth 2.0 based authentication", §4 — "Auth: JWT + OAuth 2.0"

## Context
The SOW requires JWT/OAuth 2.0. No external OAuth authorization server is specified.

## Decision
- Short-lived JWT access tokens (15 minutes, configurable)
- HMAC-SHA256 signing algorithm (explicitly validated; reject unexpected algorithms)
- Minimal claims: `sub`, `jti`, `iat`, `exp`, `role`, `device_id`, `force_number`
- No sensitive PII in tokens (no salary, password, OTP, mobile number)
- Validate: issuer, audience, signature, expiration, not-before, algorithm
- Signing key from secure configuration (never source code)

## Assumption
JWT with refresh tokens satisfies the SOW's "JWT/OAuth 2.0" requirement. A full OAuth 2.0 authorization server is NOT built unless explicitly required. See OQ-002.

## Consequences
- Stateless authentication suitable for horizontal scaling
- Short token lifetime limits exposure window if token is compromised
- Requires refresh token mechanism for seamless user experience
