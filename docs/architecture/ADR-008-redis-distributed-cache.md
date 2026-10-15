# ADR-008: Redis Distributed Cache

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §4 — "Caching: Redis or equivalent distributed cache", §5 — "Redis: Hosted within the same secure infrastructure"

## Context
The SOW requires distributed caching for horizontal scalability.

## Decision
- Use `ICacheService` abstraction in Application layer
- Infrastructure implements via `IDistributedCache` (Microsoft.Extensions.Caching.StackExchangeRedis)
- Falls back to in-memory distributed cache when Redis is not configured (development)
- Use cases: OTP rate limiting, frequently accessed reference data, ephemeral security state
- **Never** store authoritative business data only in Redis — it is volatile
- All cache entries have explicit TTL

## Consequences
- Application code is cache-provider agnostic
- Horizontal scaling works (no in-process session state)
- Redis is optional for development but required for production multi-instance deployment
