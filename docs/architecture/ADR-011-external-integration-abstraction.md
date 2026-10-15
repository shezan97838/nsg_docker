# ADR-011: External Integration Abstraction

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §4 — SMS Gateway, FCM, HRMS, Payroll; §3.3, §3.4, §3.5, §3.7

## Context
The SOW references HRMS, Payroll, NIC SMS, and FCM integrations but does not provide their API contracts.

## Decision
- Define clean interfaces in the Application layer for each external system
- Infrastructure provides placeholder implementations that log calls
- Actual implementations will adapt to the real API contracts when available
- Use `IHttpClientFactory` with typed clients for HTTP-based integrations
- Configure: timeout, retry (transient failures only), circuit breaker, cancellation
- **Do NOT** automatically retry non-idempotent operations
- **Do NOT** fabricate external API endpoints, request schemas, or credentials

### Interfaces
| Interface | Purpose | Placeholder |
|-----------|---------|-------------|
| `INicSmsService` | OTP SMS delivery | Logs OTP (dev only) |
| `IPushNotificationService` | FCM/APNs notifications | Logs notification |
| `IHrmsService` | Personnel verification, PIS data | Returns "found" |
| `IPayrollService` | Salary/Form-16 retrieval | Empty (TBD) |
| `IFileStorageService` | Document storage | Local filesystem |

## Consequences
- Application logic is decoupled from external API details
- External integrations can be implemented/swapped independently
- Placeholder implementations enable development without external dependencies
