# ADR-010: Single-Tenant Architecture

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §1 — "The National Security Guard (NSG) requires a mobile application"

## Context
The application is for NSG exclusively. There is no SOW requirement for multi-tenancy.

## Decision
- Single-tenant application — no TenantId on any table
- No multi-tenant middleware, no tenant resolution, no tenant-scoped queries
- Organizational data (Office, Group) is business data, not security tenancy
- User isolation via role-based authorization and authenticated JWT identity
- If Office/Group-level data scoping is needed (e.g., notifications by Office), it is implemented as business logic, not as a tenant boundary

## Rationale
- The SOW describes one application for one organization (NSG)
- Adding multi-tenancy "just in case" violates YAGNI and adds unnecessary complexity
- Security is enforced through user identity, roles, and policies — not tenant isolation

## Consequences
- Simpler architecture, fewer moving parts
- No accidental data leakage between tenants (because there are no tenants)
- If multi-tenancy is ever required, it would be a significant architectural change
